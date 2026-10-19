using System.Diagnostics;
using System.Diagnostics.Tracing;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Serilog;
using Starlight.Launcher.Services.ContentManagement;
using Starlight.Launcher.Services.Settings;
using Starlight.Launcher.WebUI.Models.Settings;
using Starlight.Launcher.WebUI.Models.Diagnostics;

namespace Starlight.Launcher.Services.Diagnostics;

public sealed class ClientDiagnostics : IDisposable
{
    private const string LoaderProcessName = "Robust.Loader";
    private const string RuntimeProvider = "System.Runtime";
    private const int CounterIntervalSec = 1;

    private static readonly TimeSpan _idleTimeout = TimeSpan.FromSeconds(20);

    private readonly Func<int?> _findClient;
    private readonly Func<AppSettings> _getSettings;
    private readonly Lock _sessionLock = new();
    private readonly Lock _snapshotLock = new();
    private readonly Timer _idleTimer;

    private CounterSession? _session;
    private DateTime _lastRequested;

    public ClientDiagnostics(ContentManager content, SettingsService settings)
        : this(() => FindClientPid(content), settings.GetSettings)
    {
    }

    internal ClientDiagnostics(Func<int?> findClient, Func<AppSettings> getSettings)
    {
        _findClient = findClient;
        _getSettings = getSettings;
        _idleTimer = new Timer(_ => StopIfIdle(), null, _idleTimeout, _idleTimeout);
    }

    public ClientDiagnosticsSnapshot? LastSnapshot { get; private set; }

    public int? FindClient() => _findClient();

    public ClientDiagnosticsSnapshot? GetSnapshot()
    {
        var pid = _findClient();

        lock (_sessionLock)
        {
            _lastRequested = DateTime.UtcNow;

            if (pid is not { } p)
            {
                StopSession();
                return null;
            }

            if (_session?.Pid != p || _session.Faulted)
            {
                StopSession();
                _session = CounterSession.TryStart(p, OnCounters);
            }

            var last = LastSnapshot;
            return last?.Pid == p ? last : new ClientDiagnosticsSnapshot(p, DateTimeOffset.Now, []);
        }
    }

    /// <summary>
    /// Writes a heap dump of the newest running client into the crash dumps folder, like <c>dotnet-dump collect</c>.
    /// The client is frozen while the dump is written.
    /// </summary>
    public async Task<ClientDumpResult> WriteDumpAsync(CancellationToken cancel = default)
    {
        if (_findClient() is not { } pid)
            return new ClientDumpResult(ClientDumpStatus.NoClient);

        var settings = _getSettings();
        var dir = settings.DirCrashDumps;
        var path = Path.Combine(dir, $"{CrashDumps.SnapshotPrefix}{pid}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}{CrashDumps.Extension}");

        try
        {
            _ = Directory.CreateDirectory(dir);
            CrashDumps.Prune(dir, Math.Max(settings.CrashDumpsRetain, 1) - 1);

            Log.Information("Writing dump of client {Pid} to {Path}", pid, path);
            var sw = Stopwatch.StartNew();
            await new DiagnosticsClient(pid).WriteDumpAsync(DumpType.WithHeap, path, logDumpGeneration: false, cancel);
            Log.Information("Dump of client {Pid} written in {Elapsed}", pid, sw.Elapsed);

            return new ClientDumpResult(ClientDumpStatus.Success, path);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            Log.Error(e, "Failed to write dump of client {Pid}", pid);
            return new ClientDumpResult(ClientDumpStatus.Failed);
        }
    }

    private static int? FindClientPid(ContentManager content)
    {
        var pids = new HashSet<int>();

        try
        {
            using var con = content.GetSqliteConnection();
            foreach (var (pid, _) in ContentManager.GetRunningClients(con))
                _ = pids.Add(pid);
        }
        catch (Exception e)
        {
            Log.Debug(e, "Can't read running clients from the content DB");
        }

        try
        {
            foreach (var pid in DiagnosticsClient.GetPublishedProcesses())
            {
                if (IsLoader(pid))
                    _ = pids.Add(pid);
            }
        }
        catch (Exception e)
        {
            Log.Debug(e, "Can't list .NET processes");
        }

        var (newestPid, newestStart) = pids
            .Select(pid => (Pid: pid, Start: TryGetStartTime(pid)))
            .Where(p => p.Start != null)
            .MaxBy(p => p.Start);

        return newestStart == null ? null : newestPid;
    }

    private static bool IsLoader(int pid)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            return proc.ProcessName == LoaderProcessName;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private static DateTime? TryGetStartTime(int pid)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            return proc.HasExited ? null : proc.StartTime;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private void OnCounters(int pid, RuntimeCounter counter)
    {
        lock (_snapshotLock)
        {
            var counters = LastSnapshot?.Pid == pid
                ? LastSnapshot.Counters.Where(c => c.Name != counter.Name).Append(counter).ToList()
                : [counter];

            LastSnapshot = new ClientDiagnosticsSnapshot(pid, DateTimeOffset.Now, counters);
        }
    }

    private void StopIfIdle()
    {
        lock (_sessionLock)
        {
            if (_session != null && DateTime.UtcNow - _lastRequested > _idleTimeout)
                StopSession();
        }
    }

    private void StopSession()
    {
        _session?.Dispose();
        _session = null;
    }

    public void Dispose()
    {
        _idleTimer.Dispose();
        lock (_sessionLock)
            StopSession();
    }

    private sealed class CounterSession : IDisposable
    {
        private readonly EventPipeSession _session;

        public int Pid { get; }
        public bool Faulted { get; private set; }

        private CounterSession(int pid, EventPipeSession session)
        {
            Pid = pid;
            _session = session;
        }

        public static CounterSession? TryStart(int pid, Action<int, RuntimeCounter> onCounter)
        {
            EventPipeSession eventSession;
            try
            {
                var provider = new EventPipeProvider(RuntimeProvider, EventLevel.Informational, (long)EventKeywords.None,
                    new Dictionary<string, string> { ["EventCounterIntervalSec"] = CounterIntervalSec.ToString() });

                eventSession = new DiagnosticsClient(pid).StartEventPipeSession(provider, requestRundown: false);
            }
            catch (Exception e)
            {
                Log.Warning(e, "Can't attach to client {Pid} for runtime counters", pid);
                return null;
            }

            var session = new CounterSession(pid, eventSession);
            _ = Task.Run(() => session.Read(onCounter));
            Log.Debug("Listening to runtime counters of client {Pid}", pid);
            return session;
        }

        private void Read(Action<int, RuntimeCounter> onCounter)
        {
            try
            {
                using var source = new EventPipeEventSource(_session.EventStream);
                source.Dynamic.All += e =>
                {
                    if (e.EventName == "EventCounters" && TryParse(e) is { } counter)
                        onCounter(Pid, counter);
                };
                _ = source.Process();
            }
            catch (Exception e)
            {
                Log.Debug(e, "Runtime counter stream of client {Pid} ended", Pid);
            }
            finally
            {
                Faulted = true;
            }
        }

        private static RuntimeCounter? TryParse(TraceEvent e)
        {
            if (e.PayloadValue(0) is not IDictionary<string, object> outer
                || !outer.TryGetValue("Payload", out var inner)
                || inner is not IDictionary<string, object> payload)
            {
                return null;
            }

            var name = payload.TryGetValue("Name", out var n) ? n as string : null;
            if (name == null)
                return null;

            var isRate = payload.TryGetValue("CounterType", out var type) && (type as string) == "Sum";
            var raw = payload.TryGetValue(isRate ? "Increment" : "Mean", out var v) ? v : null;
            if (raw == null)
                return null;

            var display = payload.TryGetValue("DisplayName", out var d) && d is string ds && ds.Length > 0 ? ds : name;
            var units = payload.TryGetValue("DisplayUnits", out var u) ? u as string ?? "" : "";

            return new RuntimeCounter(name, display, Convert.ToDouble(raw), units);
        }

        public void Dispose()
        {
            try
            {
                _session.Stop();
            }
            catch (Exception e)
            {
                Log.Debug(e, "Failed to stop runtime counter session of client {Pid}", Pid);
            }

            _session.Dispose();
        }
    }
}
