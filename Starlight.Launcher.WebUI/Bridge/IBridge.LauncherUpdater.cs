using System.Diagnostics;
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

    bool ShouldShowChangelog();

    Task<IReadOnlyList<ChangelogEntry>> GetChangelogsToShow();

    void MarkChangelogSeen();

    string GetVersion();
}
