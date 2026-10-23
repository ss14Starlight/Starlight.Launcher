using Serilog;
using Starlight.Launcher.WebUI.Models.Data;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.ExceptionServices;
using System.Text.Json.Serialization;

namespace Starlight.Launcher.Services.EngineManager;

public sealed partial class EngineManagerDynamic
{
    // This part of the code is responsible for downloading and caching the Robust build manifest.

    private readonly SemaphoreSlim _manifestSemaphore = new(1);
    private readonly Stopwatch _manifestStopwatch = Stopwatch.StartNew();

    // One cache entry per CDN. Keyed by the CDN instance (stable while the list is static;
    // a reconfigured list yields new instances -> fresh caches, which is correct).
    private readonly Dictionary<string, CdnManifestCache> _manifestCaches = [];

    /// <summary>
    /// Look up information about an engine version across all CDNs, in priority order.
    /// </summary>
    /// <param name="version">The version number to look up.</param>
    /// <param name="followRedirects">Follow redirections in version info.</param>
    /// <param name="cancel">Cancellation token.</param>
    /// <returns>
    /// Information about the version, or null if it could not be found on any CDN.
    /// The returned version may differ from what was requested if redirects were followed.
    /// </returns>
    private async ValueTask<FoundVersionInfo?> GetVersionInfo(
        string version,
        bool followRedirects = true,
        CancellationToken cancel = default)
    {
        await _manifestSemaphore.WaitAsync(cancel);
        try
        {
            return await GetVersionInfoCore(version, followRedirects, cancel);
        }
        finally
        {
            _ = _manifestSemaphore.Release();
        }
    }

    private async ValueTask<FoundVersionInfo?> GetVersionInfoCore(
        string version,
        bool followRedirects,
        CancellationToken cancel)
    {
        if (_manifestCachesDirty)
        {
            _manifestCachesDirty = false;
            _manifestCaches.Clear();
            Log.Debug("CDN list has changed, manifest caches cleared.");
        }

        var cdns = _cdns.Cdns;

        // Pass 1: use already-valid caches, honoring CDN priority.
        foreach (var cdn in cdns)
        {
            var cache = GetOrCreateCache(cdn);
            if (cache.Versions != null
                && cache.ValidUntil > _manifestStopwatch.Elapsed
                && FindVersionInfoInCached(cdn, cache, version, followRedirects) is { } found)
            {
                return found;
            }
        }

        // Pass 2: not found in any valid cache. Refresh per-CDN in priority order and re-check.
        // We refresh lazily so that as soon as a higher-priority CDN yields the version,
        // we stop without ever touching the lower-priority ones.
        // (This also re-requests a still-valid manifest on a total miss, which catches a
        //  freshly-published version within the cache window - same intent as the original code.)
        Exception? firstError = null;
        var failedCount = 0;
        foreach (var cdn in cdns)
        {
            var cache = GetOrCreateCache(cdn);
            try
            {
                await UpdateBuildManifest(cdn, cache, cancel);
            }
            catch (Exception e) when (!cancel.IsCancellationRequested)
            {
                Log.Warning(e, "Failed to load engine manifest from CDN {Cdn}", cdn.Name);
                firstError ??= e;
                failedCount++;
            }

            if (FindVersionInfoInCached(cdn, cache, version, followRedirects) is { } found)
                return found;
        }

        if (firstError != null && failedCount == cdns.Length)
            ExceptionDispatchInfo.Throw(firstError);

        return null;
    }

    private async Task<List<(string Url, RobustCdn Cdn)>> GetSameEngineBuildOnOtherCdns(
        FoundVersionInfo found,
        string rid,
        string sha256,
        CancellationToken cancel)
    {
        var result = new List<(string, RobustCdn)>();

        await _manifestSemaphore.WaitAsync(cancel);
        try
        {
            foreach (var cdn in _cdns.Cdns)
            {
                if (ReferenceEquals(cdn, found.Cdn))
                    continue;

                var cache = GetOrCreateCache(cdn);
                try
                {
                    if (cache.Versions == null || cache.ValidUntil <= _manifestStopwatch.Elapsed)
                        await UpdateBuildManifest(cdn, cache, cancel);
                }
                catch (Exception e) when (!cancel.IsCancellationRequested)
                {
                    Log.Warning(e, "Failed to load engine manifest from fallback CDN {Cdn}", cdn.Name);
                    continue;
                }

                if (FindVersionInfoInCached(cdn, cache, found.Version, followRedirects: false) is not { } other
                    || other.Info.Insecure
                    || !other.Info.Platforms.TryGetValue(rid, out var build)
                    || !string.Equals(build.Sha256, sha256, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                result.Add((build.Url, cdn));
            }
        }
        finally
        {
            _ = _manifestSemaphore.Release();
        }

        return result;
    }

    private async Task UpdateBuildManifest(RobustCdn cdn, CdnManifestCache cache, CancellationToken cancel)
    {
        var manifestUrl = cdn.BuildsManifest;
        Log.Debug("Loading manifest from {ManifestUrls}...", string.Join(", ", manifestUrl.Urls));

        // Conditional request: the manifest is large and rarely changes, so let the server answer 304.
        // Validators are shared across mirrors; a mismatching ETag just yields a full 200 response.
        var hasCached = cache.Versions != null;
        using var response = await manifestUrl.SendAsync(_http, url =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (hasCached)
            {
                if (cache.ETag != null)
                    request.Headers.IfNoneMatch.Add(cache.ETag);

                if (cache.LastModified is { } lastModified)
                    request.Headers.IfModifiedSince = lastModified;
            }

            return request;
        }, cancel);

        if (hasCached && response.StatusCode == HttpStatusCode.NotModified)
        {
            Log.Debug("Manifest not modified, keeping cached version.");
        }
        else
        {
            _ = response.EnsureSuccessStatusCode();
            cache.Versions = await response.Content.ReadFromJsonAsync<Dictionary<string, VersionInfo>>(cancel);
            cache.ETag = response.Headers.ETag;
            cache.LastModified = response.Content.Headers.LastModified;
        }

        cache.ValidUntil = _manifestStopwatch.Elapsed + _settings.GetSettings().RobustManifestCacheTime;
    }

    private static FoundVersionInfo? FindVersionInfoInCached(
        RobustCdn cdn,
        CdnManifestCache cache,
        string version,
        bool followRedirects)
    {
        if (cache.Versions == null)
            return null;

        if (!cache.Versions.TryGetValue(version, out var versionInfo))
            return null;

        if (followRedirects)
        {
            var visited = new HashSet<string> { version };
            while (versionInfo.RedirectVersion is { } redirect)
            {
                if (!visited.Add(redirect))
                {
                    Log.Warning("Redirect cycle in engine manifest of CDN {Cdn} at version {Version}", cdn.Name, redirect);
                    return null;
                }

                if (!cache.Versions.TryGetValue(redirect, out var redirectInfo))
                {
                    Log.Warning("Engine version {Version} on CDN {Cdn} redirects to missing version {Redirect}", version, cdn.Name, redirect);
                    return null;
                }

                version = redirect;
                versionInfo = redirectInfo;
            }
        }

        return new FoundVersionInfo(version, versionInfo, cdn);
    }

    private CdnManifestCache GetOrCreateCache(RobustCdn cdn)
    {
        var key = string.Join('|', cdn.BaseUrl.Urls);
        if (!_manifestCaches.TryGetValue(key, out var cache))
            _manifestCaches[key] = cache = new CdnManifestCache();

        return cache;
    }

    private sealed class CdnManifestCache
    {
        public Dictionary<string, VersionInfo>? Versions;
        public TimeSpan ValidUntil;
        public EntityTagHeaderValue? ETag;
        public DateTimeOffset? LastModified;
    }

    private sealed record FoundVersionInfo(string Version, VersionInfo Info, RobustCdn Cdn);

    private sealed record VersionInfo(
        bool Insecure,
        [property: JsonPropertyName("redirect")]
        string? RedirectVersion,
        Dictionary<string, BuildInfo> Platforms);

    private sealed class BuildInfo
    {
        [JsonInclude]
        [JsonPropertyName("url")]
        public string Url = default!;

        [JsonInclude]
        [JsonPropertyName("sha256")]
        public string Sha256 = default!;

        [JsonInclude]
        [JsonPropertyName("sig")]
        public string Signature = default!;
    }
}
