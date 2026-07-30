using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Starlight.Launcher.Models.Tray;
using Starlight.Launcher.WebUI.Services;

namespace Starlight.Launcher.Services;

/// <summary>
/// Coordinates initialization and interaction with the system tray.
/// </summary>
public sealed class TrayCoordinator
{
    private readonly INativeTray _tray;

    /// <summary>
    /// Initializes a new instance of the <see cref="TrayCoordinator"/> class.
    /// </summary>
    public TrayCoordinator(INativeTray tray) => _tray = tray;

    /// <summary>
    /// Initializes the system tray icon and its menu.
    /// </summary>
    public void Initialize()
    {
        var menu = new List<TrayMenuItem>
        {
            new("Open", () => Dispatcher.UIThread.Post(_tray.ShowWindow)),
            TrayMenuItem.Separator,
            new("Quit", QuitApp),
        };
        _tray.Initialize(new TrayOptions("STARLIGHT.LAUNCHER", "Resources/AppIcon/icon.ico"), menu);
        _tray.IconActivated += (_, _) => Dispatcher.UIThread.Post(_tray.ShowWindow);
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
