using Robust.Launcher.Api.Models.ServerStatus;
using Serilog;
using Starlight.Launcher.Services.Settings;
using Starlight.Launcher.WebUI.Models.Journal;

namespace Starlight.Launcher.Services.Journal;

public sealed class JournalService
{
    private const int MaxSessions = 3000;

    private static readonly TimeSpan _roundPollInterval = TimeSpan.FromSeconds(60);

    private readonly JsonFileStore<NotesDocument> _notes;
    private readonly JsonFileStore<WatchesDocument> _watches;
    private readonly JsonFileStore<ActivityDocument> _activity;
    private readonly JsonFileStore<HistoryDocument> _history;
    private readonly ServerStatusCache _statusCache;
    private readonly HttpClient _http;

    private volatile IReadOnlyDictionary<string, ServerNote> _notesIndex;
    private RunningSession? _active;

    public event Action? NotesChanged;
    public event Action? WatchesChanged;
    public event Action? ActivityChanged;
    public event Action? HistoryChanged;

    public event Action<PlaySession?>? ActiveSessionChanged;

    public JournalService(SettingsService settings, ServerStatusCache statusCache, HttpClient http)
    {
        _statusCache = statusCache;
        _http = http;

        var dir = Path.Combine(settings.GetSettings().DirLauncherData, "journal");
        _notes = new JsonFileStore<NotesDocument>(Path.Combine(dir, "server-notes.json"));
        _watches = new JsonFileStore<WatchesDocument>(Path.Combine(dir, "server-watches.json"));
        _activity = new JsonFileStore<ActivityDocument>(Path.Combine(dir, "server-activity.json"));
        _history = new JsonFileStore<HistoryDocument>(Path.Combine(dir, "play-history.json"));
        _notesIndex = BuildNotesIndex();

        _history.Update(doc =>
        {
            foreach (var s in doc.Sessions.Where(s => s.EndUtc is null))
                s.EndUtc = s.Rounds.Count > 0 ? s.Rounds[^1].SeenUtc : s.StartUtc;
        });
    }

    public Task FlushAsync() => Task.WhenAll(_notes.FlushAsync(), _watches.FlushAsync(), _activity.FlushAsync(), _history.FlushAsync());

    #region Notes

    public IReadOnlyDictionary<string, ServerNote> GetNotes() => _notesIndex;

    public ServerNote? GetNote(string address) => _notesIndex.GetValueOrDefault(address);

    public void SetNote(ServerNote note)
    {
        _notes.Update(doc =>
        {
            _ = doc.Notes.RemoveAll(x => string.Equals(x.Address, note.Address, StringComparison.OrdinalIgnoreCase));
            if (!note.IsEmpty)
                doc.Notes.Add(note with { Note = note.Note.Length > 2000 ? note.Note[..2000] : note.Note, Rating = Math.Clamp(note.Rating, 0, 5) });
        });

        _notesIndex = BuildNotesIndex();
        NotesChanged?.Invoke();
    }

    private Dictionary<string, ServerNote> BuildNotesIndex()
        => _notes.Read(doc => doc.Notes
            .GroupBy(x => x.Address, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase));

    #endregion Notes

    #region Watches

    public List<ServerWatch> GetWatches() => _watches.Read(doc => doc.Watches.ToList());

    public ServerWatch? GetWatch(string address)
        => _watches.Read(doc => doc.Watches.FirstOrDefault(x => string.Equals(x.Address, address, StringComparison.OrdinalIgnoreCase)));

    public void SetWatch(ServerWatch watch)
    {
        _watches.Update(doc =>
        {
            _ = doc.Watches.RemoveAll(x => string.Equals(x.Address, watch.Address, StringComparison.OrdinalIgnoreCase));
            if (!watch.IsEmpty)
                doc.Watches.Add(watch);
        });

        WatchesChanged?.Invoke();
    }

    #endregion Watches

    #region Activity

    public ServerActivity? GetActivity(string address)
        => _activity.Read(doc => doc.Servers.TryGetValue(address, out var a) ? Clone(a) : null);

    public DateTime? LastActivitySample(string address)
        => _activity.Read(doc => doc.Servers.TryGetValue(address, out var a) ? a.LastSampleUtc : (DateTime?)null);

    public void AddActivitySample(string address, int players)
    {
        var now = DateTime.UtcNow;
        _activity.Update(doc =>
        {
            if (!doc.Servers.TryGetValue(address, out var activity))
                doc.Servers[address] = activity = new ServerActivity { Address = address };

            activity.Add(now.ToLocalTime(), players);
            activity.LastSampleUtc = now;
        });

        ActivityChanged?.Invoke();
    }

    private static ServerActivity Clone(ServerActivity a) => new()
    {
        Address = a.Address,
        PlayerSums = [.. a.PlayerSums],
        Samples = [.. a.Samples],
        LastSampleUtc = a.LastSampleUtc,
    };

    #endregion Activity

    #region History

    public PlaySession? ActiveSession => _active?.Session;

    public List<PlaySession> GetSessions() => _history.Read(doc => doc.Sessions.ConvertAll(CloneSession));

    public void ClearHistory()
    {
        _history.Update(doc => doc.Sessions.RemoveAll(s => s.EndUtc is not null));
        HistoryChanged?.Invoke();
    }

    public IDisposable BeginSession(string address, string? serverName)
    {
        var session = new PlaySession
        {
            Address = address,
            ServerName = serverName,
            StartUtc = DateTime.UtcNow,
        };

        var active = new RunningSession(this, session);
        Interlocked.Exchange(ref _active, active)?.Dispose();

        _history.Update(doc =>
        {
            doc.Sessions.Add(session);
            if (doc.Sessions.Count > MaxSessions)
                doc.Sessions.RemoveRange(0, doc.Sessions.Count - MaxSessions);
        });

        HistoryChanged?.Invoke();
        ActiveSessionChanged?.Invoke(CloneSession(session));
        active.Start();
        return active;
    }

    private void EndSession(RunningSession active)
    {
        _history.Update(_ => active.Session.EndUtc = DateTime.UtcNow);

        if (Interlocked.CompareExchange(ref _active, null, active) == active)
            ActiveSessionChanged?.Invoke(null);

        HistoryChanged?.Invoke();
    }

    private void RecordRound(PlaySession session, ServerStatusData data)
    {
        if (data.Status != ServerStatusCode.Online)
            return;

        var changed = false;
        _history.Update(_ =>
        {
            if (data.Name is { Length: > 0 } name)
                session.ServerName = name;

            if (data.RoundStatus == GameRoundStatus.InLobby)
                return;

            var last = session.Rounds.Count > 0 ? session.Rounds[^1] : null;
            var isNew = last is null || (data.RoundId is { } id ? id != last.RoundId : data.Map is not null && data.Map != last.Map);
            if (isNew && (data.RoundId is not null || data.Map is not null))
            {
                session.Rounds.Add(new PlayedRound(data.RoundId, data.Map, data.Preset, DateTime.UtcNow));
                changed = true;
            }
            else if (!isNew && last!.Preset is null && data.Preset is not null)
            {
                session.Rounds[^1] = last with { Preset = data.Preset };
                changed = true;
            }
        });

        if (changed)
            HistoryChanged?.Invoke();
    }

    private static PlaySession CloneSession(PlaySession s) => new()
    {
        Id = s.Id,
        Address = s.Address,
        ServerName = s.ServerName,
        StartUtc = s.StartUtc,
        EndUtc = s.EndUtc,
        Rounds = [.. s.Rounds],
    };

    private sealed class RunningSession(JournalService owner, PlaySession session) : IDisposable
    {
        private readonly CancellationTokenSource _cts = new();
        private int _disposed;

        public PlaySession Session { get; } = session;

        public void Start() => _ = PollAsync(_cts.Token);

        private async Task PollAsync(CancellationToken cancel)
        {
            var data = owner._statusCache.GetStatusFor(Session.Address);
            using var timer = new PeriodicTimer(_roundPollInterval);
            do
            {
                try
                {
                    await owner._statusCache.UpdateStatusFor(data, owner._http, cancel);
                    if (!cancel.IsCancellationRequested)
                        owner.RecordRound(Session, data);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception e)
                {
                    Log.Debug(e, "Can't poll status of {Address} for the play history", Session.Address);
                }

                try
                {
                    if (!await timer.WaitForNextTickAsync(cancel))
                        return;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            } while (true);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            _cts.Cancel();
            _cts.Dispose();
            owner.EndSession(this);
        }
    }

    #endregion History

    private sealed class NotesDocument
    {
        public List<ServerNote> Notes { get; set; } = [];
    }

    private sealed class WatchesDocument
    {
        public List<ServerWatch> Watches { get; set; } = [];
    }

    private sealed class ActivityDocument
    {
        public Dictionary<string, ServerActivity> Servers { get; set; } = [with(StringComparer.OrdinalIgnoreCase)];
    }

    private sealed class HistoryDocument
    {
        public List<PlaySession> Sessions { get; set; } = [];
    }
}
