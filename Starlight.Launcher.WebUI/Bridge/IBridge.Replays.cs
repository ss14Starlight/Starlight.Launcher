using Starlight.Launcher.WebUI.Models.Helpers;
using Starlight.Launcher.WebUI.Models.Replays;

namespace Starlight.Launcher.WebUI.Bridge;

public partial interface IBridge
{
    Task<ReplaysResult> GetReplaysAsync(CancellationToken cancel = default);

    bool IsReplayDownloaded(ReplayEntry replay);

    Task<IFileResult> DownloadReplayAsync(ReplayEntry replay, IProgress<double>? progress, CancellationToken cancel = default);
}
