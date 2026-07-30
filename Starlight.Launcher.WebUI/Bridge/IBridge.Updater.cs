using Starlight.Launcher.WebUI.Models.Updater;

namespace Starlight.Launcher.WebUI.Bridge;

/// <summary>
/// Bridged parts from Updater.cs
/// </summary>
public partial interface IBridge
{
    /// <returns>Returns value from Updater.Progress/></returns>
    (long downloaded, long total, ProgressUnit unit)? GetUpdateProgress();

    UpdateStatus GetUpdateStatus();
}
