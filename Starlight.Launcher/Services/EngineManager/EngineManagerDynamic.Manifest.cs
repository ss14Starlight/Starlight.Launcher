using Serilog;
using Starlight.Launcher.WebUI.Models.Data;
using Starlight.Launcher.WebUI.Models.Settings;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Starlight.Launcher.Services.EngineManager;

public sealed partial class EngineManagerDynamic
{
    // This part of the code is responsible for downloading and caching the Robust build manifest.

    private readonly SemaphoreSlim _manifestSemaphore = new(1);
    private readonly Stopwatch _manifestStopwatch = Stopwatch.StartNew();

    // One cache entry per CDN. Keyed by the CDN instance (stable while the list is static;
    // a reconfigured list yields new instances -> fresh caches, which is correct).
    private readonly Dictionary<string, CdnManifestCache> _manifestCaches = new();

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
            if (cache.Versions != null && cache.ValidUntil > _manifestStopwatch.Elapsed
                && FindVersionInfoInCached(cdn, cache, version, followRedirects) is { } found)
            {
                return found;
            }
        }

        // Pass 2: not found in any valid cache. Refresh per-CDN in priority order and re-check.
        // We refresh lazily so that as soon as a higher-priority CDN yields the version,
        // we stop without ever touching the lower-priority ones.
        // (This also re-requests a still-valid manifest on a total miss, which catches a
        //  freshly-published version within the cache window — same intent as the original code.)
        foreach (var cdn in cdns)
        {
            var cache = GetOrCreateCache(cdn);
            await UpdateBuildManifest(cdn, cache, cancel);

            if (FindVersionInfoInCached(cdn, cache, version, followRedirects) is { } found)
                return found;
        }

        return null;
    }

    private async Task UpdateBuildManifest(RobustCdn cdn, CdnManifestCache cache, CancellationToken cancel)
    {
        // TODO: If-Modified-Since and If-None-Match request conditions.

        var manifestUrl = cdn.BuildsManifest;
        Log.Debug("Loading manifest from {ManifestUrls}...", string.Join(", ", manifestUrl.Urls));

        cache.Versions = await manifestUrl.GetFromJsonAsync<Dictionary<string, VersionInfo>>(_http, cancel);

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
            while (versionInfo.RedirectVersion != null)
            {
                version = versionInfo.RedirectVersion;
                versionInfo = cache.Versions[versionInfo.RedirectVersion];
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
