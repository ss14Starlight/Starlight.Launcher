
using Starlight.Launcher.WebUI.Bridge;
using Starlight.Launcher.WebUI.Models.Updater;

namespace Starlight.Launcher.Services.Bridge;

public sealed partial class Bridge : IBridge
{
    public (long downloaded, long total, ProgressUnit unit)? GetUpdateProgress() => _updater.Progress;

    public UpdateStatus GetUpdateStatus() => _updater.Status;
}
