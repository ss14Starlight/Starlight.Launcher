using System.Diagnostics;
using System.Globalization;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Starlight.Launcher.Components.Atoms.Settings;
using Starlight.Launcher.Models.Settings;
using Starlight.Launcher.Services;
using Starlight.Launcher.Services.Localization;
using Starlight.Launcher.Services.Settings;
using Starlight.Launcher.Services.State;

namespace Starlight.Launcher.Components.Pages;

public partial class Settings : ComponentBase, IDisposable
{
    [Inject] private LocalizationManager _localization { get; set; } = default!;
    [Inject] private SettingsService _settings { get; set; } = default!;
    [Inject] private AppState _state { get; set; } = default!;
    [Inject] private IDialogService _dialog { get; set; } = default!;
    [Inject] private IFileDialogService _fileDialog { get; set; } = default!;
    [Inject] private NavigationManager _navigation { get; set; } = default!;
    [Inject] private LauncherUpdater _launcherUpdater { get; set; } = default!;
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
        var settings = await _settings.GetSettingsAsync();
        _availableLanguages = _localization.EnumarateAllLoadedLanguages().Select(x => new CultureInfo(x).Name).ToList();
        _state.OnChange += OnStateChanged;
        await base.OnInitializedAsync();
    }

    private async Task OnResetSettings()
    {
        var confirmed = await _dialog.ShowMessageBoxAsync(
            _localization["settings-menu-reset-confirm-title"],
            _localization["settings-menu-reset-confirm-text"],
            yesText: _localization["settings-menu-reset-confirm-yes"],
            cancelText: _localization["settings-menu-reset-confirm-cancel"]);

        if (confirmed != true)
            return;

        await _settings.WriteSettingsAsync(new AppSettings());

        _appSettingsCache = null;
        _state.CallUpdate();

        _navigation.NavigateTo("/settings", forceLoad: true);
    }

    private async Task CheckUpdate()
    {
        var (isUpdateAvailable, currentVersion, latestVersion, latestUrl) = await _launcherUpdater.IsUpdateAvailable();
        if (isUpdateAvailable)
        {
            _snackbar.Add(_localization.GetString("settings-menu-update-found", ("latest", latestVersion)), Severity.Warning, config =>
            {
                config.Action = _localization["settings-menu-update-download"];
                config.ActionColor = MudBlazor.Color.Primary;
                config.OnClick = _snackbar =>
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = latestUrl,
                        UseShellExecute = true
                    });
                    return Task.CompletedTask;
                };
            });
        }
        else
            _snackbar.Add(_localization["settings-menu-update-latest"], Severity.Success);
    }

    private void OnStateChanged()
        => StateHasChanged();

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

            var settings = await _settings.GetSettingsAsync();
            if (!settings.DevPolicyAccepted)
            {
                var dialog = await _dialog.ShowAsync<AlertDialog>(_localization["settings-development-tab-alert-title"], options);
                if (dialog.Dialog is AlertDialog alert)
                {
                    alert.OnSuccess += async () => await _settings.WriteSettingsAsync(await _settings.GetSettingsAsync() with { DevPolicyAccepted = true });
                    alert.OnCancel += async () => await _tabs.ActivatePanelAsync(_generalTab);
                }
            }
        }
    }

    private Task OnLanguageChanged(string? value, Action<string?>? setLocal,
        Func<AppSettings, string?, AppSettings> update)
    {
        if (value is not null && _availableLanguages.Contains(value))
            _localization.SwitchLanguage(value?.ToString() ?? string.Empty);

        setLocal?.Invoke(value);
        return UpdateSetting(s => update(s, value), true);
    }

    private Task OnSettingChanged<T>(T value, Action<T>? setLocal, Func<AppSettings, T, AppSettings> update, bool callWindowUpdate = false)
    {
        setLocal?.Invoke(value);
        return UpdateSetting(s => update(s, value), callWindowUpdate);
    }

    private async Task UpdateSetting(Func<AppSettings, AppSettings> update, bool callWindowUpdate = false)
    {
        var settings = await _settings.GetSettingsAsync();
        var newSettings = update(settings);
        await _settings.WriteSettingsAsync(newSettings);
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
            settings = await _settings.GetSettingsAsync();
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

    public void Dispose()
    {
        _state.OnChange -= OnStateChanged;
        GC.SuppressFinalize(this);
    }
}
