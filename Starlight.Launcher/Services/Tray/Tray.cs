using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Threading;
using Starlight.Launcher.Models.Tray;
using Starlight.Launcher.Services.Settings;
using Starlight.Launcher.WebUI.Localization;

namespace Starlight.Launcher.Services;

/// <summary>
/// Coordinates initialization and interaction with the system tray.
/// </summary>
public sealed class TrayCoordinator
{
    private readonly INativeTray _tray;
    private readonly SettingsService _settings;
    private readonly ILocalizationManager _loc;

    /// <summary>
    /// Initializes a new instance of the <see cref="TrayCoordinator"/> class.
    /// </summary>
    public TrayCoordinator(INativeTray tray, SettingsService settings, ILocalizationManager loc)
    {
        _tray = tray;
        _settings = settings;
        _loc = loc;
    }

    /// <summary>
    /// Initializes the system tray icon and its menu.
    /// </summary>
    public void Initialize()
    {
        _tray.Initialize(new TrayOptions("STARLIGHT.LAUNCHER", "Resources/AppIcon/icon.ico"), BuildMenu(), GetPalette());
        _tray.IconActivated += (_, _) => Dispatcher.UIThread.Post(_tray.ShowWindow);

        _loc.Changed += () => Dispatcher.UIThread.Post(() => _tray.SetMenu(BuildMenu()));
        // Lives as long as the app, so the subscription is never disposed.
        _ = _settings.Subscribe(s => s.Theme, _ => Dispatcher.UIThread.Post(RefreshPalette));

        // "System" follows the OS light/dark mode, which Avalonia tracks as the app's actual theme variant.
        if (Application.Current is { } app)
            app.ActualThemeVariantChanged += (_, _) => RefreshPalette();
    }

    private List<TrayMenuItem> BuildMenu() =>
    [
        new("Starlight Launcher", Icon: TrayMenuIcon.AppLogo, IsHeader: true),
        TrayMenuItem.Separator,
        new(_loc["tray-menu-open"], () => Dispatcher.UIThread.Post(_tray.ShowWindow), Icon: TrayMenuIcon.Open),
        new(_loc["tray-menu-quit"], QuitApp, Icon: TrayMenuIcon.Quit, IsDanger: true),
    ];

    private void RefreshPalette() => _tray.ApplyPalette(GetPalette());

    private TrayPalette GetPalette()
    {
        var systemDark = Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
        return TrayPalette.FromTheme(_settings.GetSettings().Theme, systemDark);
    }

    /// <summary>
    /// Shuts down the application.
    /// </summary>
    private static void QuitApp()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }
}
