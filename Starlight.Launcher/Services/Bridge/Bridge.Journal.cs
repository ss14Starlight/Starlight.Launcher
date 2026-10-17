using Starlight.Launcher.WebUI.Bridge;
using Starlight.Launcher.WebUI.Models.Journal;

namespace Starlight.Launcher.Services.Bridge;

public sealed partial class Bridge : IBridge
{
    public event Action? ServerNotesChanged
    {
        add => _journal.NotesChanged += value;
        remove => _journal.NotesChanged -= value;
    }

    public event Action? ServerWatchesChanged
    {
        add => _journal.WatchesChanged += value;
        remove => _journal.WatchesChanged -= value;
    }

    public event Action? ServerActivityChanged
    {
        add => _journal.ActivityChanged += value;
        remove => _journal.ActivityChanged -= value;
    }

    public event Action? PlayHistoryChanged
    {
        add => _journal.HistoryChanged += value;
        remove => _journal.HistoryChanged -= value;
    }

    public event Action<LauncherNotification>? NotificationRaised
    {
        add => _monitor.NotificationRaised += value;
        remove => _monitor.NotificationRaised -= value;
    }

    public IReadOnlyDictionary<string, ServerNote> GetServerNotes() => _journal.GetNotes();

    public ServerNote? GetServerNote(string address) => _journal.GetNote(address);

    public void SetServerNote(ServerNote note) => _journal.SetNote(note);

    public ServerWatch? GetServerWatch(string address) => _journal.GetWatch(address);

    public List<ServerWatch> GetServerWatches() => _journal.GetWatches();

    public void SetServerWatch(ServerWatch watch) => _journal.SetWatch(watch);

    public ServerActivity? GetServerActivity(string address) => _journal.GetActivity(address);

    public List<PlaySession> GetPlaySessions() => _journal.GetSessions();

    public PlaySession? GetActivePlaySession() => _journal.ActiveSession;

    public void ClearPlayHistory() => _journal.ClearHistory();
}
