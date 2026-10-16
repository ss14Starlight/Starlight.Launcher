using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Robust.Launcher.Api.Models;
using Robust.Launcher.Api.Utility;
using Serilog;
using Starlight.Launcher.Services.Settings;
using Starlight.Launcher.WebUI.Models.LocalServer;
using static Robust.Launcher.Api.Utility.Helpers;

namespace Starlight.Launcher.Services.LocalServer.Dotnet;

public sealed record DotnetInstallation(
    string Root,
    string Muxer,
    IReadOnlyDictionary<string, List<RuntimeVersion>> Frameworks,
    bool IsLauncherManaged)
{
    public RuntimeVersion? NetCoreVersion =>
        Frameworks.TryGetValue(DotnetRuntimeRequirements.NetCoreApp, out var v) && v.Count > 0 ? v.Max() : null;
}

public readonly record struct DotnetInstallProgress(string Version, bool Installing, long Downloaded = 0, long Total = 0);

public sealed partial class DotnetRuntimeManager
{
    private const string InstallMarkerFileName = ".starlight-install-complete";
    private const string TempDirPrefix = ".tmp-";
    private const string UpdateCheckFilePrefix = ".update-check-";

    private const string ReleaseMetadataUrl = "https://builds.dotnet.microsoft.com/dotnet/release-metadata/{0}/releases.json";

    private static readonly TimeSpan SystemProbeTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromDays(3);

    private static string MuxerFileName => OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";

    private readonly HttpClient _http;
    private readonly Func<string> _cacheDirectory;

    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    private readonly Lock _updateLock = new();
    private readonly HashSet<string> _stagingDirs = new(StringComparer.Ordinal);
    private readonly HashSet<string> _updatingChannels = new(StringComparer.Ordinal);
    private readonly List<Task> _backgroundUpdates = [];

    public DotnetRuntimeManager(HttpClient http, SettingsService settings)
        : this(http, () => Path.Combine(settings.GetSettings().DirLauncherData, "dotnet"))
    {
    }

    internal DotnetRuntimeManager(HttpClient http, Func<string> cacheDirectory)
    {
        _http = http;
        _cacheDirectory = cacheDirectory;
    }

    private string CacheDirectory => _cacheDirectory();

    public async Task<DotnetInstallation> EnsureRuntimeAsync(
        DotnetRuntimeRequirements requirements,
        Action<DotnetInstallProgress>? progress,
        CancellationToken cancel)
    {
        await _cacheLock.WaitAsync(cancel);
        try
        {
            if (PickBest(ScanCache(), requirements) is { } cached)
            {
                ScheduleUpdateCheck(requirements, cached);
                return cached;
            }

            if (PickBest(await ProbeSystemAsync(cancel), requirements) is { } system)
                return system;

            return await DownloadAsync(requirements, progress, cancel);
        }
        finally
        {
            _ = _cacheLock.Release();
        }
    }

    public async Task PruneCacheAsync(IEnumerable<DotnetRuntimeRequirements> inUse)
    {
        await _cacheLock.WaitAsync();
        try
        {
            var cacheDir = CacheDirectory;
            if (!Directory.Exists(cacheDir))
                return;

            var installs = ScanCache();
            var keep = inUse
                .Select(r => PickBest(installs, r)?.Root)
                .OfType<string>()
                .ToHashSet(StringComparer.Ordinal);

            HashSet<string> staging;
            lock (_updateLock)
                staging = [.. _stagingDirs];

            foreach (var dir in Directory.EnumerateDirectories(cacheDir))
            {
                if (keep.Contains(dir) || staging.Contains(dir))
                    continue;

                try
                {
                    await Task.Run(() => Directory.Delete(dir, recursive: true));
                    Log.Information("Removed unused .NET runtime {Dir}", dir);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    Log.Warning(e, "Failed to remove unused .NET runtime {Dir}", dir);
                }
            }
        }
        finally
        {
            _ = _cacheLock.Release();
        }
    }

    private static DotnetInstallation? PickBest(IEnumerable<DotnetInstallation> installs, DotnetRuntimeRequirements requirements) =>
        installs
            .Where(i => requirements.IsSatisfiedBy(i.Frameworks))
            .MaxBy(i => i.NetCoreVersion);

    private List<DotnetInstallation> ScanCache()
    {
        var result = new List<DotnetInstallation>();
        var cacheDir = CacheDirectory;
        if (!Directory.Exists(cacheDir))
            return result;

        foreach (var dir in Directory.EnumerateDirectories(cacheDir))
        {
            if (Path.GetFileName(dir).StartsWith('.'))
                continue;

            var muxer = Path.Combine(dir, MuxerFileName);
            if (!File.Exists(Path.Combine(dir, InstallMarkerFileName)) || !File.Exists(muxer))
                continue;

            result.Add(new DotnetInstallation(dir, muxer, ScanSharedFrameworks(dir), IsLauncherManaged: true));
        }

        return result;
    }

    private static Dictionary<string, List<RuntimeVersion>> ScanSharedFrameworks(string root)
    {
        var frameworks = new Dictionary<string, List<RuntimeVersion>>(StringComparer.Ordinal);
        var shared = Path.Combine(root, "shared");
        if (!Directory.Exists(shared))
            return frameworks;

        foreach (var frameworkDir in Directory.EnumerateDirectories(shared))
        {
            var versions = Directory.EnumerateDirectories(frameworkDir)
                .Select(d => RuntimeVersion.TryParse(Path.GetFileName(d), out var v) ? v : (RuntimeVersion?)null)
                .OfType<RuntimeVersion>()
                .ToList();

            if (versions.Count > 0)
                frameworks[Path.GetFileName(frameworkDir)] = versions;
        }

        return frameworks;
    }

    [GeneratedRegex(@"^(?<name>\S+)\s+(?<version>\S+)\s+\[(?<path>.+)\]\s*$")]
    private static partial Regex ListRuntimesLineRegex();

    private static async Task<List<DotnetInstallation>> ProbeSystemAsync(CancellationToken cancel)
    {
        var candidates = new List<string>();
        if (Environment.GetEnvironmentVariable("DOTNET_ROOT") is { Length: > 0 } dotnetRoot)
            candidates.Add(Path.Combine(dotnetRoot, MuxerFileName));
        candidates.Add("dotnet");

        var byRoot = new Dictionary<string, Dictionary<string, List<RuntimeVersion>>>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            var output = await TryRunAsync(candidate, "--list-runtimes", cancel);
            if (output is null)
                continue;

            foreach (var line in output.Split('\n'))
            {
                var match = ListRuntimesLineRegex().Match(line.Trim());
                if (!match.Success || !RuntimeVersion.TryParse(match.Groups["version"].Value, out var version))
                    continue;

                var root = Path.GetDirectoryName(Path.GetDirectoryName(match.Groups["path"].Value.TrimEnd('/', '\\')));
                if (root is null)
                    continue;

                if (!byRoot.TryGetValue(root, out var frameworks))
                    byRoot[root] = frameworks = new Dictionary<string, List<RuntimeVersion>>(StringComparer.Ordinal);

                var name = match.Groups["name"].Value;
                if (!frameworks.TryGetValue(name, out var versions))
                    frameworks[name] = versions = [];
                if (!versions.Contains(version))
                    versions.Add(version);
            }
        }

        return byRoot
            .Select(kv => new DotnetInstallation(kv.Key, Path.Combine(kv.Key, MuxerFileName), kv.Value, IsLauncherManaged: false))
            .Where(i => File.Exists(i.Muxer))
            .ToList();
    }

    private static async Task<string?> TryRunAsync(string fileName, string argument, CancellationToken cancel)
    {
        Process? process = null;
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                ArgumentList = { argument },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";

            process = Process.Start(startInfo);
            if (process is null)
                return null;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(SystemProbeTimeout);

            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            _ = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);

            return process.ExitCode == 0 ? await stdout : null;
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            Log.Debug("Timed out probing system .NET via {FileName}", fileName);
            return null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
        finally
        {
            if (process is { HasExited: false })
            {
                try { process.Kill(); }
                catch (InvalidOperationException) { }
            }

            process?.Dispose();
        }
    }

    private sealed record RuntimeArchive(string Flavor, RuntimeVersion Version, string Rid, string Url, string Sha512);

    private async Task<DotnetInstallation> DownloadAsync(
        DotnetRuntimeRequirements requirements,
        Action<DotnetInstallProgress>? progress,
        CancellationToken cancel)
    {
        var displayVersion = requirements.DisplayVersion;

        RuntimeArchive archive;
        try
        {
            archive = await FindArchiveAsync(requirements, cancel)
                ?? throw new UpdateException($"No .NET {displayVersion} runtime is available for your platform.");
        }
        catch (Exception e) when (IsNetworkError(e, cancel))
        {
            throw new LocalServerRuntimeUnavailableException(displayVersion, e);
        }

        var tempDir = NewTempDir();
        try
        {
            await StageAsync(archive, tempDir, progress, displayVersion, cancel);
            var install = Commit(archive, tempDir);

            MarkUpdateChecked(requirements);

            return requirements.IsSatisfiedBy(install.Frameworks)
                ? install
                : throw new UpdateException($"Downloaded .NET {archive.Version} does not satisfy the server requirements.");
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    private string NewTempDir()
    {
        var cacheDir = CacheDirectory;
        EnsureDirectoryExists(cacheDir);
        return Path.Combine(cacheDir, TempDirPrefix + Guid.NewGuid().ToString("N"));
    }

    private async Task StageAsync(
        RuntimeArchive archive,
        string tempDir,
        Action<DotnetInstallProgress>? progress,
        string displayVersion,
        CancellationToken cancel)
    {
        var version = archive.Version.ToString();

        Log.Information("Downloading .NET {Flavor} {Version} ({Rid}) from {Url}", archive.Flavor, version, archive.Rid, archive.Url);

        await using var tempFile = TempFile.CreateTempFile();

        progress?.Invoke(new DotnetInstallProgress(version, Installing: false));
        try
        {
            await _http.DownloadToStream(
                archive.Url,
                tempFile,
                (downloaded, total) => progress?.Invoke(new DotnetInstallProgress(version, false, downloaded, total)),
                cancel);
        }
        catch (Exception e) when (IsNetworkError(e, cancel))
        {
            throw new LocalServerRuntimeUnavailableException(displayVersion, e);
        }

        progress?.Invoke(new DotnetInstallProgress(version, Installing: true));

        _ = tempFile.Seek(0, SeekOrigin.Begin);
        var actualSha = Convert.ToHexString(await SHA512.HashDataAsync(tempFile, cancel));
        if (!string.Equals(actualSha, archive.Sha512, StringComparison.OrdinalIgnoreCase))
            throw new UpdateException($"Downloaded .NET runtime hash mismatch: expected {archive.Sha512}, got {actualSha}.");

        _ = tempFile.Seek(0, SeekOrigin.Begin);
        _ = Directory.CreateDirectory(tempDir);

        if (archive.Url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ExtractZipToDirectory(tempDir, tempFile);
        }
        else
        {
            await using var gzip = new GZipStream(tempFile, CompressionMode.Decompress, leaveOpen: true);
            await TarFile.ExtractToDirectoryAsync(gzip, tempDir, overwriteFiles: true, cancel);
        }

        var muxer = Path.Combine(tempDir, MuxerFileName);
        if (!File.Exists(muxer))
            throw new UpdateException("Downloaded .NET runtime archive does not contain the dotnet host.");

        if (!OperatingSystem.IsWindows())
            ChmodPlusX(muxer);

        await File.WriteAllTextAsync(Path.Combine(tempDir, InstallMarkerFileName), archive.Sha512, cancel);
    }

    private DotnetInstallation Commit(RuntimeArchive archive, string tempDir)
    {
        var finalDir = Path.Combine(CacheDirectory, $"{archive.Flavor}-{archive.Version}-{archive.Rid}");

        if (Directory.Exists(finalDir))
            Directory.Delete(finalDir, recursive: true);

        Directory.Move(tempDir, finalDir);

        return new DotnetInstallation(finalDir, Path.Combine(finalDir, MuxerFileName), ScanSharedFrameworks(finalDir), IsLauncherManaged: true);
    }

    private void ScheduleUpdateCheck(DotnetRuntimeRequirements requirements, DotnetInstallation cached)
    {
        if (!IsUpdateCheckDue(requirements))
            return;

        var key = UpdateCheckKey(requirements);
        lock (_updateLock)
        {
            if (!_updatingChannels.Add(key))
                return;

            _ = _backgroundUpdates.RemoveAll(t => t.IsCompleted);
            _backgroundUpdates.Add(Task.Run(async () =>
            {
                try
                {
                    await UpdateAsync(requirements, cached);
                }
                catch (Exception e)
                {
                    Log.Warning(e, "Background update of the .NET {Version} runtime failed", requirements.DisplayVersion);
                }
                finally
                {
                    lock (_updateLock)
                        _ = _updatingChannels.Remove(key);
                }
            }));
        }
    }

    internal Task WaitForBackgroundUpdatesAsync()
    {
        lock (_updateLock)
            return Task.WhenAll(_backgroundUpdates);
    }

    private async Task UpdateAsync(DotnetRuntimeRequirements requirements, DotnetInstallation cached)
    {
        var archive = await FindArchiveAsync(requirements, CancellationToken.None);

        var framework = requirements.NeedsAspNetCore ? DotnetRuntimeRequirements.AspNetCoreApp : DotnetRuntimeRequirements.NetCoreApp;
        var current = cached.Frameworks[framework].Max();

        if (archive is null || archive.Version <= current)
        {
            MarkUpdateChecked(requirements);
            return;
        }

        Log.Information("Updating cached .NET runtime {Current} to {Version}", current, archive.Version);

        var tempDir = NewTempDir();
        lock (_updateLock)
            _ = _stagingDirs.Add(tempDir);

        try
        {
            await StageAsync(archive, tempDir, progress: null, requirements.DisplayVersion, CancellationToken.None);

            await _cacheLock.WaitAsync();
            try
            {
                _ = Commit(archive, tempDir);
            }
            finally
            {
                _ = _cacheLock.Release();
            }

            MarkUpdateChecked(requirements);
        }
        finally
        {
            lock (_updateLock)
                _ = _stagingDirs.Remove(tempDir);
            TryDeleteDirectory(tempDir);
        }
    }

    private static string UpdateCheckKey(DotnetRuntimeRequirements requirements) =>
        $"{(requirements.NeedsAspNetCore ? "aspnetcore" : "runtime")}-{requirements.DisplayVersion}";

    private string UpdateCheckPath(DotnetRuntimeRequirements requirements) =>
        Path.Combine(CacheDirectory, UpdateCheckFilePrefix + UpdateCheckKey(requirements));

    private bool IsUpdateCheckDue(DotnetRuntimeRequirements requirements)
    {
        var path = UpdateCheckPath(requirements);
        return !File.Exists(path) || DateTime.UtcNow - File.GetLastWriteTimeUtc(path) >= UpdateCheckInterval;
    }

    private void MarkUpdateChecked(DotnetRuntimeRequirements requirements)
    {
        try
        {
            File.WriteAllText(UpdateCheckPath(requirements), "");
            File.SetLastWriteTimeUtc(UpdateCheckPath(requirements), DateTime.UtcNow);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warning(e, "Failed to record the .NET runtime update check time");
        }
    }

    private async Task<RuntimeArchive?> FindArchiveAsync(DotnetRuntimeRequirements requirements, CancellationToken cancel)
    {
        var channel = requirements.DisplayVersion;
        var flavor = requirements.NeedsAspNetCore ? "aspnetcore-runtime" : "dotnet-runtime";
        var componentKey = requirements.NeedsAspNetCore ? "aspnetcore-runtime" : "runtime";

        await using var stream = await _http.GetStreamAsync(string.Format(ReleaseMetadataUrl, channel), cancel);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancel);

        if (!doc.RootElement.TryGetProperty("releases", out var releases))
            return null;

        foreach (var release in releases.EnumerateArray())
        {
            if (!TryGetComponent(release, "runtime", out var runtimeVersion, out _)
                || !TryGetComponent(release, componentKey, out var componentVersion, out var files))
                continue;

            var provided = new Dictionary<string, List<RuntimeVersion>>
            {
                [DotnetRuntimeRequirements.NetCoreApp] = [runtimeVersion]
            };
            if (requirements.NeedsAspNetCore)
                provided[DotnetRuntimeRequirements.AspNetCoreApp] = [componentVersion];

            if (!requirements.IsSatisfiedBy(provided))
                continue;

            var archives = new Dictionary<string, RuntimeArchive>(StringComparer.Ordinal);
            foreach (var file in files.EnumerateArray())
            {
                var name = file.GetProperty("name").GetString() ?? "";
                var rid = file.TryGetProperty("rid", out var r) ? r.GetString() : null;
                var url = file.GetProperty("url").GetString();
                var hash = file.TryGetProperty("hash", out var h) ? h.GetString() : null;

                if (rid is null || url is null || hash is null || !IsRuntimeArchive(name, flavor))
                    continue;

                archives[rid] = new RuntimeArchive(componentKey == "runtime" ? "runtime" : "aspnetcore", componentVersion, rid, url, hash);
            }

            var bestRid = RidUtility.FindBestRid(archives.Keys);
            if (bestRid is not null)
                return archives[bestRid];
        }

        return null;
    }

    private static bool IsRuntimeArchive(string name, string flavor) =>
        name.StartsWith(flavor + "-", StringComparison.Ordinal)
        && !name.StartsWith(flavor + "-composite-", StringComparison.Ordinal)
        && (OperatingSystem.IsWindows()
            ? name.EndsWith(".zip", StringComparison.Ordinal)
            : name.EndsWith(".tar.gz", StringComparison.Ordinal));

    private static bool TryGetComponent(JsonElement release, string key, out RuntimeVersion version, out JsonElement files)
    {
        version = default;
        files = default;

        return release.TryGetProperty(key, out var component)
            && component.ValueKind == JsonValueKind.Object
            && component.TryGetProperty("version", out var v)
            && RuntimeVersion.TryParse(v.GetString(), out version)
            && component.TryGetProperty("files", out files)
            && files.ValueKind == JsonValueKind.Array;
    }

    private static bool IsNetworkError(Exception e, CancellationToken cancel) =>
        e is HttpRequestException or HttpIOException or TimeoutException or DownloadStalledException
        || (e is OperationCanceledException && !cancel.IsCancellationRequested)
        || (e is IOException && e.InnerException is System.Net.Sockets.SocketException);

    private static void TryDeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warning(e, "Failed to remove temporary directory {Dir}", dir);
        }
    }
}
