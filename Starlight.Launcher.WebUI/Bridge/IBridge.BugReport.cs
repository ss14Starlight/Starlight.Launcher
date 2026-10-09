using Starlight.Launcher.WebUI.Models.BugReport;

namespace Starlight.Launcher.WebUI.Bridge;

public partial interface IBridge
{
    Task<BugReportTargets?> GetBugReportTargetsAsync(CancellationToken cancel = default);

    Task<BugReportResult> SendBugReportAsync(BugReportDraft draft, CancellationToken cancel = default);
}
