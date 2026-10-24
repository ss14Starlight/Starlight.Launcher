using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using MudBlazor;
using Starlight.Launcher.WebUI.Components.Atoms.Dialogs;
using Starlight.Launcher.WebUI.Components.Atoms.Settings;
using Starlight.Launcher.WebUI.Models.Data;
using Starlight.Launcher.WebUI.Models.Settings;
using Starlight.Launcher.WebUI.Services;

namespace Starlight.Launcher.WebUI.Components.Pages;

public partial class Settings : SettingsPageBase
{
    [Inject] private IDialogService _dialog { get; set; } = default!;
    [Inject] private IFileDialogService _fileDialog { get; set; } = default!;
    [Inject] private NavigationManager _navigation { get; set; } = default!;
    [Inject] private ISnackbar _snackbar { get; set; } = default!;
    [Inject] private ILogger<Settings> _logger { get; set; } = default!;

    private List<string> _availableLanguages = [];

    // Values of the engine's audio.output_mode cvar, null (the empty option) is automatic.
    private static readonly List<string> _audioOutputModes = ["stereo", "headphones", "quad", "5.1", "7.1", "mono"];

    private string AudioOutputModeName(string mode) => L[$"settings-menu-audio-output-mode-{mode switch { "5.1" => "surround51", "7.1" => "surround71", _ => mode }}"];

    private bool _developerMode;

    private StorageUsageOption? _storageUsage;

    protected override async Task OnInitializedAsync()
    {
        var settings = await Bridge.GetSettingsAsync();
        _developerMode = settings.DeveloperMode;
        _availableLanguages = L.EnumarateAllLoadedLanguages().ConvertAll(x => new CultureInfo(x).Name);
        await base.OnInitializedAsync();
    }

    private async Task OnResetSettings()
    {
        var confirmed = await _dialog.ShowMessageBoxAsync(
            L["settings-menu-reset-confirm-title"],
            L["settings-menu-reset-confirm-text"],
            yesText: L["settings-menu-reset-confirm-yes"],
            cancelText: L["settings-menu-reset-confirm-cancel"]);

        if (confirmed != true)
            return;

        var settings = new AppSettings
        {
            LastSeenChangelogVersion = Bridge.GetVersion()
        };

        await Bridge.WriteSettingsAsync(settings);

        ResetSettingsCache();
        State.CallUpdate();

        _navigation.NavigateTo("/settings", forceLoad: true);
    }

    private Task OnClearEngines() => ClearDataAsync(
        "settings-menu-clear-engines-confirm-title",
        "settings-menu-clear-engines-confirm-text",
        "settings-menu-clear-engines-success",
        Bridge.ClearEnginesAsync);

    private Task OnClearContent() => ClearDataAsync(
        "settings-menu-clear-content-confirm-title",
        "settings-menu-clear-content-confirm-text",
        "settings-menu-clear-content-success",
        Bridge.ClearContentAsync);

    private async Task OnVerifyContent()
    {
        var result = await Bridge.VerifyContentAsync();

        var (message, severity) = result switch
        {
            { Status: DataResetResult.Busy } => (L["settings-menu-clear-busy"], Severity.Warning),
            { Status: DataResetResult.ClientRunning } => (L["settings-menu-clear-client-running"], Severity.Warning),
            { Status: not DataResetResult.Success } => (L["settings-menu-verify-content-failed"], Severity.Error),
            { DatabaseRebuilt: true } => (L["settings-menu-verify-content-rebuilt"], Severity.Warning),
            { FoundProblems: true } => (L.GetString("settings-menu-verify-content-repaired",
                ("corrupt", result.CorruptBlobs),
                ("versions", result.RemovedVersions),
                ("orphans", result.RemovedOrphans)), Severity.Warning),
            _ => (L.GetString("settings-menu-verify-content-ok", ("count", result.CheckedBlobs)), Severity.Success),
        };

        _ = _snackbar.Add(message, severity);

        if (result.Status == DataResetResult.Success)
            await RefreshStorageUsage();
    }

    private async Task RefreshStorageUsage()
    {
        if (_storageUsage is not null)
            await _storageUsage.RefreshAsync();
    }

    private async Task OnCreateDiagnosticsBundle()
    {
        var result = await Bridge.CreateDiagnosticsBundleAsync();
        if (!result.Success || result.Path is null)
        {
            _ = _snackbar.Add(L["settings-menu-diagnostics-bundle-failed"], Severity.Error);
            return;
        }

        _ = _snackbar.Add(L.GetString("settings-menu-diagnostics-bundle-success", ("path", result.Path)), Severity.Success);
        Bridge.OpenPath(Path.GetDirectoryName(result.Path)!);
    }

    private async Task ClearDataAsync(string titleKey, string textKey, string successKey, Func<Task<DataResetResult>> clear)
    {
        var confirmed = await _dialog.ShowMessageBoxAsync(
            L[titleKey],
            L[textKey],
            yesText: L["settings-menu-clear-confirm-yes"],
            cancelText: L["general-cancel"]);

        if (confirmed != true)
            return;

        var (message, severity) = await clear() switch
        {
            DataResetResult.Success => (L[successKey], Severity.Success),
            DataResetResult.Busy => (L["settings-menu-clear-busy"], Severity.Warning),
            DataResetResult.ClientRunning => (L["settings-menu-clear-client-running"], Severity.Warning),
            _ => (L["settings-menu-clear-failed"], Severity.Error),
        };

        _ = _snackbar.Add(message, severity);
        await RefreshStorageUsage();
    }

    private async Task CheckUpdate()
    {
        var info = await Bridge.IsUpdateAvailable();
        if (!info.IsUpdateAvailable)
        {
            _ = _snackbar.Add(L["settings-menu-update-latest"], Severity.Success);
            return;
        }

        _ = _snackbar.Add(
            L.GetString("settings-menu-update-found", ("latest", info.LatestVersion)),
            Severity.Warning,
            config =>
            {
                config.Action = L["settings-menu-update-download"];
                config.ActionColor = Color.Primary;
                config.OnClick = __ =>
                {
                    if (info.Asset is { } asset)
                    {
                        var parameters = new DialogParameters<LauncherUpdateDialog>
                        {
                        { x => x.Asset, asset }
                        };
                        _ = _dialog.ShowAsync<LauncherUpdateDialog>(
                            null,
                            parameters,
                            new DialogOptions
                            {
                                CloseOnEscapeKey = false,
                                BackdropClick = false,
                                CloseButton = false
                            });
                    }
                    else
                    {
                        Bridge.OpenBrowser(info.ReleasePageUrl);
                    }

                    return Task.CompletedTask;
                };
            });
    }

    /// <summary>
    /// Turning developer mode on shows the warning first, the switch only flips once it's accepted.
    /// </summary>
    private async Task OnDeveloperModeChanged(bool value)
    {
        try
        {
            if (value)
            {
                var options = new DialogOptions
                {
                    CloseButton = false,
                    MaxWidth = MaxWidth.Small,
                    FullWidth = true,
                    BackdropClick = false,
                };

                var dialog = await _dialog.ShowAsync<AlertDialog>(L["settings-development-tab-alert-title"], options);
                if (await dialog.Result is not { Canceled: false })
                    return;
            }

            _developerMode = value;
            await UpdateSetting(s => s with { DeveloperModeStored = value, DevPolicyAccepted = s.DevPolicyAccepted || value }, callWindowUpdate: true);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to switch developer mode");
        }
    }

    private async Task OnLanguageChanged(string? value, Action<string?>? setLocal,
        Func<AppSettings, string?, AppSettings> update)
    {
        if (value is not null && _availableLanguages.Contains(value))
            await L.SwitchLanguageAsync(value);

        setLocal?.Invoke(value);
        await UpdateSetting(s => update(s, value));
    }

    private async Task OnDataFolderChanged(string path)
    {
        await UpdateSetting(s => s with { DirLauncherData = path });

        if (await Bridge.IsDirectoryWritableAsync(path))
            return;

        _ = await _dialog.ShowMessageBoxAsync(
            L["settings-menu-data-folder-no-access-title"],
            L.GetString("settings-menu-data-folder-no-access-text", ("path", path)),
            yesText: L["general-close"]);
    }

    private static List<Hub> ConvertToHubList(List<(string Url, long Priority)> list)
    {
        List<Hub> hubUris = [];
        foreach (var (url, priority) in list)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                hubUris.Add(new Hub { HubUri = uri, Priority = priority });
        }

        return hubUris;
    }
}
