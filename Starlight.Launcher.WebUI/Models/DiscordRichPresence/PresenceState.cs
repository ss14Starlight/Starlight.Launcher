namespace Starlight.Launcher.WebUI.Models.DiscordRichPresence;

public enum PresenceState
{
    Idle, // Home screen, not doing anything
    SearchingServers, // Browsing server list
    SettingUp, // Configuring settings, etc.
    DownloadingContent,
    LaunchingGame
}
