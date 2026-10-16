using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Starlight.Launcher.WebUI.Models.LauncherUpdater;

namespace Starlight.Launcher.Services.Settings;

public partial class LauncherUpdater(SettingsService settings)
{
    private readonly SettingsService _settings = settings;

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

    public static async Task<UpdateInfo> IsUpdateAvailable()
    {
        var currentVersion = NormalizeVersion(GetVersion());
        var current = ParseVersion(currentVersion);

#if DEBUG
        return new UpdateInfo(false, currentVersion, string.Empty, string.Empty, string.Empty, null);
#endif

#if RELEASE

        var releases = await GetReleases();

        if (releases is null)
            throw new InvalidOperationException("Failed to fetch releases.");

        var (Release, Parsed) = releases
            .Select(r => (Release: r, Parsed: ParseVersion(NormalizeVersion(r.TagName))))
            .Where(r => r.Parsed is not null)
            .OrderByDescending(r => r.Parsed)
            .FirstOrDefault();

        if (Release is null)
            return new UpdateInfo(false, currentVersion, string.Empty, string.Empty, string.Empty, null);

        var latestVersion = NormalizeVersion(Release.TagName);
        var isNewer = current is null || Parsed! > current;

        var asset = isNewer ? PickAssetForCurrentOs(Release.Assets) : null;

        return new UpdateInfo(
            isNewer,
            currentVersion,
            latestVersion,
            Release.HtmlUrl,
            Release.Body ?? string.Empty,
            asset);
#endif
    }

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
        var version = NormalizeVersion(GetVersion());
        _settings.UpdateSettings(s => s with { LastSeenChangelogVersion = version });
    }

    public async Task<IReadOnlyList<ChangelogEntry>> GetChangelogsToShow()
    {
        var lastSeen = ParseVersion(NormalizeVersion(_settings.GetSettings().LastSeenChangelogVersion));
        var current = ParseVersion(NormalizeVersion(GetVersion()));

        var releases = await GetReleases();

        return releases is null
            ? throw new InvalidOperationException("Failed to fetch releases.")
            : (IReadOnlyList<ChangelogEntry>)[.. releases
            .Select(r => (r.TagName, r.Body, Parsed: ParseVersion(NormalizeVersion(r.TagName))))
            .Where(r => r.Parsed is not null)
            .Where(r => lastSeen is null || r.Parsed! > lastSeen)
            .Where(r => current is null || r.Parsed! <= current)
            .OrderByDescending(r => r.Parsed)
            .Select(r => new ChangelogEntry(NormalizeVersion(r.TagName), r.Body))];
    }

    private sealed record GhReleaseInfo(string TagName, string HtmlUrl, string? Body, IReadOnlyList<ReleaseAsset> Assets);

    private static async Task<IReadOnlyList<GhReleaseInfo>?> GetReleases()
    {
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Starlight.Launcher");

        try
        {
            using var response = await httpClient.GetAsync(
                "https://api.github.com/repos/ss14Starlight/Starlight.Launcher/releases?per_page=50",
                HttpCompletionOption.ResponseHeadersRead);
            _ = response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(json);

            var result = new List<GhReleaseInfo>();
            foreach (var el in document.RootElement.EnumerateArray())
            {
                var tag = el.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
                if (string.IsNullOrWhiteSpace(tag))
                    continue;

                var htmlUrl = el.TryGetProperty("html_url", out var h) ? h.GetString() : null;
                var body = el.TryGetProperty("body", out var b) ? b.GetString() : null;

                var assets = new List<ReleaseAsset>();
                if (el.TryGetProperty("assets", out var assetsEl) && assetsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var a in assetsEl.EnumerateArray())
                    {
                        var name = a.TryGetProperty("name", out var n) ? n.GetString() : null;
                        var url = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                        var size = a.TryGetProperty("size", out var s) && s.TryGetInt64(out var sv) ? sv : 0;

                        if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(url))
                            assets.Add(new ReleaseAsset(name, url, size));
                    }
                }

                result.Add(new GhReleaseInfo(tag!, htmlUrl ?? "", body, assets));
            }

            return result;
        }
        catch
        {
            return null;
        }
    }

    public static async Task<IReadOnlyList<ChangelogEntry>> GetAllChangelogs()
    {
        var releases = await GetReleases();

        return releases is null
            ? throw new InvalidOperationException("Failed to fetch releases.")
            : (IReadOnlyList<ChangelogEntry>)[.. releases
            .Select(r => (r.TagName, r.Body, Parsed: ParseVersion(NormalizeVersion(r.TagName))))
            .Where(r => r.Parsed is not null)
            .OrderByDescending(r => r.Parsed)
            .Select(r => new ChangelogEntry(NormalizeVersion(r.TagName), r.Body))];
    }

    private static string NormalizeVersion(string? version)
        => version?.Trim().TrimStart('v', 'V') ?? string.Empty;

    private static Version? ParseVersion(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var core = raw.Split('-')[0].Split('+')[0];
        return Version.TryParse(core, out var v) ? v : null;
    }

#if RELEASE
    private static ReleaseAsset? PickAssetForCurrentOs(IReadOnlyList<ReleaseAsset> assets)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return assets.FirstOrDefault(a =>
                a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                a.Name.Contains("setup", StringComparison.OrdinalIgnoreCase));
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var extension = InstallKindDetector.Current switch
            {
                InstallKind.AppImage => ".AppImage",
                InstallKind.Deb => ".deb",
                InstallKind.Pacman => ".pkg.tar.zst",
                InstallKind.Rpm => ".rpm",
                InstallKind.Flatpak => ".flatpak",
                _ => ".tar.gz",
            };

            return assets.FirstOrDefault(a =>
                a.Name.Contains("linux-x64", StringComparison.OrdinalIgnoreCase) &&
                a.Name.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var rid = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64";
            return assets.FirstOrDefault(a =>
                a.Name.Contains(rid, StringComparison.OrdinalIgnoreCase) &&
                a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }
#endif

    /// <summary>
    /// Downloads the asset to the launcher data folder and returns the local path.
    /// </summary>
    public async Task<string> DownloadAsset(ReleaseAsset asset, CancellationToken ct = default)
    {
        var dir = GetUpdateFolder();
        _ = Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, asset.Name);

        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Starlight.Launcher");

        using var response = await httpClient.GetAsync(
            asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        _ = response.EnsureSuccessStatusCode();

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
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
        catch (Exception ex)
        {
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
            _ = Process.Start(new ProcessStartInfo
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

    private static void RunLinuxUpdate(string downloadedPath, string installDir)
    {
        switch (InstallKindDetector.Current)
        {
            case InstallKind.Flatpak:
                RunFlatpakUpdate(downloadedPath);
                return;
            case InstallKind.AppImage:
                RunAppImageUpdate(downloadedPath);
                return;
            case InstallKind.Deb:
                RunSystemPackageUpdate(downloadedPath, $"apt-get install -y --allow-downgrades {Quote(downloadedPath)}");
                return;
            case InstallKind.Pacman:
                RunSystemPackageUpdate(downloadedPath, $"pacman -U --noconfirm {Quote(downloadedPath)}");
                return;
            case InstallKind.Rpm:
                RunSystemPackageUpdate(downloadedPath,
                    $"if command -v dnf >/dev/null; then dnf install -y {Quote(downloadedPath)}; " +
                    $"else zypper --non-interactive install --allow-unsigned-rpm {Quote(downloadedPath)}; fi");
                return;
            default:
                RunTarballUpdate(downloadedPath, installDir);
                return;
        }
    }

    private const string FlatpakUpdateApplied = "UPDATE-APPLIED";
    private const string FlatpakUpdateNotApplied = "UPDATE-NOT-APPLIED";

    private static string FlatpakUpdateLogPath =>
        Path.Combine(
            Environment.GetEnvironmentVariable("XDG_CACHE_HOME")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache"),
            "flatpak-update.log");

    private static void RunFlatpakUpdate(string bundlePath)
    {
        var appId = Environment.GetEnvironmentVariable("FLATPAK_ID")
            ?? throw new InvalidOperationException("FLATPAK_ID is not set.");

        var logPath = FlatpakUpdateLogPath;
        _ = Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

        var hostScript = $"""
            exec >{Quote(logPath)} 2>&1 </dev/null
            app={Quote(appId)}
            echo "Waiting for $app to exit"
            i=0
            while flatpak ps --columns=application | grep -qx "$app"; do
                i=$((i + 1))
                if [ "$i" -eq 150 ]; then echo "Still running after 30s, killing it"; flatpak kill "$app"; fi
                if [ "$i" -ge 175 ]; then echo "Could not stop $app"; break; fi
                sleep 0.2
            done
            echo "$(flatpak --version), user $(id -un)"
            echo "user install: $(flatpak info --user --show-commit "$app" 2>/dev/null || echo none)"
            echo "system install: $(flatpak info --system --show-commit "$app" 2>/dev/null || echo none)"
            if flatpak info --user "$app" >/dev/null 2>&1; then scope=--user; else scope=--system; fi
            before=$(flatpak info "$scope" --show-commit "$app" 2>/dev/null)
            echo "Installing update ($scope) over commit $before:" {Quote(bundlePath)}
            flatpak install "$scope" -y --reinstall --bundle {Quote(bundlePath)}
            rc=$?
            if [ "$rc" -ne 0 ]; then
                echo "flatpak install $scope failed with exit code $rc"
                # A system install needs polkit, which can't ask for a password from here (SteamOS'
                # deck user has none by default). A user install needs no rights and flatpak run
                # prefers it over the system one.
                if [ "$scope" = --system ]; then
                    echo "Retrying as a user install"
                    scope=--user
                    before=$(flatpak info --user --show-commit "$app" 2>/dev/null)
                    flatpak install --user -y --reinstall --bundle {Quote(bundlePath)} || echo "flatpak install --user failed with exit code $?"
                fi
            fi
            after=$(flatpak info "$scope" --show-commit "$app" 2>/dev/null)
            if [ -n "$after" ] && [ "$after" != "$before" ]; then echo "{FlatpakUpdateApplied} $scope $after"; else echo "{FlatpakUpdateNotApplied}"; fi
            # Not into the log: the launcher's own console output would bury the lines above.
            exec flatpak run "$app" >/dev/null 2>&1
            """;

        _ = Process.Start(new ProcessStartInfo
        {
            FileName = "flatpak-spawn",
            ArgumentList = { "--host", "sh", "-c", hostScript },
            UseShellExecute = false,
        }) ?? throw new InvalidOperationException("Failed to start flatpak-spawn.");

        Thread.Sleep(500);
        Environment.Exit(0);
    }

    /// <summary>
    ///     If the last Flatpak self-update didn't apply, returns the end of its log (once).
    /// </summary>
    public static string? TakeFailedUpdateReport()
    {
        if (InstallKindDetector.Current != InstallKind.Flatpak)
            return null;

        var logPath = FlatpakUpdateLogPath;
        try
        {
            if (!File.Exists(logPath))
                return null;

            var lines = File.ReadAllLines(logPath);
            File.Move(logPath, logPath + ".last", overwrite: true);

            if (lines.Any(l => l.StartsWith(FlatpakUpdateApplied, StringComparison.Ordinal)))
                return null;

            var script = lines
                .TakeWhile(l => !l.StartsWith(FlatpakUpdateNotApplied, StringComparison.Ordinal))
                .Where(l => !l.StartsWith('['))
                .TakeLast(25);

            return string.Join('\n', script);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void RunAppImageUpdate(string newAppImagePath)
    {
        var target = Environment.GetEnvironmentVariable("APPIMAGE")
            ?? throw new InvalidOperationException("APPIMAGE is not set.");
        var pid = Environment.ProcessId;

        var script = $"""
            #!/bin/sh
            while kill -0 {pid} 2>/dev/null; do sleep 0.2; done
            mv -f {Quote(newAppImagePath)} {Quote(target)}
            chmod +x {Quote(target)}
            exec {Quote(target)}
            """;

        RunDetachedShellScript(script);
        Environment.Exit(0);
    }

    private static void RunSystemPackageUpdate(string packagePath, string installCommand)
    {
        if (!CommandExists("pkexec"))
            throw new InvalidOperationException($"pkexec was not found. Install the update manually: {packagePath}");

        var pid = Environment.ProcessId;
        var exePath = Environment.ProcessPath!;

        var script = $"""
            #!/bin/sh
            while kill -0 {pid} 2>/dev/null; do sleep 0.2; done
            pkexec sh -c {Quote(installCommand)}
            exec {Quote(exePath)}
            """;

        RunDetachedShellScript(script);
        Environment.Exit(0);
    }

    private static bool CommandExists(string name)
        => (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(':', StringSplitOptions.RemoveEmptyEntries)
            .Any(dir => File.Exists(Path.Combine(dir, name)));

    private static string Quote(string value) => "'" + value.Replace("'", "'\\''") + "'";

    private static void RunTarballUpdate(string archivePath, string installDir)
    {
        var stagingDir = Path.Combine(Path.GetTempPath(), "starlight-update-" + Guid.NewGuid());
        _ = Directory.CreateDirectory(stagingDir);

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
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        var scriptPath = Path.Combine(Path.GetTempPath(), $"starlight-update-{Guid.NewGuid()}.sh");
        File.WriteAllText(scriptPath, script);
        File.SetUnixFileMode(scriptPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        _ = Process.Start(new ProcessStartInfo
        {
            FileName = "/bin/sh",
            ArgumentList = { scriptPath },
            UseShellExecute = false,
        });
    }
}
