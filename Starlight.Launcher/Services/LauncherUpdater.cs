using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Starlight.Launcher.Services.Settings;
using Starlight.Launcher.WebUI.Models.LauncherUpdater;

namespace Starlight.Launcher.Services;

public partial class LauncherUpdater
{
    private readonly SettingsService _settings;

    public LauncherUpdater(SettingsService settings) => _settings = settings;

    public static string GetVersion() => Environment.ProcessPath == null
        ? ""
        : FileVersionInfo.GetVersionInfo(Environment.ProcessPath).ProductVersion?.Split('+')[0] ?? "";

    // Progress reporting for the download. (downloaded, total) — total == 0 means unknown.
    public event Action<(long downloaded, long total)>? DownloadProgress;

    public async Task<UpdateInfo> IsUpdateAvailable()
    {
        var (tagName, htmlUrl, body, assets) = await GetLatestRelease();
        var currentVersion = NormalizeVersion(GetVersion());
        var latestVersion = NormalizeVersion(tagName);

        Console.WriteLine($"Current version: {currentVersion}");
        Console.WriteLine($"Latest version: {latestVersion}");

        var asset = PickAssetForCurrentOs(assets);

        return new UpdateInfo(
            !string.Equals(currentVersion, latestVersion, StringComparison.OrdinalIgnoreCase),
            currentVersion,
            latestVersion,
            htmlUrl ?? string.Empty,
            body ?? string.Empty,
            asset);
    }

    /// <summary>
    /// True if the currently running version differs from the last version
    /// for which we showed the changelog. Used to show "what's new" after an update.
    /// </summary>
    public bool ShouldShowChangelog()
    {
        var current = NormalizeVersion(GetVersion());
        if (string.IsNullOrEmpty(current))
            return false;

        var lastSeen = NormalizeVersion(_settings.GetSettings().LastSeenChangelogVersion);
        return !string.Equals(current, lastSeen, StringComparison.OrdinalIgnoreCase);
    }

    public void MarkChangelogSeen()
    {
        var settings = _settings.GetSettings();
        settings.LastSeenChangelogVersion = NormalizeVersion(GetVersion());
        _settings.WriteSettings(settings);
    }

    public async Task<string?> GetChangelogForCurrentVersion()
    {
        var current = NormalizeVersion(GetVersion());
        if (string.IsNullOrEmpty(current))
            return null;

        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Starlight.Launcher");

        foreach (var tag in new[] { $"v{current}", current })
        {
            using var response = await httpClient.GetAsync(
                $"https://api.github.com/repos/ss14Starlight/Starlight.Launcher/releases/tags/{tag}",
                HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
                continue;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("body", out var b))
                return b.GetString();
        }
        return null;
    }

    private static string NormalizeVersion(string? version)
        => version?.Trim().TrimStart('v', 'V') ?? string.Empty;

    // OS-specific asset selection. Today only Windows; the switch makes adding Linux/Mac trivial later.
    private static ReleaseAsset? PickAssetForCurrentOs(IReadOnlyList<ReleaseAsset> assets)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // e.g. "Starlight.Launcher-1.1.2-setup.exe"
            return assets.FirstOrDefault(a =>
                a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                a.Name.Contains("setup", StringComparison.OrdinalIgnoreCase));
        }

        // TODO Linux:  return assets.FirstOrDefault(a => a.Name.EndsWith(".AppImage", ...));
        // TODO macOS:  return assets.FirstOrDefault(a => a.Name.EndsWith(".dmg", ...));
        return null;
    }

    private async Task<(string? TagName, string? HtmlUrl, string? Body, IReadOnlyList<ReleaseAsset> Assets)> GetLatestRelease()
    {
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Starlight.Launcher");

        using var response = await httpClient.GetAsync(
            "https://api.github.com/repos/ss14Starlight/Starlight.Launcher/releases/latest",
            HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        string responseBody = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(responseBody);

        document.RootElement.TryGetProperty("tag_name", out var tagName);
        document.RootElement.TryGetProperty("html_url", out var htmlUrl);
        document.RootElement.TryGetProperty("body", out var body);

        var assets = new List<ReleaseAsset>();
        if (document.RootElement.TryGetProperty("assets", out var assetsEl) &&
            assetsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in assetsEl.EnumerateArray())
            {
                var name = a.TryGetProperty("name", out var n) ? n.GetString() : null;
                var url = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                long size = a.TryGetProperty("size", out var s) && s.TryGetInt64(out var sv) ? sv : 0;

                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(url))
                    assets.Add(new ReleaseAsset(name, url, size));
            }
        }

        return (tagName.GetString(), htmlUrl.GetString(), body.GetString(), assets);
    }

    /// <summary>
    /// Downloads the asset to the launcher data folder and returns the local path.
    /// </summary>
    public async Task<string> DownloadAsset(ReleaseAsset asset, CancellationToken ct = default)
    {
        var dir = GetUpdateFolder();
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, asset.Name);

        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Starlight.Launcher");

        using var response = await httpClient.GetAsync(
            asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        // Content-Length can be null; fall back to the size from the API.
        var total = response.Content.Headers.ContentLength ?? (asset.Size > 0 ? asset.Size : 0);

        await using var src = await response.Content.ReadAsStreamAsync(ct);
        await using var dst = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);

        var buffer = new byte[81920];
        long downloaded = 0;
        int read;
        DownloadProgress?.Invoke((0, total));

        while ((read = await src.ReadAsync(buffer, ct)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, read), ct);
            downloaded += read;
            DownloadProgress?.Invoke((downloaded, total));
        }

        return path;
    }

    private string GetUpdateFolder() => Path.Combine(_settings.GetSettings().DirLauncherData, "updates");

    // Removes previously downloaded installers. Call on launcher startup:
    // at that point any installer from a past update is already unlocked.
    public void CleanupOldInstallers()
    {
        try
        {
            var dir = GetUpdateFolder();
            if (!Directory.Exists(dir))
                return;

            foreach (var file in Directory.EnumerateFiles(dir))
            {
                try
                {
                    File.Delete(file);
                }
                catch (IOException)
                {
                    // File still locked (rare) — skip it, we'll get it next launch.
                }
                catch (UnauthorizedAccessException)
                {
                    // Same idea — don't let one stubborn file break startup.
                }
            }
        }
        catch (Exception ex)
        {
            // Cleanup must never crash startup.
            Console.WriteLine($"Installer cleanup failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Starts the installer and asks the app to exit so it isn't locked during install.
    /// </summary>
    public static void RunInstallerAndExit(string installerPath)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = installerPath,
                UseShellExecute = true
            });
        }
        // TODO Linux/macOS: chmod +x the AppImage / open the .dmg, etc.

        // Give the OS a beat, then exit so the installer can replace files.
        Environment.Exit(0);
    }
}
