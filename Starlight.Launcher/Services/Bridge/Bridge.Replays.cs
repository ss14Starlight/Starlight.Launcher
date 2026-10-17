using Starlight.Launcher.WebUI.Bridge;
using Starlight.Launcher.WebUI.Models.Helpers;
using Starlight.Launcher.WebUI.Models.Replays;

namespace Starlight.Launcher.Services.Bridge;

public sealed partial class Bridge : IBridge
{
    public Task<ReplaysResult> GetReplaysAsync(CancellationToken cancel = default) => _replays.GetAsync(cancel);

    public bool IsReplayDownloaded(ReplayEntry replay) => _replays.IsDownloaded(replay);

    public Task<IFileResult> DownloadReplayAsync(ReplayEntry replay, IProgress<double>? progress, CancellationToken cancel = default)
        => _replays.DownloadAsync(replay, progress, cancel);
}
