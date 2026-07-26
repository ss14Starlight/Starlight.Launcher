using System.Diagnostics;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using MudBlazor;
using MudBlazor.Services;
using Starlight.Launcher.Components.Atoms.Dialogs;
using Starlight.Launcher.Models.Data;
using Starlight.Launcher.Services;
using Starlight.Launcher.Services.Discord;
using Starlight.Launcher.Services.Localization;
using Starlight.Launcher.Services.Settings;
using Starlight.Launcher.Services.State;

using AppTheme = Starlight.Launcher.Models.Settings.AppTheme;

namespace Starlight.Launcher.Components.Layout;

public partial class MainLayout : LayoutComponentBase, IAsyncDisposable, IBrowserViewportObserver
{
    [Inject] private IJSRuntime _jS { get; set; } = default!;
    [Inject] private SettingsService _settings { get; set; } = default!;
    [Inject] private LocalizationManager _localization { get; set; } = default!;
    [Inject] private AppState _state { get; set; } = default!;
    [Inject] private IBrowserViewportService _browserViewportService { get; set; } = default!;
    [Inject] private INativeTray _tray { get; set; } = default!;
    [Inject] private NavigationManager _navigation { get; set; } = default!;
    [Inject] private DiscordRichPresence _presence { get; set; } = default!;
    [Inject] private LauncherUpdater _launcherUpdater { get; set; } = default!;
    [Inject] private ISnackbar _snackbar { get; set; } = default!;
    [Inject] private IDialogService _dialogService { get; set; } = default!;

    Guid IBrowserViewportObserver.Id { get; } = Guid.NewGuid();

    private bool _isSmallScreen = false;

    public static string GetVersion() => LauncherUpdater.GetVersion();

    private static string ToDataTheme(AppTheme t, bool systemPrefersDark) => t switch
    {
        AppTheme.EmeraldLight => "emerald-light",
        AppTheme.EmeraldDark => "emerald-dark",
        AppTheme.AmberLight => "amber-light",
        AppTheme.AmberDark => "amber-dark",
        AppTheme.Midnight => "midnight",
        AppTheme.RoseLight => "rose-light",
        AppTheme.RoseDark => "rose-dark",
        AppTheme.VioletLight => "violet-light",
        AppTheme.VioletDark => "violet-dark",
        AppTheme.OceanLight => "ocean-light",
        AppTheme.System => systemPrefersDark ? "emerald-dark" : "emerald-light",
        _ => "emerald-light"
    };

    private ErrorBoundary? _errorBoundary;
    private ElementPosition _elementPosition;

    protected override void OnParametersSet() => _errorBoundary?.Recover();

    protected override async Task OnInitializedAsync()
    {
        var settings = await _settings.GetSettingsAsync();
        await ApplyThemeAsync();
        _elementPosition = settings.Navigation;
        _state.OnChange += AppCalledRepaint;
        _navigation.LocationChanged += OnLocationChanged;
        _settings.LoginsUnrecoverable += OnLoginsUnrecover;

        if (settings.CollapseInTrayOnStart)
            _tray.HideWindow(); // If layout is initialized - window exists, so we can hide it right away if the user wants that.

        _launcherUpdater.CleanupOldInstallers();
        await ShowChangelogIfNeeded();
        await CheckUpdate();
    }

    private void OnLoginsUnrecover() =>
        _snackbar.Add(
            _localization["settings-logins-unrecoverable"],
            Severity.Error,
            config =>
            {
                config.Action = _localization["settings-logins-unrecoverable-action"];
                config.ActionColor = MudBlazor.Color.Primary;
                config.OnClick = _ =>
                {
                    _dialogService.ShowAsync<LoginsUnrecoverableDialog>(
                        null,
                        new DialogParameters<LoginsUnrecoverableDialog>
                        {
                            { nameof(LoginsUnrecoverableDialog.Logins), _settings.GetLogins() }
                        },
                        new DialogOptions
                        {
                            CloseOnEscapeKey = false,
                            BackdropClick = false,
                            CloseButton = false
                        });
                    return Task.CompletedTask;
                };
            });

    private async Task ShowChangelogIfNeeded()
    {
        if (!_launcherUpdater.ShouldShowChangelog())
            return;

        var notes = await _launcherUpdater.GetChangelogForCurrentVersion();
        var version = LauncherUpdater.GetVersion();

        _launcherUpdater.MarkChangelogSeen();

        if (string.IsNullOrWhiteSpace(notes))
            return;

        var parameters = new DialogParameters<ChangelogDialog>
        {
            { x => x.Version, version },
            { x => x.Notes, notes! }
        };

        await _dialogService.ShowAsync<ChangelogDialog>(
            null,
            parameters,
            new DialogOptions
            {
                CloseOnEscapeKey = true,
                BackdropClick = true,
                MaxWidth = MaxWidth.Medium,
                FullWidth = true
            });
    }

    private async Task CheckUpdate()
    {
        var info = await _launcherUpdater.IsUpdateAvailable();
        if (!info.IsUpdateAvailable)
            return;

        _snackbar.Add(
            _localization.GetString("settings-menu-update-found", ("latest", info.LatestVersion)),
            Severity.Warning,
            config =>
            {
                config.Action = _localization["settings-menu-update-download"];
                config.ActionColor = MudBlazor.Color.Primary;
                config.OnClick = _ =>
                {
                    if (info.Asset is { } asset)
                    {
                        var parameters = new DialogParameters<LauncherUpdateDialog>
                        {
                        { x => x.Asset, asset }
                        };
                        _dialogService.ShowAsync<LauncherUpdateDialog>(
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
                        // No installer for this OS in the release — fall back to the release page.
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = info.ReleasePageUrl,
                            UseShellExecute = true
                        });
                    }
                    return Task.CompletedTask;
                };
            });
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        var uri = new Uri(e.Location);
        switch (uri.AbsolutePath)
        {
            case "/servers":
                _presence.UpdatePresence(PresenceState.SearchingServers);
                break;
            case "/settings":
                _presence.UpdatePresence(PresenceState.SettingUp);
                break;
            default:
                _presence.UpdatePresence(PresenceState.Idle);
                break;
        }
    }

    private async Task ApplyThemeAsync()
    {
        var settings = await _settings.GetSettingsAsync();
        var prefersDark = await _jS.InvokeAsync<bool>("appTheme.prefersDark");
        var themeName = ToDataTheme(settings.Theme, prefersDark);
        await _jS.InvokeVoidAsync("appTheme.set", themeName);
    }

    private void AppCalledRepaint() => _ = InvokeAsync((async () =>
                                            {
                                                var settings = await _settings.GetSettingsAsync();
                                                await ApplyThemeAsync();
                                                _elementPosition = settings.Navigation;
                                                StateHasChanged();
                                            }));

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        await _browserViewportService.UnsubscribeAsync(this);
        _state.OnChange -= AppCalledRepaint;
        _navigation.LocationChanged -= OnLocationChanged;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await _browserViewportService.SubscribeAsync(this, fireImmediately: true);
            await _jS.InvokeVoidAsync("eval", "document.getElementById('app')?.classList.add('loaded')");
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    ResizeOptions IBrowserViewportObserver.ResizeOptions { get; } = new()
    {
        NotifyOnBreakpointOnly = true
    };

    Task IBrowserViewportObserver.NotifyBrowserViewportChangeAsync(BrowserViewportEventArgs browserViewportEventArgs)
    {
        _isSmallScreen = browserViewportEventArgs.Breakpoint <= Breakpoint.Sm;
        return InvokeAsync(StateHasChanged);
    }
}
