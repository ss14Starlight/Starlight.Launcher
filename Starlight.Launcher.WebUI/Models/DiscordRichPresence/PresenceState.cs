namespace Starlight.Launcher.WebUI.Models.DiscordRichPresence;

public enum PresenceState
{
    Idle, // Just opened the launcher, not doing anything yet
    ManagesLogins, // Auth page
    SearchingServers, // Server list page
    ViewingServer, // When focused on a server in the list
    SettingUp, // Settings page
    DownloadingContent, // Downloading content for a server
    UpdatingLauncher, // Updating the launcher
    LaunchingGame, // Launching the game
    InGame, // In the game
    Reconnecting // Reconnecting to a server after a disconnect
}
