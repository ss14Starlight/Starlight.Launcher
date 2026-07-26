using Starlight.Launcher.Models;
using Starlight.Launcher.Services.Settings;
using Starlight.Launcher.WebUI.Services;

namespace Starlight.Launcher.Services;

public sealed class TrayCoordinator
{
    private readonly INativeTray _tray;

    public TrayCoordinator(INativeTray tray) => _tray = tray;

    public void Initialize()
    {
        var menu = new List<TrayMenuItem>
        {
            new("Open", () => _tray.ShowWindow()),
            TrayMenuItem.Separator,
            new("Quit", QuitApp),
        };
        _tray.Initialize(new TrayOptions("STARLIGHT.LAUNCHER", "Resources/AppIcon/icon.ico"), menu);
        _tray.IconActivated += (_, _) => _tray.ShowWindow();
    }

    private void QuitApp() => Application.Current?.Quit();
}
