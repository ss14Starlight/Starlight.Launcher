using System.Diagnostics;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using MudBlazor;
using MudBlazor.Services;
using Starlight.Launcher.WebUI.Components.Atoms.Dialogs;
using Starlight.Launcher.WebUI.Bridge;
using Starlight.Launcher.WebUI.Models.Data;
using Starlight.Launcher.WebUI.Models.Settings;
using Starlight.Launcher.WebUI.Services;
using Starlight.Launcher.WebUI.Localization;
using Starlight.Launcher.WebUI.Models.DiscordRichPresence;

namespace Starlight.Launcher.WebUI.Components.Layout;

public partial class MainLayout : LocalizedLayoutBase, IAsyncDisposable, IBrowserViewportObserver
{
    [Inject] private IJSRuntime _jS { get; set; } = default!;
    [Inject] private IBrowserViewportService _browserViewportService { get; set; } = default!;
    [Inject] private IBridge _bridge { get; set; } = default!;
    [Inject] private INativeTray _tray { get; set; } = default!;
    [Inject] private NavigationManager _navigation { get; set; } = default!;
    [Inject] private ISnackbar _snackbar { get; set; } = default!;
    [Inject] private IDialogService _dialogService { get; set; } = default!;
    [Inject] private AppState _state { get; set; } = default!;

    Guid IBrowserViewportObserver.Id { get; } = Guid.NewGuid();

    private bool _isSmallScreen = false;

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
        var settings = await _bridge.GetSettingsAsync();
        _elementPosition = settings.Navigation;
        _state.OnChange += AppCalledRepaint;
        _navigation.LocationChanged += OnLocationChanged;
        _bridge.LoginsUnrecoverable += OnLoginsUnrecover;

        if (settings.CollapseInTrayOnStart)
            _tray.HideWindow(); // If layout is initialized - window exists, so we can hide it right away if the user wants that.

        _bridge.CleanupOldInstallers();
        await ShowChangelogIfNeeded();
        await CheckUpdate();
    }

    private void OnLoginsUnrecover() =>
        _snackbar.Add(
            L["settings-logins-unrecoverable"],
            Severity.Error,
            config =>
            {
                config.Action = L["settings-logins-unrecoverable-action"];
                config.ActionColor = MudBlazor.Color.Primary;
                config.OnClick = _ =>
                {
                    _dialogService.ShowAsync<LoginsUnrecoverableDialog>(
                        null,
                        new DialogParameters<LoginsUnrecoverableDialog>
                        {
                            { nameof(LoginsUnrecoverableDialog.Logins), _bridge.GetLogins() }
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
        if (!_bridge.ShouldShowChangelog())
            return;

        var entries = await _bridge.GetChangelogsToShow();

        _bridge.MarkChangelogSeen();

        if (entries.Count == 0)
            return;

        var parameters = new DialogParameters<ChangelogDialog>
        {
            { x => x.Entries, entries }
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
        var info = await _bridge.IsUpdateAvailable();
        if (!info.IsUpdateAvailable)
            return;

        _snackbar.Add(
            L.GetString("settings-menu-update-found", ("latest", info.LatestVersion)),
            Severity.Warning,
            config =>
            {
                config.Action = L["settings-menu-update-download"];
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
                _bridge.UpdatePresence(PresenceState.SearchingServers);
                break;
            case "/settings":
                _bridge.UpdatePresence(PresenceState.SettingUp);
                break;
            default:
                _bridge.UpdatePresence(PresenceState.Idle);
                break;
        }
    }

    private async Task ApplyThemeAsync()
    {
        var settings = await _bridge.GetSettingsAsync();
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
            await ApplyThemeAsync();
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
