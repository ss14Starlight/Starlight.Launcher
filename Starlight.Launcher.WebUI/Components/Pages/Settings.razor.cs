using System.Globalization;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Starlight.Launcher.WebUI.Bridge;
using Starlight.Launcher.WebUI.Components.Atoms.Dialogs;
using Starlight.Launcher.WebUI.Components.Atoms.Settings;
using Starlight.Launcher.WebUI.Localization;
using Starlight.Launcher.WebUI.Models.Settings;
using Starlight.Launcher.WebUI.Services;

namespace Starlight.Launcher.WebUI.Components.Pages;

public partial class Settings : LocalizedComponentBase, IDisposable
{
    [Inject] private IBridge _bridge { get; set; } = default!;
    [Inject] private AppState _state { get; set; } = default!;
    [Inject] private IDialogService _dialog { get; set; } = default!;
    [Inject] private IFileDialogService _fileDialog { get; set; } = default!;
    [Inject] private NavigationManager _navigation { get; set; } = default!;
    [Inject] private ISnackbar _snackbar { get; set; } = default!;
    private List<string> _availableLanguages = [];

    private MudTabs _tabs = null!;

    private MudTabPanel _generalTab = null!;
    private MudTabPanel _developmentTab = null!;

    private AppSettings? _appSettingsCache = null;
    private DateTime _lastCacheUpdate;
    private readonly TimeSpan _cacheUpdateInterval = TimeSpan.FromSeconds(2);

    protected override async Task OnInitializedAsync()
    {
        var settings = await _bridge.GetSettingsAsync();
        _availableLanguages = L.EnumarateAllLoadedLanguages().Select(x => new CultureInfo(x).Name).ToList();
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
            LastSeenChangelogVersion = _bridge.GetVersion()
        };

        await _bridge.WriteSettingsAsync(settings);

        _appSettingsCache = null;
        _state.CallUpdate();

        _navigation.NavigateTo("/settings", forceLoad: true);
    }

    private async Task CheckUpdate()
    {
        var info = await _bridge.IsUpdateAvailable();
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
                config.ActionColor = MudBlazor.Color.Primary;
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
                        _bridge.OpenBrowser(info.ReleasePageUrl);
                    }
                    return Task.CompletedTask;
                };
            });
    }

    private async void OnActivePanelIndexChanged(int value)
    {
        var index = _tabs.Panels.Select((value, index) => (value, index)).FirstOrDefault(x => ReferenceEquals(x.value, _developmentTab)).index;

        if (value == index)
        {
            var options = new DialogOptions
            {
                CloseButton = false,
                MaxWidth = MaxWidth.Small,
                FullWidth = true,
                BackdropClick = false,
            };

            var settings = await _bridge.GetSettingsAsync();
            if (!settings.DevPolicyAccepted)
            {
                var dialog = await _dialog.ShowAsync<AlertDialog>(L["settings-development-tab-alert-title"], options);
                if (dialog.Dialog is AlertDialog alert)
                {
                    alert.OnSuccess += async () => await _bridge.WriteSettingsAsync(await _bridge.GetSettingsAsync() with { DevPolicyAccepted = true });
                    alert.OnCancel += async () => await _tabs.ActivatePanelAsync(_generalTab);
                }
            }
        }
    }

    private Task OnLanguageChanged(string? value, Action<string?>? setLocal,
        Func<AppSettings, string?, AppSettings> update)
    {
        if (value is not null && _availableLanguages.Contains(value))
            L.SwitchLanguage(value?.ToString() ?? string.Empty);

        setLocal?.Invoke(value);
        return UpdateSetting(s => update(s, value));
    }

    private Task OnSettingChanged<T>(T value, Action<T>? setLocal, Func<AppSettings, T, AppSettings> update, bool callWindowUpdate = false)
    {
        setLocal?.Invoke(value);
        return UpdateSetting(s => update(s, value), callWindowUpdate);
    }

    private async Task UpdateSetting(Func<AppSettings, AppSettings> update, bool callWindowUpdate = false)
    {
        var settings = await _bridge.GetSettingsAsync();
        var newSettings = update(settings);
        await _bridge.WriteSettingsAsync(newSettings);
        if (callWindowUpdate)
            _state.CallUpdate();
    }

    private async Task<T> FetchSettings<T>(Func<AppSettings, T> func)
    {
        AppSettings settings;
        if (_appSettingsCache != null &&
            DateTime.Now - _lastCacheUpdate < _cacheUpdateInterval)
        {
            settings = _appSettingsCache;
        }
        else
        {
            settings = await _bridge.GetSettingsAsync();
            _appSettingsCache = settings;
            _lastCacheUpdate = DateTime.Now;
        }

        var result = func(settings);
        return result;
    }

    private static List<Hub> ConvertToHubList(List<(string Url, long Priority)> list)
    {
        List<Hub> hubUris = [];
        foreach (var (Url, Priority) in list)
            if (Uri.TryCreate(Url, UriKind.Absolute, out var uri))
                hubUris.Add(new Hub { HubUri = uri, Priority = Priority });

        return hubUris;
    }
}
