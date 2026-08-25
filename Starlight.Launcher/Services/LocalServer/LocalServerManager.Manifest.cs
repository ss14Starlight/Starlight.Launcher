using System.Net.Http.Json;
using System.Text.Json;
using Robust.Launcher.Api.Utility;
using Serilog;
using Starlight.Launcher.WebUI.Models.LocalServer;

namespace Starlight.Launcher.Services.LocalServer;

public sealed partial class LocalServerManager
{
    public async Task<LocalServerManifest?> FetchManifestAsync(string manifestUrl, CancellationToken cancel = default)
    {
        if (!Uri.TryCreate(manifestUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("Manifest URL must be an absolute http(s) URL.", nameof(manifestUrl));

        return await _http.GetFromJsonAsync<LocalServerManifest>(uri, Helpers.JsonWebOptions, cancel);
    }

    public async Task<LocalServerLatestBuildResult> GetLatestBuildAsync(string manifestUrl, CancellationToken cancel = default)
    {
        try
        {
            var manifest = await FetchManifestAsync(manifestUrl, cancel);
            if (manifest == null || manifest.Builds.Count == 0)
                return new LocalServerLatestBuildResult(null, null, null, null, false, "Manifest contains no builds.");

            var latest = manifest.Builds.MaxBy(kv => kv.Value.Time);
            var build = latest.Value;

            var rid = RidUtility.FindBestRid(build.Server.Keys);
            if (rid == null || !build.Server.TryGetValue(rid, out var asset))
                return new LocalServerLatestBuildResult(latest.Key, build.Time, null, null, false, "No server build available for your platform.");

            return new LocalServerLatestBuildResult(latest.Key, build.Time, rid, asset.Size, true, null);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or ArgumentException)
        {
            Log.Warning(e, "Failed to fetch local server manifest from {ManifestUrl}", manifestUrl);
            return new LocalServerLatestBuildResult(null, null, null, null, false, e.Message);
        }
    }
}
