using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Starlight.Launcher.Models.Tray;

namespace Starlight.Launcher.Services;

public sealed class AvaloniaTray : INativeTray
{
    private TrayIcon? _trayIcon;

    public event EventHandler? IconActivated;

    public bool IsWindowVisible => GetWindow()?.IsVisible == true;

    public void Initialize(TrayOptions options, IReadOnlyList<TrayMenuItem> menu)
    {
        var nativeMenu = new NativeMenu();
        foreach (var item in menu)
        {
            if (item.IsSeparator)
            {
                nativeMenu.Items.Add(new NativeMenuItemSeparator());
                continue;
            }

            var nativeItem = new NativeMenuItem(item.Text);
            nativeItem.Click += (_, _) => item.Invoke?.Invoke();
            nativeMenu.Items.Add(nativeItem);
        }

        _trayIcon = new TrayIcon
        {
            ToolTipText = options.Tooltip,
            Icon = LoadIcon(options.IconPath),
            Menu = nativeMenu,
        };
        _trayIcon.Clicked += (_, _) => IconActivated?.Invoke(this, EventArgs.Empty);

        var icons = TrayIcon.GetIcons(Application.Current!) ?? new TrayIcons();
        icons.Add(_trayIcon);
        TrayIcon.SetIcons(Application.Current!, icons);
    }

    public void ShowWindow()
    {
        var window = GetWindow();
        if (window is null) return;

        window.Show();
        window.WindowState = WindowState.Normal;
        window.Activate();
    }

    public void HideWindow() => GetWindow()?.Hide();

    public void UpdateTooltip(string text) => _trayIcon?.ToolTipText = text;

    public void Dispose()
    {
        if (_trayIcon is null) return;

        _ = TrayIcon.GetIcons(Application.Current!)?.Remove(_trayIcon);
        _trayIcon.Dispose();
        _trayIcon = null;
    }

    private static Window? GetWindow() =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    private static WindowIcon? LoadIcon(string iconPath)
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri($"avares://Starlight.Launcher/{iconPath}"));
            return new WindowIcon(stream);
        }
        catch
        {
            return null;
        }
    }
}
