namespace Starlight.Launcher.WebUI.Models.Journal;

public enum LauncherNotificationKind
{
    Info,
    Lobby,
    NewRound,
    Players,
    SlotFree,
    AutoConnect,
    FriendRequest,
}

public sealed record LauncherNotification(LauncherNotificationKind Kind, string Title, string Message, string? Address = null);
