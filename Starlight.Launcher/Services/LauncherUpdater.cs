using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Starlight.Launcher.Services.Settings;
using Starlight.Launcher.WebUI.Models.LauncherUpdater;

namespace Starlight.Launcher.Services;

public partial class LauncherUpdater
{
    private readonly SettingsService _settings;

    public LauncherUpdater(SettingsService settings) => _settings = settings;

    public static string GetVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var infoVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        var version = infoVersion ?? assembly.GetName().Version?.ToString() ?? "";
        return version.Split('+')[0];
    }

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

    public async Task<IReadOnlyList<ChangelogEntry>> GetChangelogsToShow()
    {
        var lastSeen = ParseVersion(NormalizeVersion(_settings.GetSettings().LastSeenChangelogVersion));
        var current = ParseVersion(NormalizeVersion(GetVersion()));

        var releases = await GetAllReleases();

        return releases
            .Select(r => (r.TagName, r.Body, Parsed: ParseVersion(NormalizeVersion(r.TagName))))
            .Where(r => r.Parsed is not null)
            .Where(r => lastSeen is null || r.Parsed! > lastSeen)
            .Where(r => current is null || r.Parsed! <= current)
            .OrderByDescending(r => r.Parsed)
            .Select(r => new ChangelogEntry(NormalizeVersion(r.TagName), r.Body))
            .ToList();
    }

    private async Task<IReadOnlyList<(string? TagName, string? Body)>> GetAllReleases()
    {
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Starlight.Launcher");

        try
        {
            using var response = await httpClient.GetAsync(
                "https://api.github.com/repos/ss14Starlight/Starlight.Launcher/releases?per_page=50",
                HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(json);

            var result = new List<(string?, string?)>();
            foreach (var el in document.RootElement.EnumerateArray())
            {
                var tag = el.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
                var body = el.TryGetProperty("body", out var b) ? b.GetString() : null;
                result.Add((tag, body));
            }
            return result;
        }
        catch
        {
            return Array.Empty<(string?, string?)>();
        }
    }

    private static Version? ParseVersion(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var core = raw.Split('-')[0].Split('+')[0];
        return Version.TryParse(core, out var v) ? v : null;
    }

    private static string NormalizeVersion(string? version)
        => version?.Trim().TrimStart('v', 'V') ?? string.Empty;

    private static ReleaseAsset? PickAssetForCurrentOs(IReadOnlyList<ReleaseAsset> assets)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // e.g. "Starlight.Launcher-1.1.2-setup.exe"
            return assets.FirstOrDefault(a =>
                a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                a.Name.Contains("setup", StringComparison.OrdinalIgnoreCase));
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // e.g. "Starlight.Launcher-linux-x64-1.1.2.tar.gz"
            return assets.FirstOrDefault(a =>
                a.Name.Contains("linux-x64", StringComparison.OrdinalIgnoreCase) &&
                a.Name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase));
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // e.g. "Starlight.Launcher-osx-arm64-1.1.2.zip"
            var rid = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64";
            return assets.FirstOrDefault(a =>
                a.Name.Contains(rid, StringComparison.OrdinalIgnoreCase) &&
                a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        }

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
    /// Starts the update and asks the app to exit so files aren't locked during install.
    /// </summary>
    public static void RunInstallerAndExit(string downloadedPath, string installDir)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = downloadedPath,
                UseShellExecute = true
            });
            Environment.Exit(0);
            return;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            RunLinuxUpdate(downloadedPath, installDir);
            return;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            RunMacUpdate(downloadedPath, installDir);
            return;
        }

        Environment.Exit(0);
    }

    public static string GetMacAppBundleRoot(string baseDirectory) =>
        Path.GetFullPath(Path.Combine(baseDirectory, "..", ".."));

    private static void RunLinuxUpdate(string archivePath, string installDir)
    {
        var stagingDir = Path.Combine(Path.GetTempPath(), "starlight-update-" + Guid.NewGuid());
        Directory.CreateDirectory(stagingDir);

        using (var fileStream = File.OpenRead(archivePath))
        using (var gzip = new GZipStream(fileStream, CompressionMode.Decompress))
            TarFile.ExtractToDirectory(gzip, stagingDir, overwriteFiles: true);

        var pid = Environment.ProcessId;
        var exePath = Environment.ProcessPath!;

        var script = $"""
            #!/bin/sh
            while kill -0 {pid} 2>/dev/null; do sleep 0.2; done
            rm -rf "{installDir}"
            mv "{stagingDir}" "{installDir}"
            chmod +x "{installDir}/Starlight.Launcher"
            exec "{exePath}"
            """;

        RunDetachedShellScript(script);
        Environment.Exit(0);
    }

    private static void RunMacUpdate(string zipPath, string appBundlePath)
    {
        var stagingDir = Path.Combine(Path.GetTempPath(), "starlight-update-" + Guid.NewGuid());
        ZipFile.ExtractToDirectory(zipPath, stagingDir, overwriteFiles: true);

        // ditto -c --keepParent (used in CI) wraps the bundle as the top-level zip entry.
        var newAppPath = Path.Combine(stagingDir, "Starlight Launcher.app");
        var pid = Environment.ProcessId;

        var script = $"""
            #!/bin/sh
            while kill -0 {pid} 2>/dev/null; do sleep 0.2; done
            rm -rf "{appBundlePath}"
            mv "{newAppPath}" "{appBundlePath}"
            xattr -cr "{appBundlePath}" 2>/dev/null
            open "{appBundlePath}"
            """;

        RunDetachedShellScript(script);
        Environment.Exit(0);
    }

    private static void RunDetachedShellScript(string script)
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"starlight-update-{Guid.NewGuid()}.sh");
        File.WriteAllText(scriptPath, script);
        File.SetUnixFileMode(scriptPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        Process.Start(new ProcessStartInfo
        {
            FileName = "/bin/sh",
            ArgumentList = { scriptPath },
            UseShellExecute = false,
        });
    }
}
