using System.Runtime.InteropServices;
using Starlight.Launcher.WebUI.Bridge;
using Starlight.Launcher.WebUI.Models.LauncherUpdater;

namespace Starlight.Launcher.Services.Bridge;

public sealed partial class Bridge : IBridge
{
    public event Action<(long downloaded, long total)>? DownloadProgress
    {
        add => _launcherUpdater.DownloadProgress += value;
        remove => _launcherUpdater.DownloadProgress -= value;
    }

    public async Task<UpdateInfo> IsUpdateAvailable() => await _launcherUpdater.IsUpdateAvailable();

    public async Task<string> DownloadAsset(ReleaseAsset asset, CancellationToken ct = default) => await _launcherUpdater.DownloadAsset(asset, ct);

    public void RunInstallerAndExit(string downloadedPath)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            LauncherUpdater.RunInstallerAndExit(downloadedPath, _settings.GetSettings().DirLauncherInstall);
        else
            LauncherUpdater.RunInstallerAndExit(downloadedPath, LauncherUpdater.GetMacAppBundleRoot(AppContext.BaseDirectory));
    }

    public void CleanupOldInstallers() => _launcherUpdater.CleanupOldInstallers();

    public bool ShouldShowChangelog() => _launcherUpdater.ShouldShowChangelog();

    public async Task<IReadOnlyList<ChangelogEntry>> GetChangelogsToShow() => await _launcherUpdater.GetChangelogsToShow();

    public async Task<IReadOnlyList<ChangelogEntry>> GetAllChangelogs() => await _launcherUpdater.GetAllChangelogs();

    public void MarkChangelogSeen() => _launcherUpdater.MarkChangelogSeen();

    public string GetVersion() => LauncherUpdater.GetVersion();
}
