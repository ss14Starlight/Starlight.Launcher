using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Robust.Launcher.Api.Models;
using Robust.Launcher.Api.Utility;
using Serilog;
using Starlight.Launcher.Services.Settings;
using Starlight.Launcher.WebUI.Models.LocalServer;

namespace Starlight.Launcher.Services.LocalServer;

public sealed partial class LocalServerManager
{
    private const int MaxConsoleLines = 5000;

    private const string InstallMarkerFileName = ".starlight-install-complete";

    private readonly HttpClient _http;
    private readonly SettingsService _settings;

    private readonly object _lock = new();
    private readonly List<LocalServerLogLine> _consoleBuffer = [];
    private Process? _process;
    private Win32JobObject? _jobObject;

    public LocalServerManager(HttpClient http, SettingsService settings)
    {
        _http = http;
        _settings = settings;
    }

    public LocalServerState CurrentState { get; private set; } = new(LocalServerPhase.Idle);

    public string? LastInstallDirectory { get; private set; }

    public event Action<LocalServerLogLine>? OutputReceived;
    public event Action<LocalServerState>? StateChanged;

    public IReadOnlyList<LocalServerLogLine> GetConsoleBuffer()
    {
        lock (_lock)
            return _consoleBuffer.ToArray();
    }

    public async Task StartAsync(
        string sourceName,
        string manifestUrl,
        IReadOnlyList<ServerCVarValue> cvarOverrides,
        CancellationToken cancel = default)
    {
        await StopAsync();

        string? buildHash = null;
        try
        {
            SetState(new LocalServerState(LocalServerPhase.FetchingManifest, sourceName));

            var manifest = await FetchManifestAsync(manifestUrl, cancel);
            if (manifest == null || manifest.Builds.Count == 0)
                throw new UpdateException("Manifest contains no builds.");

            var latest = manifest.Builds.MaxBy(kv => kv.Value.Time);
            buildHash = latest.Key;
            var build = latest.Value;

            var rid = RidUtility.FindBestRid(build.Server.Keys)
                ?? throw new UpdateException("No server build available for your platform.");

            var asset = build.Server[rid];

            var installDir = GetInstallDirectory(manifestUrl, buildHash);
            var exePath = GetServerExecutablePath(installDir);
            var markerPath = Path.Combine(installDir, InstallMarkerFileName);

            if (!File.Exists(exePath) || !File.Exists(markerPath))
            {
                SetState(new LocalServerState(LocalServerPhase.Downloading, sourceName, buildHash, build.Time, 0, asset.Size));
                await DownloadAndExtractAsync(installDir, asset, sourceName, buildHash, build.Time, cancel);
            }

            LastInstallDirectory = installDir;

            ApplyCVarOverrides(installDir, cvarOverrides);

            SetState(new LocalServerState(LocalServerPhase.Starting, sourceName, buildHash, build.Time));

            LaunchProcess(installDir, exePath);

            SetState(new LocalServerState(LocalServerPhase.Running, sourceName, buildHash, build.Time));
        }
        catch (OperationCanceledException)
        {
            SetState(new LocalServerState(LocalServerPhase.Stopped, sourceName, buildHash));
            throw;
        }
        catch (Exception e)
        {
            Log.Error(e, "Failed to start local server from {ManifestUrl}", manifestUrl);
            SetState(new LocalServerState(LocalServerPhase.Error, sourceName, buildHash, ErrorMessage: e.Message));
            throw;
        }
    }

    public void Stop() => _ = StopAsync();

    private async Task StopAsync()
    {
        Process? process;
        lock (_lock)
        {
            process = _process;
            _process = null;
        }

        if (process == null)
            return;

        SetState(CurrentState with { Phase = LocalServerPhase.Stopping });

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
        }
        catch (Exception e)
        {
            Log.Warning(e, "Failed to stop local server process");
        }
    }

    public Task StopAndWaitAsync() => StopAsync();

    public bool SendCommand(string text)
    {
        Process? process;
        lock (_lock)
            process = _process;

        if (process == null || process.HasExited)
            return false;

        try
        {
            process.StandardInput.WriteLine(text);
            process.StandardInput.Flush();
            AppendLine($"> {text}", isError: false);
            return true;
        }
        catch (Exception e)
        {
            Log.Warning(e, "Failed to send command to local server");
            return false;
        }
    }

    public async Task ClearInstalledServersAsync()
    {
        await StopAsync();

        var dir = Path.Combine(_settings.GetSettings().DirLauncherData, "local-servers");
        await DeleteDirectoryWithRetryAsync(dir);

        LastInstallDirectory = null;
        SetState(new LocalServerState(LocalServerPhase.Idle));
    }

    private static async Task DeleteDirectoryWithRetryAsync(string dir)
    {
        const int MaxAttempts = 8;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                await Task.Run(() =>
                {
                    if (Directory.Exists(dir))
                        Directory.Delete(dir, recursive: true);
                });
                return;
            }
            catch (Exception e) when ((e is IOException or UnauthorizedAccessException) && attempt < MaxAttempts)
            {
                Log.Debug(e, "Local server install directory locked, retrying delete (attempt {Attempt})", attempt);
                await Task.Delay(TimeSpan.FromMilliseconds(300 * attempt));
            }
        }
    }

    private void LaunchProcess(string installDir, string exePath)
    {
        if (!File.Exists(exePath))
            throw new UpdateException($"Server executable not found at {exePath}.");

        if (!OperatingSystem.IsWindows())
            Helpers.ChmodPlusX(exePath);

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            WorkingDirectory = installDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true
        };

        var process = Process.Start(startInfo) ?? throw new UpdateException("Failed to start server process.");

        if (OperatingSystem.IsWindows())
            BindToKillOnCloseJob(process);
        else
            StartOrphanWatchdog(process.Id);

        lock (_lock)
            _process = process;

        AppendLine("[Starlight.Launcher] Server process started.", isError: false);

        _ = PumpOutputAsync(process);
    }

    private void BindToKillOnCloseJob(Process process)
    {
        try
        {
            _jobObject ??= Win32JobObject.CreateKillOnCloseJob();
            if (_jobObject is null || !_jobObject.AssignProcess(process.Handle))
                Log.Warning("Failed to bind local server process to a kill-on-close job object; it may survive an unexpected launcher exit.");
        }
        catch (Exception e)
        {
            Log.Warning(e, "Failed to bind local server process to a kill-on-close job object");
        }
    }

    private static void StartOrphanWatchdog(int serverPid)
    {
        try
        {
            var launcherPid = Environment.ProcessId;
            var script = $"while kill -0 {launcherPid} 2>/dev/null; do kill -0 {serverPid} 2>/dev/null || exit 0; sleep 2; done; kill -9 {serverPid} 2>/dev/null";

            _ = Process.Start(new ProcessStartInfo
            {
                FileName = "/bin/sh",
                ArgumentList = { "-c", script },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });
        }
        catch (Exception e)
        {
            Log.Warning(e, "Failed to start orphan-protection watchdog for local server process; it may survive an unexpected launcher exit.");
        }
    }

    private async Task PumpOutputAsync(Process process)
    {
        try
        {
            await Task.WhenAll(
                PumpStreamAsync(process.StandardOutput, isError: false),
                PumpStreamAsync(process.StandardError, isError: true));
        }
        catch (Exception e)
        {
            Log.Warning(e, "Error piping local server output");
        }

        int? exitCode = null;
        try
        {
            await process.WaitForExitAsync();
            exitCode = process.ExitCode;
        }
        catch (Exception e)
        {
            Log.Warning(e, "Failed to await local server exit");
        }

        var wasIntentionalStop = CurrentState.Phase == LocalServerPhase.Stopping;

        lock (_lock)
        {
            if (_process == process)
                _process = null;
        }

        AppendLine($"[Starlight.Launcher] Server process exited (code {exitCode?.ToString() ?? "unknown"}).", isError: false);

        SetState(wasIntentionalStop || exitCode is null or 0
            ? CurrentState with { Phase = LocalServerPhase.Stopped }
            : CurrentState with { Phase = LocalServerPhase.Error, ErrorMessage = $"Server exited with code {exitCode}." });
    }

    private async Task PumpStreamAsync(StreamReader reader, bool isError)
    {
        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
            AppendLine(line, isError);
    }

    private const string ServerConfigFileName = "server_config.toml";

    private static void ApplyCVarOverrides(string installDir, IReadOnlyList<ServerCVarValue> overrides)
    {
        if (overrides.Count == 0)
            return;

        try
        {
            var configPath = Path.Combine(installDir, ServerConfigFileName);
            var doc = File.Exists(configPath) ? TomlDocument.Parse(File.ReadAllText(configPath)) : new TomlDocument();

            foreach (var cvar in overrides)
            {
                if (string.IsNullOrWhiteSpace(cvar.Group) || string.IsNullOrWhiteSpace(cvar.Name))
                    continue;

                doc.Set(cvar.Group, cvar.Name, cvar.Type, cvar.Value);
            }

            File.WriteAllText(configPath, doc.Serialize());
        }
        catch (Exception e)
        {
            Log.Warning(e, "Failed to apply CVar overrides to {InstallDir}", installDir);
        }
    }

    private async Task DownloadAndExtractAsync(
        string installDir,
        LocalServerAssetInfo asset,
        string sourceName,
        string buildHash,
        DateTimeOffset buildTime,
        CancellationToken cancel)
    {
        await DeleteDirectoryWithRetryAsync(installDir);

        Helpers.EnsureDirectoryExists(Path.GetDirectoryName(installDir)!);

        await using var tempFile = TempFile.CreateTempFile();

        await _http.DownloadToStream(
            asset.Url,
            tempFile,
            (downloaded, total) => SetState(new LocalServerState(LocalServerPhase.Downloading, sourceName, buildHash, buildTime, downloaded, total)),
            cancel);

        _ = tempFile.Seek(0, SeekOrigin.Begin);
        var actualSha = await ComputeSha256HexAsync(tempFile, cancel);

        if (!string.Equals(actualSha, asset.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new UpdateException($"Downloaded server build hash mismatch: expected {asset.Sha256}, got {actualSha}.");

        SetState(new LocalServerState(LocalServerPhase.Extracting, sourceName, buildHash, buildTime));

        _ = tempFile.Seek(0, SeekOrigin.Begin);
        Helpers.ExtractZipToDirectory(installDir, tempFile);

        // Written last: marks the install as complete and safe to launch.
        await File.WriteAllTextAsync(Path.Combine(installDir, InstallMarkerFileName), asset.Sha256, cancel);
    }

    private static async Task<string> ComputeSha256HexAsync(Stream stream, CancellationToken cancel)
    {
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancel);
        return Convert.ToHexString(hash);
    }

    private static string GetServerExecutablePath(string installDir) =>
        Path.Combine(installDir, OperatingSystem.IsWindows() ? "Robust.Server.exe" : "Robust.Server");

    private string GetInstallDirectory(string manifestUrl, string buildHash)
    {
        var settings = _settings.GetSettings();
        return Path.Combine(settings.DirLauncherData, "local-servers", GetSourceSlug(manifestUrl), buildHash);
    }

    private static string GetSourceSlug(string manifestUrl)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(manifestUrl));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    private void SetState(LocalServerState state)
    {
        CurrentState = state;
        StateChanged?.Invoke(state);
    }

    private void AppendLine(string text, bool isError)
    {
        var line = new LocalServerLogLine(DateTimeOffset.Now, text, isError);

        lock (_lock)
        {
            _consoleBuffer.Add(line);
            if (_consoleBuffer.Count > MaxConsoleLines)
                _consoleBuffer.RemoveAt(0);
        }

        OutputReceived?.Invoke(line);
    }
}
