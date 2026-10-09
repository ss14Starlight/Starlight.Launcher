using System.Globalization;
using System.Runtime.InteropServices;
using Starlight.Launcher.Services.Auth;
using Starlight.Launcher.WebUI.Bridge;
using Starlight.Launcher.WebUI.Models.BugReport;

namespace Starlight.Launcher.Services.Bridge;

public sealed partial class Bridge : IBridge
{
    public Task<BugReportTargets?> GetBugReportTargetsAsync(CancellationToken cancel = default)
        => _starlightAuth.GetBugReportTargetsAsync(cancel);

    public async Task<BugReportResult> SendBugReportAsync(BugReportDraft draft, CancellationToken cancel = default)
    {
        if (_loginManager.ActiveAccount is not { } account || ProfileToken(account) is null)
            return new(BugReportStatus.NotLinked);

        var metadata = new Dictionary<string, string>();
        if (draft.Target == BugReportTarget.Game && !string.IsNullOrWhiteSpace(draft.ServerAddress))
            metadata["Server"] = draft.ServerAddress.Trim();

        if (draft.IncludeSystemInfo)
        {
            metadata["Launcher"] = GetVersion();
            metadata["OS"] = $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})";
            metadata["Runtime"] = RuntimeInformation.FrameworkDescription;
            metadata["Install"] = InstallKindDetector.Current.ToString();
            metadata["Language"] = _settings.GetSettings().SelectedLanguage ?? CultureInfo.CurrentUICulture.Name;
        }

        var target = draft.Target == BugReportTarget.Launcher ? "launcher" : "game";
        var project = draft.Target == BugReportTarget.Game ? draft.Project : null;
        var request = new StarlightBugReportRequest(target, project, draft.Title.Trim(), draft.Description.Trim(), metadata);

        for (var attempt = 0; ; attempt++)
        {
            if (ProfileToken(account) is not { } token)
                return new(BugReportStatus.Unauthorized);

            var (outcome, result) = await _starlightAuth.SendBugReportAsync(token, request, cancel);
            if (outcome != TokenCheckOutcome.Invalid)
                return result;

            if (attempt > 0)
                return new(BugReportStatus.Unauthorized);

            _ = await _loginManager.EnsureFreshAsync(account, cancel);
        }
    }
}
