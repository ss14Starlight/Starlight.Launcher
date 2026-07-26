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

    public void RunInstallerAndExit(string installerPath) => LauncherUpdater.RunInstallerAndExit(installerPath);

    public void CleanupOldInstallers() => _launcherUpdater.CleanupOldInstallers();

    public bool ShouldShowChangelog() => _launcherUpdater.ShouldShowChangelog();

    public async Task<string?> GetChangelogForCurrentVersion() => await _launcherUpdater.GetChangelogForCurrentVersion();

    public void MarkChangelogSeen() => _launcherUpdater.MarkChangelogSeen();

    public string GetVersion() => LauncherUpdater.GetVersion();
}
