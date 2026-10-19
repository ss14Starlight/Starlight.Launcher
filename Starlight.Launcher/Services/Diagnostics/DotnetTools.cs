using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Serilog;
using Starlight.Launcher.Services.LocalServer.Dotnet;
using Starlight.Launcher.Services.Settings;
using Starlight.Launcher.WebUI.Models.Settings;
using Starlight.Launcher.WebUI.Models.Diagnostics;

namespace Starlight.Launcher.Services.Diagnostics;

/// <summary>
/// Downloads the official .NET diagnostics tools on first use and runs them against the running client:
/// dotnet-trace, dotnet-counters, dotnet-gcdump, dotnet-stack and dotnet-monitor.
/// </summary>
/// <remarks>
/// The tools are framework-dependent, so they run on a .NET runtime from <see cref="DotnetRuntimeManager"/>
/// (the system one if it's new enough, otherwise a launcher-managed download shared with local servers).
/// </remarks>
public sealed partial class DotnetTools
{
    private readonly HttpClient _http;
    private readonly Func<AppSettings> _getSettings;
    private readonly DotnetRuntimeManager _dotnet;
    private readonly Func<int?> _findClient;

    public DotnetTools(HttpClient http, SettingsService settings, DotnetRuntimeManager dotnet, ClientDiagnostics client)
        : this(http, settings.GetSettings, dotnet, client.FindClient)
    {
    }

    internal DotnetTools(HttpClient http, Func<AppSettings> getSettings, DotnetRuntimeManager dotnet, Func<int?> findClient)
    {
        _http = http;
        _getSettings = getSettings;
        _dotnet = dotnet;
        _findClient = findClient;
    }

    // dotnet-monitor is only published as a .NET tool package, the others as single-file executables.
    private const string MonitorVersion = "10.0.5";
    private const string MonitorUrl = $"https://api.nuget.org/v3-flatcontainer/dotnet-monitor/{MonitorVersion}/dotnet-monitor.{MonitorVersion}.nupkg";

    private const string MonitorSha512 =
        "C63F30E1C83C248A15F19B915CD7FFA421364084DA283CAEA096D84F9DD143A996B8FB04F8474858386D5B201BD5094F34E5AA7B1EFF2B650CCCCEE439E5DA92";

    public const string MonitorUrlBase = "http://127.0.0.1:52323";
    private const string MonitorMetricsUrl = "http://127.0.0.1:52325";

    // aka.ms links always redirect to the latest release on Microsoft's download CDN, refuse anything else.
    private const string ToolDownloadHost = "download.visualstudio.microsoft.com";

    private const int MaxOutputLines = 2000;
    private const int MaxRunsKept = 20;

    /// <summary>
    /// dotnet-monitor 10 needs ASP.NET Core 10. The single-file tools roll forward onto it too, so one runtime serves all.
    /// </summary>
    public static readonly DotnetRuntimeRequirements RuntimeRequirements = new(
    [
        new FrameworkRequirement(DotnetRuntimeRequirements.NetCoreApp, new RuntimeVersion(10, 0, 0), RollForwardPolicy.Major),
        new FrameworkRequirement(DotnetRuntimeRequirements.AspNetCoreApp, new RuntimeVersion(10, 0, 0), RollForwardPolicy.Major),
    ]);

    private readonly ConcurrentDictionary<Guid, ToolRun> _runs = new();
    private readonly SemaphoreSlim _installLock = new(1, 1);

    [GeneratedRegex(@"\x1B\[[0-9;?]*[ -/]*[@-~]")]
    private static partial Regex AnsiEscape { get; }

    /// <summary>
    /// Raised whenever a run changes or prints something.
    /// </summary>
    public event Action? RunsChanged;

    /// <summary>
    /// Runtime identifier of the single-file tool builds, null where Microsoft publishes none.
    /// </summary>
    private static string? s_toolRid
        => (OperatingSystem.IsWindows(), OperatingSystem.IsLinux(), RuntimeInformation.OSArchitecture) switch
        {
            (true, _, Architecture.X64) => "win-x64",
            (true, _, Architecture.Arm64) => "win-arm64",
            (_, true, Architecture.X64) => "linux-x64",
            (_, true, Architecture.Arm64) => "linux-arm64",
            _ => null,
        };

    public static bool IsSupported(DiagnosticTool tool) => tool == DiagnosticTool.Monitor || s_toolRid != null;

    private string _toolsDir => Path.Combine(_getSettings().DirTools, "dotnet-diagnostics");

    public IReadOnlyList<ToolRunInfo> GetRuns()
        => [.. _runs.Values.OrderByDescending(r => r.StartedAt).Select(r => r.ToInfo())];

    public ToolStartResult Start(ToolRequest request)
    {
        if (!IsSupported(request.Tool))
            return new ToolStartResult(ToolStartStatus.Unsupported);

        int? pid = null;
        if (request.Tool != DiagnosticTool.Monitor)
        {
            pid = _findClient();
            if (pid == null)
                return new ToolStartResult(ToolStartStatus.NoClient);
        }
        else if (_runs.Values.Any(r => r.Tool == DiagnosticTool.Monitor && r.IsActive))
        {
            return new ToolStartResult(ToolStartStatus.AlreadyRunning);
        }

        PruneRuns();

        var run = new ToolRun(request.Tool, pid);
        _runs[run.Id] = run;
        Changed();

        _ = Task.Run(() => RunAsync(run, request));
        return new ToolStartResult(ToolStartStatus.Started, run.Id);
    }

    public void Stop(Guid id)
    {
        if (!_runs.TryGetValue(id, out var run))
            return;

        run.Stop();
        Changed();
    }

    public void Remove(Guid id)
    {
        if (_runs.TryGetValue(id, out var run) && !run.IsActive)
            _ = _runs.TryRemove(id, out _);

        Changed();
    }

    private async Task RunAsync(ToolRun run, ToolRequest request)
    {
        try
        {
            var runtime = await _dotnet.EnsureRuntimeAsync(RuntimeRequirements, p =>
            {
                if (p.Installing)
                    run.AddOutput($"Installing .NET {p.Version}...");
            }, run.Cancel);

            var startInfo = await BuildStartInfoAsync(run, request, runtime);

            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.CreateNoWindow = true;
            startInfo.Environment["DOTNET_ROOT"] = runtime.Root;
            startInfo.Environment["DOTNET_ROLL_FORWARD"] = "Major";
            startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";

            run.AddOutput($"> {Path.GetFileName(startInfo.FileName)} {string.Join(' ', startInfo.ArgumentList)}");
            Log.Information("Starting {Tool}: {File} {Args}", run.Tool, startInfo.FileName, string.Join(' ', startInfo.ArgumentList));

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Process.Start returned null");
            run.Attach(process);
            run.SetStatus(ToolRunStatus.Running);
            Changed();

            await Task.WhenAll(
                Pump(process.StandardOutput, run),
                Pump(process.StandardError, run),
                process.WaitForExitAsync());

            if (run.Tool == DiagnosticTool.Stack && run.OutputPath is { } stackFile)
            {
                // Everything after the echoed command line is the tool's own output.
                var lines = run.CopyOutput();
                await File.WriteAllLinesAsync(stackFile, lines.Skip(lines.FindIndex(l => l.StartsWith("> ", StringComparison.Ordinal)) + 1));
            }

            if (run.StopRequested)
                run.SetStatus(ToolRunStatus.Stopped);
            else
                run.SetStatus(process.ExitCode == 0 ? ToolRunStatus.Completed : ToolRunStatus.Failed);

            run.AddOutput($"Exited with code {process.ExitCode}.");
            Log.Information("{Tool} exited with {Code}", run.Tool, process.ExitCode);
        }
        catch (OperationCanceledException)
        {
            run.SetStatus(ToolRunStatus.Stopped);
        }
        catch (Exception e)
        {
            Log.Error(e, "Failed to run {Tool}", run.Tool);
            run.AddOutput(e.Message);
            run.SetStatus(ToolRunStatus.Failed);
        }
        finally
        {
            Changed();
        }
    }

    private async Task<ProcessStartInfo> BuildStartInfoAsync(ToolRun run, ToolRequest request, DotnetInstallation runtime)
    {
        var captures = _getSettings().DirDiagnosticCaptures;
        _ = Directory.CreateDirectory(captures);

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var pid = run.Pid.GetValueOrDefault().ToString(CultureInfo.InvariantCulture);
        var duration = TimeSpan.FromSeconds(Math.Clamp(request.DurationSeconds, 1, 3600)).ToString(@"dd\:hh\:mm\:ss", CultureInfo.InvariantCulture);

        switch (request.Tool)
        {
            case DiagnosticTool.Trace:
                {
                    var output = Path.Combine(captures, $"trace-{pid}-{stamp}.nettrace");
                    run.OutputPath = output;
                    var info = new ProcessStartInfo(await EnsureToolAsync("dotnet-trace", run))
                    {
                        ArgumentList =
                        {
                            "collect", "-p", pid, "-o", output, "--duration", duration,
                            "--profile", string.IsNullOrWhiteSpace(request.Profile) ? "dotnet-sampled-thread-time,dotnet-common" : request.Profile,
                        },
                    };
                    if (request.Format is "Speedscope" or "Chromium")
                    {
                        info.ArgumentList.Add("--format");
                        info.ArgumentList.Add(request.Format);
                    }

                    return info;
                }

            case DiagnosticTool.Counters:
                {
                    var output = Path.Combine(captures, $"counters-{pid}-{stamp}.csv");
                    run.OutputPath = output;
                    return new ProcessStartInfo(await EnsureToolAsync("dotnet-counters", run))
                    {
                        ArgumentList = { "collect", "-p", pid, "-o", output, "--format", "csv", "--refresh-interval", "1", "--duration", duration },
                    };
                }

            case DiagnosticTool.GcDump:
                {
                    var output = Path.Combine(captures, $"gcdump-{pid}-{stamp}.gcdump");
                    run.OutputPath = output;
                    // Walking a big game heap takes a while, the default 30 s gives up too early.
                    return new ProcessStartInfo(await EnsureToolAsync("dotnet-gcdump", run))
                    {
                        ArgumentList = { "collect", "-p", pid, "-o", output, "-t", "300" },
                    };
                }

            case DiagnosticTool.Stack:
                run.OutputPath = Path.Combine(captures, $"stacks-{pid}-{stamp}.txt");
                return new ProcessStartInfo(await EnsureToolAsync("dotnet-stack", run))
                {
                    ArgumentList = { "report", "-p", pid },
                };

            case DiagnosticTool.Monitor:
                var dll = await EnsureMonitorAsync(run);
                run.Url = MonitorUrlBase;
                // Bound to loopback only, so going without auth exposes nothing beyond this machine.
                return new ProcessStartInfo(runtime.Muxer)
                {
                    ArgumentList = { dll, "collect", "--no-auth", "--urls", MonitorUrlBase, "--metricUrls", MonitorMetricsUrl },
                };
            default:
                throw new ArgumentOutOfRangeException(nameof(request));
        }
    }

    private async Task<string> EnsureToolAsync(string name, ToolRun run)
    {
        var rid = s_toolRid ?? throw new PlatformNotSupportedException($"{name} has no build for this platform.");
        var path = Path.Combine(_toolsDir, rid, OperatingSystem.IsWindows() ? $"{name}.exe" : name);

        await _installLock.WaitAsync(run.Cancel);
        try
        {
            if (File.Exists(path))
                return path;

            run.AddOutput($"Downloading {name}...");
            _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            using var response = await _http.GetAsync($"https://aka.ms/{name}/{rid}", HttpCompletionOption.ResponseHeadersRead, run.Cancel);
            _ = response.EnsureSuccessStatusCode();

            var finalHost = response.RequestMessage?.RequestUri?.Host;
            if (!string.Equals(finalHost, ToolDownloadHost, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"{name} download redirected to unexpected host {finalHost}");

            var tmp = path + ".tmp";
            await using (var file = File.Create(tmp))
                await response.Content.CopyToAsync(file, run.Cancel);

            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            File.Move(tmp, path, overwrite: true);
            Log.Information("Installed {Tool} to {Path}", name, path);
            return path;
        }
        finally
        {
            _ = _installLock.Release();
        }
    }

    private async Task<string> EnsureMonitorAsync(ToolRun run)
    {
        var dir = Path.Combine(_toolsDir, $"dotnet-monitor-{MonitorVersion}");
        var dll = Path.Combine(dir, "dotnet-monitor.dll");

        await _installLock.WaitAsync(run.Cancel);
        try
        {
            if (File.Exists(dll))
                return dll;

            run.AddOutput($"Downloading dotnet-monitor {MonitorVersion}...");
            _ = Directory.CreateDirectory(_toolsDir);

            var tmp = Path.Combine(_toolsDir, $".dotnet-monitor-{Guid.NewGuid():N}.nupkg");
            try
            {
                using (var response = await _http.GetAsync(MonitorUrl, HttpCompletionOption.ResponseHeadersRead, run.Cancel))
                {
                    _ = response.EnsureSuccessStatusCode();
                    await using var file = File.Create(tmp);
                    await response.Content.CopyToAsync(file, run.Cancel);
                }

                string hash;
                await using (var file = File.OpenRead(tmp))
                    hash = Convert.ToHexString(await SHA512.HashDataAsync(file, run.Cancel));

                if (!hash.Equals(MonitorSha512, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"dotnet-monitor download hash mismatch: got {hash}");

                // The tool lives under tools/<tfm>/any/ in the package.
                await using var zip = ZipFile.OpenRead(tmp);
                var prefix = zip.Entries
                    .Select(e => e.FullName)
                    .First(n => n.StartsWith("tools/", StringComparison.Ordinal) && n.EndsWith("/any/dotnet-monitor.dll", StringComparison.Ordinal))[..^"dotnet-monitor.dll".Length];

                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);

                foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith(prefix, StringComparison.Ordinal) && e.Name.Length > 0))
                {
                    var target = Path.GetFullPath(Path.Combine(dir, entry.FullName[prefix.Length..]));
                    if (!target.StartsWith(Path.GetFullPath(dir) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                        throw new InvalidDataException($"Unexpected path in dotnet-monitor package: {entry.FullName}");

                    _ = Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    entry.ExtractToFile(target, overwrite: true);
                }
            }
            finally
            {
                try
                {
                    File.Delete(tmp);
                }
                catch (IOException)
                {
                    // Leftover temp file, harmless.
                }
            }

            Log.Information("Installed dotnet-monitor {Version} to {Dir}", MonitorVersion, dir);
            return dll;
        }
        finally
        {
            _ = _installLock.Release();
        }
    }

    private async Task Pump(StreamReader reader, ToolRun run)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            run.AddOutput(AnsiEscape.Replace(line, ""));
            Changed();
        }
    }

    private void PruneRuns()
    {
        foreach (var old in _runs.Values.Where(r => !r.IsActive).OrderByDescending(r => r.StartedAt).Skip(MaxRunsKept - 1))
            _ = _runs.TryRemove(old.Id, out _);
    }

    private void Changed() => RunsChanged?.Invoke();

    /// <summary>
    /// Stops whatever still runs, called when the launcher exits.
    /// </summary>
    public void StopAll()
    {
        foreach (var run in _runs.Values)
            run.Stop();
    }

    private sealed class ToolRun(DiagnosticTool tool, int? pid)
    {
        private readonly Lock _lock = new();
        private readonly Queue<string> _output = new();
        private readonly CancellationTokenSource _cts = new();
        private Process? _process;

        public Guid Id { get; } = Guid.NewGuid();
        public DiagnosticTool Tool { get; } = tool;
        public int? Pid { get; } = pid;
        public DateTimeOffset StartedAt { get; } = DateTimeOffset.Now;
        public ToolRunStatus Status { get; private set; } = ToolRunStatus.Installing;
        public string? OutputPath { get; set; }
        public string? Url { get; set; }
        public bool StopRequested { get; private set; }
        public CancellationToken Cancel => _cts.Token;

        public bool IsActive => Status is ToolRunStatus.Installing or ToolRunStatus.Running;

        public void SetStatus(ToolRunStatus status) => Status = status;

        public void Attach(Process process)
        {
            lock (_lock)
                _process = process;
        }

        public void AddOutput(string line)
        {
            lock (_lock)
            {
                _output.Enqueue(line);
                while (_output.Count > MaxOutputLines)
                    _ = _output.Dequeue();
            }
        }

        public List<string> CopyOutput()
        {
            lock (_lock)
                return [.. _output];
        }

        public void Stop()
        {
            if (!IsActive)
                return;

            StopRequested = true;
            _cts.Cancel();

            lock (_lock)
            {
                try
                {
                    if (_process is { HasExited: false })
                        _process.Kill(entireProcessTree: true);
                }
                catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    Log.Debug(e, "Failed to kill {Tool}", Tool);
                }
            }
        }

        public ToolRunInfo ToInfo()
            => new(Id, Tool, Pid, Status, StartedAt, OutputPath, Url, CopyOutput());
    }
}
