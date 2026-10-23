using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SpaceWizards.Sodium;
using Starlight.Launcher.WebUI.Models.LauncherUpdater;

namespace Starlight.Launcher.Services.Settings;

public class LauncherUpdater(SettingsService settings, HttpClient httpClient)
{
    private const string UserAgent = "Starlight.Launcher";

    private readonly SettingsService _settings = settings;
    private readonly HttpClient _httpClient = httpClient;

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

    /// <summary>
    /// Development builds never offer updates. Not a <c>#if</c>, so the release path below is still compiled in Debug.
    /// </summary>
#if DEBUG
    private static bool s_skipUpdateCheck => true;
#else
    private static bool s_skipUpdateCheck => false;
#endif

    public async Task<UpdateInfo> IsUpdateAvailable()
    {
        var currentVersion = NormalizeVersion(GetVersion());
        if (s_skipUpdateCheck)
            return new UpdateInfo(false, currentVersion, string.Empty, string.Empty, string.Empty, null);

        var current = ParseVersion(currentVersion);

        var releases = await GetReleases() ?? throw new InvalidOperationException("Failed to fetch releases.");

        var (release, parsed) = releases
            .Select(r => (Release: r, Parsed: ParseVersion(NormalizeVersion(r.TagName))))
            .Where(r => r.Parsed is not null)
            .OrderByDescending(r => r.Parsed)
            .FirstOrDefault();

        if (release is null)
            return new UpdateInfo(false, currentVersion, string.Empty, string.Empty, string.Empty, null);

        var latestVersion = NormalizeVersion(release.TagName);
        var isNewer = current is null || parsed > current;

        var asset = isNewer ? PickAssetForCurrentOs(release.Assets) : null;

        return new UpdateInfo(
            isNewer,
            currentVersion,
            latestVersion,
            release.HtmlUrl,
            release.Body ?? string.Empty,
            asset);
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
            : [.. releases
                .Select(r => (r.TagName, r.Body, Parsed: ParseVersion(NormalizeVersion(r.TagName))))
                .Where(r => r.Parsed is not null && (lastSeen is null || r.Parsed > lastSeen) && (current is null || r.Parsed <= current))
                .OrderByDescending(r => r.Parsed)
                .Select(r => new ChangelogEntry(NormalizeVersion(r.TagName), r.Body))];
    }

    private sealed record GhReleaseInfo(string TagName, string HtmlUrl, string? Body, IReadOnlyList<ReleaseAsset> Assets);

    private async Task<IReadOnlyList<GhReleaseInfo>?> GetReleases()
    {
        var includePrereleases = _settings.GetSettings().ReceivePrereleases;

        try
        {
            using var request = CreateGetRequest(
                "https://api.github.com/repos/ss14Starlight/Starlight.Launcher/releases?per_page=50");
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            _ = response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(json);

            var result = new List<GhReleaseInfo>();
            foreach (var el in document.RootElement.EnumerateArray())
            {
                var tag = el.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
                if (string.IsNullOrWhiteSpace(tag))
                    continue;

                var isPrerelease = (el.TryGetProperty("prerelease", out var p) && p.ValueKind == JsonValueKind.True)
                    || NormalizeVersion(tag).Contains('-');
                if (isPrerelease && !includePrereleases)
                    continue;

                var htmlUrl = el.TryGetProperty("html_url", out var h) ? h.GetString() : null;
                var body = el.TryGetProperty("body", out var b) ? b.GetString() : null;

                var assets = new List<(string Name, string Url, long Size, string? Digest)>();
                if (el.TryGetProperty("assets", out var assetsEl) && assetsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var a in assetsEl.EnumerateArray())
                    {
                        var name = a.TryGetProperty("name", out var n) ? n.GetString() : null;
                        var url = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                        var size = a.TryGetProperty("size", out var s) && s.TryGetInt64(out var sv) ? sv : 0;
                        var digest = a.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;

                        if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(url))
                            assets.Add((name, url, size, digest));
                    }
                }

                var signatures = assets
                    .Where(a => a.Name.EndsWith(SignatureExtension, StringComparison.Ordinal))
                    .GroupBy(a => a.Name[..^SignatureExtension.Length], StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.First().Url, StringComparer.Ordinal);

                result.Add(new GhReleaseInfo(tag, htmlUrl ?? "", body, [.. assets
                    .Where(a => !a.Name.EndsWith(SignatureExtension, StringComparison.Ordinal))
                    .Select(a => new ReleaseAsset(a.Name, a.Url, a.Size, ParseSha256Digest(a.Digest), signatures.GetValueOrDefault(a.Name)))]));
            }

            return result;
        }
        catch
        {
            return null;
        }
    }

    private static string? ParseSha256Digest(string? digest)
    {
        const string Prefix = "sha256:";
        if (digest?.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) != true)
            return null;

        var hex = digest[Prefix.Length..];
        return hex.Length == SHA256.HashSizeInBytes * 2 && hex.All(char.IsAsciiHexDigit) ? hex : null;
    }

    public async Task<IReadOnlyList<ChangelogEntry>> GetAllChangelogs()
    {
        var releases = await GetReleases();

        return releases is null
            ? throw new InvalidOperationException("Failed to fetch releases.")
            : [.. releases
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

    private static ReleaseAsset? PickAssetForCurrentOs(IReadOnlyList<ReleaseAsset> assets)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return assets.FirstOrDefault(a =>
                a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    && a.Name.Contains("setup", StringComparison.OrdinalIgnoreCase));
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
                a.Name.Contains("linux-x64", StringComparison.OrdinalIgnoreCase)
                    && a.Name.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var rid = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64";
            return assets.FirstOrDefault(a =>
                a.Name.Contains(rid, StringComparison.OrdinalIgnoreCase)
                    && a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private const string SignatureExtension = ".minisig";

    private const string ReleasePublicKeyResource = "minisign.pub";

    /// <summary>
    /// The key releases are signed with, embedded from <c>PublishFiles/minisign.pub</c>. The SHA-256 digest comes from the
    /// same GitHub API as the file itself, so only this signature protects against a tampered release.
    /// </summary>
    private static readonly Lazy<MinisignVerifier.PublicKeyInfo?> _releasePublicKey = new(() =>
    {
        using var stream = typeof(LauncherUpdater).Assembly.GetManifestResourceStream(ReleasePublicKeyResource);
        if (stream is null)
            return null;

        using var reader = new StreamReader(stream);
        return MinisignVerifier.ParsePublicKey(reader.ReadToEnd());
    });

    /// <summary>
    /// Downloads the asset to the launcher data folder, checks its minisign signature and the SHA-256 digest
    /// GitHub reported for it and returns the local path.
    /// </summary>
    public async Task<string> DownloadAsset(ReleaseAsset asset, CancellationToken ct = default)
    {
        if (asset.Sha256 is null)
            throw new InvalidOperationException($"GitHub did not provide a SHA-256 digest for {asset.Name}, refusing to install it.");

        var publicKey = _releasePublicKey.Value
            ?? throw new InvalidOperationException("This build has no release signing key, so updates can't be verified. Install the update manually.");

        if (asset.SignatureUrl is null)
            throw new InvalidOperationException($"The release has no signature for {asset.Name}, refusing to install it.");

        var signature = await DownloadSignature(asset.SignatureUrl, ct);

        var dir = GetUpdateFolder();
        _ = Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, Path.GetFileName(asset.Name));

        try
        {
            var blake2b = await DownloadAndVerify(asset, path, ct);
            VerifySignature(asset, publicKey, signature, blake2b);
        }
        catch
        {
            TryDelete(path);
            throw;
        }

        return path;
    }

    private static HttpRequestMessage CreateGetRequest(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(UserAgent);
        return request;
    }

    private async Task<string> DownloadSignature(string url, CancellationToken ct)
    {
        // A real .minisig is a few hundred bytes.
        const int MaxSignatureSize = 4096;

        using var request = CreateGetRequest(url);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        _ = response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[MaxSignatureSize + 1];
        var length = 0;
        int read;
        while (length < buffer.Length && (read = await stream.ReadAsync(buffer.AsMemory(length), ct)) > 0)
            length += read;

        return length > MaxSignatureSize
            ? throw new InvalidDataException("The update signature is too large.")
            : Encoding.UTF8.GetString(buffer, 0, length);
    }

    private static void VerifySignature(ReleaseAsset asset, MinisignVerifier.PublicKeyInfo publicKey, string signature, byte[] blake2b)
    {
        var comment = MinisignVerifier.ParseTrustedComment(MinisignVerifier.Verify(publicKey, signature, blake2b));

        if (!comment.TryGetValue("file", out var signedName) || !string.Equals(signedName, asset.Name, StringComparison.Ordinal))
            throw new InvalidDataException($"The signature is for {signedName ?? "another file"}, not {asset.Name}.");

        var signedVersion = (comment.TryGetValue("version", out var v) ? ParseVersion(NormalizeVersion(v)) : null)
            ?? throw new InvalidDataException("The update signature does not name a version.");

        var current = ParseVersion(NormalizeVersion(GetVersion()));
        if (current is not null && signedVersion <= current)
            throw new InvalidDataException($"The signed update is version {signedVersion}, which is not newer than {current}.");
    }

    private async Task<byte[]> DownloadAndVerify(ReleaseAsset asset, string path, CancellationToken ct)
    {
        using var request = CreateGetRequest(asset.DownloadUrl);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        _ = response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? (asset.Size > 0 ? asset.Size : 0);

        await using var src = await response.Content.ReadAsStreamAsync(ct);
        await using var dst = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        CryptoGenericHashBlake2B.State blake2b;
        _ = CryptoGenericHashBlake2B.Init(ref blake2b, [], MinisignVerifier.HashLength);

        var buffer = new byte[81920];
        long downloaded = 0;
        int read;
        DownloadProgress?.Invoke((0, total));

        while ((read = await src.ReadAsync(buffer, ct)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, read), ct);
            sha.AppendData(buffer, 0, read);
            _ = CryptoGenericHashBlake2B.Update(ref blake2b, buffer.AsSpan(0, read));
            downloaded += read;
            DownloadProgress?.Invoke((downloaded, total));
        }

        var actual = Convert.ToHexString(sha.GetHashAndReset());
        if (!string.Equals(actual, asset.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Checksum mismatch for {asset.Name}: expected {asset.Sha256}, got {actual}. The download may be corrupted or tampered with.");
        }

        var hash = new byte[MinisignVerifier.HashLength];
        _ = CryptoGenericHashBlake2B.Final(ref blake2b, hash);
        return hash;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
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

    public static string GetMacAppBundleRoot(string baseDirectory)
        => Path.GetFullPath(Path.Combine(baseDirectory, "..", ".."));

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
                    $"if command -v dnf >/dev/null; then dnf install -y {Quote(downloadedPath)}; "
                        + $"else zypper --non-interactive install --allow-unsigned-rpm {Quote(downloadedPath)}; fi");
                return;
            default:
                RunTarballUpdate(downloadedPath, installDir);
                return;
        }
    }

    private const string FlatpakUpdateApplied = "UPDATE-APPLIED";
    private const string FlatpakUpdateNotApplied = "UPDATE-NOT-APPLIED";

    private static string s_flatpakUpdateLogPath
        => Path.Combine(
            Environment.GetEnvironmentVariable("XDG_CACHE_HOME")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache"),
            "flatpak-update.log");

    private static void RunFlatpakUpdate(string bundlePath)
    {
        var appId = Environment.GetEnvironmentVariable("FLATPAK_ID")
            ?? throw new InvalidOperationException("FLATPAK_ID is not set.");

        var logPath = s_flatpakUpdateLogPath;
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
        })
            ?? throw new InvalidOperationException("Failed to start flatpak-spawn.");

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

        var logPath = s_flatpakUpdateLogPath;
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

    private const string LauncherExecutableName = "Starlight.Launcher";

    private const string LauncherFilesManifest = ".launcher-files";

    private static void RunTarballUpdate(string archivePath, string installDir)
    {
        installDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installDir));
        if (!File.Exists(Path.Combine(installDir, LauncherExecutableName)))
            throw new InvalidOperationException($"{installDir} doesn't look like a launcher install. Install the update manually: {archivePath}");

        var id = Guid.NewGuid().ToString("N");
        var stagingDir = Path.Combine(installDir, $".starlight-update-{id}");
        var backupDir = Path.Combine(installDir, $".starlight-backup-{id}");

        string packageDir;
        List<string> entries;
        try
        {
            _ = Directory.CreateDirectory(stagingDir);
            using (var fileStream = File.OpenRead(archivePath))
            using (var gzip = new GZipStream(fileStream, CompressionMode.Decompress))
                TarFile.ExtractToDirectory(gzip, stagingDir, overwriteFiles: true);

            packageDir = FindTarballPackageRoot(stagingDir);
            var stale = ReadLauncherFilesManifest(installDir);

            File.WriteAllLines(Path.Combine(packageDir, LauncherFilesManifest),
                Directory.EnumerateFileSystemEntries(packageDir).Select(Path.GetFileName).OfType<string>().Append(LauncherFilesManifest).Distinct());

            var packageEntries = Directory.EnumerateFileSystemEntries(packageDir).Select(Path.GetFileName).OfType<string>().ToList();
            entries = [.. packageEntries, .. stale.Except(packageEntries, StringComparer.Ordinal)];
        }
        catch
        {
            TryDeleteDirectory(stagingDir);
            throw;
        }

        var pid = Environment.ProcessId;
        var exePath = Environment.ProcessPath ?? Path.Combine(installDir, LauncherExecutableName);
        var swaps = string.Join('\n', entries.Select(e => $"[ -n \"$failed\" ] || swap {Quote(e)} || failed=1"));

        // Each old entry is moved into the backup folder before the new one takes its place; on any failure the
        // moved ones are put back, so the launcher never ends up half-installed or gone.
        var script = $$"""
            #!/bin/sh
            dir={{Quote(installDir)}}
            new={{Quote(packageDir)}}
            staging={{Quote(stagingDir)}}
            backup={{Quote(backupDir)}}
            while kill -0 {{pid}} 2>/dev/null; do sleep 0.2; done
            moved=
            failed=
            mkdir "$backup" || failed=1
            swap() {
                if [ -e "$dir/$1" ] || [ -L "$dir/$1" ]; then mv "$dir/$1" "$backup/$1" || return 1; fi
                moved="$moved$1
            "
                if [ -e "$new/$1" ] || [ -L "$new/$1" ]; then mv "$new/$1" "$dir/$1" || return 1; fi
            }
            {{swaps}}
            if [ -n "$failed" ]; then
                printf '%s' "$moved" | while IFS= read -r name; do
                    [ -n "$name" ] || continue
                    rm -rf "$dir/$name"
                    if [ -e "$backup/$name" ] || [ -L "$backup/$name" ]; then mv "$backup/$name" "$dir/$name"; fi
                done
                rmdir "$backup" 2>/dev/null
            else
                rm -rf "$backup"
            fi
            rm -rf "$staging"
            chmod +x "$dir/{{LauncherExecutableName}}" "$dir/loader/Robust.Loader" "$dir/loader/createdump" 2>/dev/null
            exec {{Quote(exePath)}}
            """;

        RunDetachedShellScript(script);
        Environment.Exit(0);
    }

    private static string FindTarballPackageRoot(string extractedDir)
    {
        if (File.Exists(Path.Combine(extractedDir, LauncherExecutableName)))
            return extractedDir;

        var children = Directory.GetFileSystemEntries(extractedDir);
        if (children.Length == 1 && File.Exists(Path.Combine(children[0], LauncherExecutableName)))
            return children[0];

        throw new InvalidOperationException($"The downloaded archive has no {LauncherExecutableName} in it.");
    }

    private static IEnumerable<string> ReadLauncherFilesManifest(string installDir)
    {
        var path = Path.Combine(installDir, LauncherFilesManifest);
        if (!File.Exists(path))
            return [];

        return File.ReadAllLines(path)
            .Where(n => n.Length > 0 && n != "." && n != ".." && !n.Contains('/') && !n.Contains('\0'))
            .Where(n => File.Exists(Path.Combine(installDir, n)) || Directory.Exists(Path.Combine(installDir, n)))
            .ToList();
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void RunMacUpdate(string zipPath, string appBundlePath)
    {
        appBundlePath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(appBundlePath));
        if (!appBundlePath.EndsWith(".app", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(Path.Combine(appBundlePath, "Contents", "MacOS", LauncherExecutableName)))
            throw new InvalidOperationException($"{appBundlePath} isn't the launcher's app bundle. Install the update manually: {zipPath}");

        // Next to the bundle, so swapping it is a rename within one filesystem.
        var stagingDir = Path.Combine(Path.GetDirectoryName(appBundlePath)!, $".starlight-update-{Guid.NewGuid():N}");
        var newAppPath = Path.Combine(stagingDir, "Starlight Launcher.app");
        try
        {
            ZipFile.ExtractToDirectory(zipPath, stagingDir, overwriteFiles: true);
            if (!File.Exists(Path.Combine(newAppPath, "Contents", "MacOS", LauncherExecutableName)))
                throw new InvalidOperationException("The downloaded archive has no Starlight Launcher.app in it.");
        }
        catch
        {
            TryDeleteDirectory(stagingDir);
            throw;
        }

        var pid = Environment.ProcessId;

        var script = $"""
            #!/bin/sh
            app={Quote(appBundlePath)}
            new={Quote(newAppPath)}
            staging={Quote(stagingDir)}
            old="$staging/old.app"
            while kill -0 {pid} 2>/dev/null; do sleep 0.2; done
            if mv "$app" "$old"; then
                mv "$new" "$app" || mv "$old" "$app"
            fi
            # Only once a bundle is back in place: otherwise the old one inside is the last copy.
            if [ -d "$app" ]; then rm -rf "$staging"; fi
            xattr -cr "$app" 2>/dev/null
            open "$app"
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
