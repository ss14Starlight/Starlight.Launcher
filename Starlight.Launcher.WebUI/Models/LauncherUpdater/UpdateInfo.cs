namespace Starlight.Launcher.WebUI.Models.LauncherUpdater;

public sealed record UpdateInfo(
    bool IsUpdateAvailable,
    string CurrentVersion,
    string LatestVersion,
    string ReleasePageUrl,
    string ReleaseNotes,
    ReleaseAsset? Asset);
