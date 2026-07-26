namespace Starlight.Launcher.WebUI.Models.Updater;

public enum UpdateStatus
{
    CheckingClientUpdate,
    CheckingEngineModules,
    DownloadingEngineVersion,
    DownloadingEngineModules,
    FetchingClientManifest,
    DownloadingClientUpdate,
    Verifying,
    CommittingDownload,
    LoadingIntoDb,
    CullingEngine,
    CullingContent,
    Ready,
    Error,
    LoadingContentBundle,
}
