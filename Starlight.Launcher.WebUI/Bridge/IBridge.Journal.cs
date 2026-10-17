using Starlight.Launcher.WebUI.Models.Journal;

namespace Starlight.Launcher.WebUI.Bridge;

public partial interface IBridge
{
    event Action? ServerNotesChanged;
    event Action? ServerWatchesChanged;
    event Action? ServerActivityChanged;
    event Action? PlayHistoryChanged;
    event Action<LauncherNotification>? NotificationRaised;

    IReadOnlyDictionary<string, ServerNote> GetServerNotes();
    ServerNote? GetServerNote(string address);
    void SetServerNote(ServerNote note);

    ServerWatch? GetServerWatch(string address);
    List<ServerWatch> GetServerWatches();
    void SetServerWatch(ServerWatch watch);

    ServerActivity? GetServerActivity(string address);

    List<PlaySession> GetPlaySessions();
    PlaySession? GetActivePlaySession();
    void ClearPlayHistory();
}
