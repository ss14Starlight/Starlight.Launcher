namespace Starlight.Launcher.WebUI.Bridge;

/// <summary>
/// Bridged parts from LauncherUpdater.cs
/// </summary>
public partial interface IBridge
{
    event Action<(long downloaded, long total)>? DownloadProgress;
}
