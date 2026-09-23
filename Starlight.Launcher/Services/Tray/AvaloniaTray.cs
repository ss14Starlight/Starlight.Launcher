using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Starlight.Launcher.Models.Tray;

namespace Starlight.Launcher.Services;

public sealed class AvaloniaTray : INativeTray
{
    // Material Design icons, 24x24 view box.
    private const string OpenIconPath = "M19 19H5V5h7V3H5c-1.11 0-2 .9-2 2v14c0 1.1.89 2 2 2h14c1.1 0 2-.9 2-2v-7h-2v7zM14 3v2h3.59l-9.83 9.83 1.41 1.41L19 6.41V10h2V3h-7z";
    private const string QuitIconPath = "M13 3h-2v10h2V3zm4.83 2.17l-1.42 1.42C17.99 7.86 19 9.81 19 12c0 3.87-3.13 7-7 7s-7-3.13-7-7c0-2.19 1.01-4.14 2.58-5.42L6.17 5.17C4.23 6.82 3 9.26 3 12c0 4.97 4.03 9 9 9s9-4.03 9-9c0-2.74-1.23-5.18-3.17-6.83z";

    // Rendered at 3x; the menu's 16px icon slot scales it down, so it stays sharp up to 300% display scaling.
    private const int IconPixels = 48;

    private TrayIcon? _trayIcon;
    private IReadOnlyList<TrayMenuItem> _menu = [];
    private TrayPalette? _palette;
    private Bitmap? _appLogo;

    public event EventHandler? IconActivated;

    public bool IsWindowVisible => GetWindow()?.IsVisible == true;

    public void Initialize(TrayOptions options, IReadOnlyList<TrayMenuItem> menu, TrayPalette palette)
    {
        _menu = menu;
        _palette = palette;
        ApplyResources(palette);

        _trayIcon = new TrayIcon
        {
            ToolTipText = options.Tooltip,
            Icon = LoadIcon(options.IconPath),
            Menu = BuildMenu(),
        };
        _trayIcon.Clicked += (_, _) => IconActivated?.Invoke(this, EventArgs.Empty);

        var icons = TrayIcon.GetIcons(Application.Current!) ?? new TrayIcons();
        icons.Add(_trayIcon);
        TrayIcon.SetIcons(Application.Current!, icons);
    }

    public void SetMenu(IReadOnlyList<TrayMenuItem> menu)
    {
        _menu = menu;
        _trayIcon?.Menu = BuildMenu();
    }

    public void ApplyPalette(TrayPalette palette)
    {
        if (palette == _palette)
            return;

        _palette = palette;
        ApplyResources(palette);
        // Icons are bitmaps tinted with the palette, so they have to be redrawn.
        _trayIcon?.Menu = BuildMenu();
    }

    public void ShowWindow()
    {
        var window = GetWindow();
        if (window is null) return;

        window.WindowState = WindowState.Normal;
        window.Show();
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

    private NativeMenu BuildMenu()
    {
        var nativeMenu = new NativeMenu();
        foreach (var item in _menu)
        {
            if (item.IsSeparator)
            {
                nativeMenu.Items.Add(new NativeMenuItemSeparator());
                continue;
            }

            var nativeItem = new NativeMenuItem(item.Text)
            {
                Icon = GetIcon(item),
                IsEnabled = !item.IsHeader,
            };

            if (!item.IsHeader && item.Invoke is { } invoke)
                nativeItem.Click += (_, _) => invoke();

            nativeMenu.Items.Add(nativeItem);
        }

        return nativeMenu;
    }

    private Bitmap? GetIcon(TrayMenuItem item)
    {
        if (_palette is null)
            return null;

        var color = item.IsDanger ? _palette.Danger : _palette.Accent;
        return item.Icon switch
        {
            TrayMenuIcon.AppLogo => _appLogo ??= LoadBitmap("Resources/AppIcon/icon.png"),
            TrayMenuIcon.Open => RenderIcon(OpenIconPath, color),
            TrayMenuIcon.Quit => RenderIcon(QuitIconPath, color),
            _ => null,
        };
    }

    private static Bitmap RenderIcon(string pathData, Color color)
    {
        var bitmap = new RenderTargetBitmap(new PixelSize(IconPixels, IconPixels), new Vector(96, 96));
        using (var ctx = bitmap.CreateDrawingContext())
        using (ctx.PushTransform(Matrix.CreateScale(IconPixels / 24.0, IconPixels / 24.0)))
        {
            ctx.DrawGeometry(new SolidColorBrush(color), null, Geometry.Parse(pathData));
        }

        return bitmap;
    }

    private static void ApplyResources(TrayPalette p)
    {
        if (Application.Current is not { } app)
            return;

        var res = app.Resources;
        res["MenuFlyoutPresenterBackground"] = new SolidColorBrush(p.Background);
        res["MenuFlyoutPresenterBorderBrush"] = new SolidColorBrush(p.Border);
        res["MenuFlyoutItemBackground"] = Brushes.Transparent;
        res["MenuFlyoutItemForeground"] = new SolidColorBrush(p.Foreground);
        res["MenuFlyoutItemBackgroundPointerOver"] = new SolidColorBrush(p.Hover);
        res["MenuFlyoutItemForegroundPointerOver"] = new SolidColorBrush(p.Foreground);
        res["MenuFlyoutItemBackgroundPressed"] = new SolidColorBrush(p.Pressed);
        res["MenuFlyoutItemForegroundPressed"] = new SolidColorBrush(p.Foreground);
        res["MenuFlyoutItemBackgroundDisabled"] = Brushes.Transparent;
        res["MenuFlyoutItemForegroundDisabled"] = new SolidColorBrush(p.Muted);
        res["SlTraySeparator"] = new SolidColorBrush(p.Separator);
        res["SlTrayShadow"] = new BoxShadows(new BoxShadow
        {
            OffsetY = 6,
            Blur = 18,
            Spread = -4,
            Color = Color.FromArgb(p.IsDark ? (byte)0x8C : (byte)0x40, 0, 0, 0),
        });
    }

    private static Window? GetWindow() =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    private static Bitmap? LoadBitmap(string path)
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri($"avares://Starlight.Launcher/{path}"));
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

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
