using Starlight.Launcher.WebUI.Models.LauncherUpdater;

namespace Starlight.Launcher.WebUI.Bridge;

/// <summary>
/// Bridged parts from LauncherUpdater.cs
/// </summary>
public partial interface IBridge
{
    event Action<(long downloaded, long total)>? DownloadProgress;

    Task<UpdateInfo> IsUpdateAvailable();

    Task<string> DownloadAsset(ReleaseAsset asset, CancellationToken ct = default);

    void RunInstallerAndExit(string downloadedPath);

    void CleanupOldInstallers();

    /// <summary>
    /// Log tail of the last self-update if it didn't apply (Flatpak only), returned once.
    /// </summary>
    string? TakeFailedUpdateReport();

    bool ShouldShowChangelog();

    Task<IReadOnlyList<ChangelogEntry>> GetChangelogsToShow();

    Task<IReadOnlyList<ChangelogEntry>> GetAllChangelogs();

    void MarkChangelogSeen();

    string GetVersion();
}
