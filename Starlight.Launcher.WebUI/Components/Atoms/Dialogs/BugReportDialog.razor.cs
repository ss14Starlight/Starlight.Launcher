using Microsoft.AspNetCore.Components;
using MudBlazor;
using Starlight.Launcher.WebUI.Bridge;
using Starlight.Launcher.WebUI.Models.BugReport;

namespace Starlight.Launcher.WebUI.Components.Atoms.Dialogs;

public sealed partial class BugReportDialog
{
    private const string DefaultProject = "STARLIGHT";

    [Inject] private IBridge _bridge { get; set; } = default!;
    [Inject] private ISnackbar _snackbar { get; set; } = default!;
    [CascadingParameter] private IMudDialogInstance _mudDialog { get; set; } = default!;

    [Parameter] public string? ServerAddress { get; set; }

    private readonly CancellationTokenSource _cts = new();

    private BugReportTargets? _targets;
    private BugReportTarget _target = BugReportTarget.Launcher;
    private string? _project;
    private string _title = "";
    private string _description = "";
    private string? _server;
    private bool _includeSystemInfo = true;
    private bool _linked;
    private bool _sending;
    private string? _error;

    private bool _launcherAvailable => _targets?.Launcher == true;
    private bool _gameAvailable => _targets?.Projects.Length > 0;

    private bool _targetAvailable => _target switch
    {
        BugReportTarget.Launcher => _launcherAvailable,
        BugReportTarget.Game => _gameAvailable && _project is not null,
        _ => false,
    };

    private bool _titleTooShort => _title.Trim().Length is > 0 and < BugReportLimits.MinTitleLength;
    private bool _descriptionTooShort => _description.Trim().Length is > 0 and < BugReportLimits.MinDescriptionLength;

    private bool _canSend
        => _linked
            && !_sending
            && _targetAvailable
            && _title.Trim().Length is >= BugReportLimits.MinTitleLength and <= BugReportLimits.MaxTitleLength
            && _description.Trim().Length is >= BugReportLimits.MinDescriptionLength and <= BugReportLimits.MaxDescriptionLength;

    protected override async Task OnInitializedAsync()
    {
        _server = ServerAddress;
        _linked = _bridge.GetActiveAccount() is { } account
            && (account.LoginInfo.DiscordToken ?? account.LoginInfo.SteamToken) is not null;

        try
        {
            _targets = await _bridge.GetBugReportTargetsAsync(_cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (_targets is null)
        {
            _error = L["bug-report-dialog-no-targets"];
            return;
        }

        if (_gameAvailable)
            _project = _targets.Projects.FirstOrDefault(p => p == DefaultProject) ?? _targets.Projects[0];

        _target = _launcherAvailable || !_gameAvailable ? BugReportTarget.Launcher : BugReportTarget.Game;

        if (!_launcherAvailable && !_gameAvailable)
            _error = L["bug-report-dialog-no-targets"];
    }

    private void OnTargetChanged(BugReportTarget target)
    {
        _target = target;
        _error = null;
    }

    private async Task Send()
    {
        if (!_canSend)
            return;

        _sending = true;
        _error = null;

        BugReportResult result;
        try
        {
            result = await _bridge.SendBugReportAsync(
                new BugReportDraft(_target, _project, _title, _description, _server, _includeSystemInfo), _cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            _sending = false;
        }

        if (result.Status == BugReportStatus.Sent)
        {
            _ = _snackbar.Add(L["bug-report-dialog-sent"], Severity.Success);
            _mudDialog.Close(DialogResult.Ok(true));
            return;
        }

        _error = result.Status switch
        {
            BugReportStatus.NotLinked => L["bug-report-dialog-not-linked"],
            BugReportStatus.Unauthorized => L["bug-report-dialog-error-unauthorized"],
            BugReportStatus.RateLimited when result.RetryAfter is { } wait
                => L.GetString("bug-report-dialog-error-rate-limited-wait", ("minutes", Math.Max(1, (int)Math.Ceiling(wait.TotalMinutes)))),
            BugReportStatus.RateLimited => L["bug-report-dialog-error-rate-limited"],
            BugReportStatus.Invalid when result.Error == "launcher-tracker-disabled" => L["bug-report-dialog-launcher-disabled"],
            BugReportStatus.Invalid => L["bug-report-dialog-error-invalid"],
            _ => L["bug-report-dialog-error-unavailable"],
        };
    }

    private void Cancel() => _mudDialog.Cancel();

    public override void Dispose()
    {
        base.Dispose();
        _cts.Cancel();
        _cts.Dispose();
    }
}
