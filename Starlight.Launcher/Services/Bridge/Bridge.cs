using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Reactive;
using Avalonia.Threading;
using Serilog;
using Starlight.Launcher.Services.Auth;
using Starlight.Launcher.Services.Discord;
using Starlight.Launcher.Services.LocalServer;
using Starlight.Launcher.Services.ServerStatus;
using Starlight.Launcher.Services.Settings;
using Starlight.Launcher.WebUI.Bridge;
using Starlight.Launcher.WebUI.Models.Helpers;
using Starlight.Launcher.WebUI.Services;

namespace Starlight.Launcher.Services.Bridge;

public sealed partial class Bridge : IBridge
{
    private readonly LauncherCommands _commands;
    private readonly Connector _connector;
    private readonly DiscordAuthService _discordAuth;
    private readonly SteamAuthService _steamAuth;
    private readonly DiscordRichPresence _discordRichPresence;
    private readonly HubServerFetcher _hubServerFetcher;
    private readonly LauncherUpdater _launcherUpdater;
    private readonly LoginManager _loginManager;
    private readonly ServerInfoLoader _serverInfoLoader;
    private readonly SettingsService _settings;
    private readonly Updater _updater;
    private readonly IFileDialogService _fileDialog;
    private readonly INativeTray _tray;
    private readonly LocalServerManager _localServer;
    private Window? _window;

    public Bridge(LauncherCommands commands, Connector connector, DiscordAuthService discordAuth,
        SteamAuthService steamAuth, DiscordRichPresence discordRichPresence, HubServerFetcher hubServerFetcher,
        LauncherUpdater launcherUpdater, LoginManager loginManager, ServerInfoLoader serverInfoLoader,
        SettingsService settings, Updater updater, IFileDialogService fileDialog,
        INativeTray tray, LocalServerManager localServer)
    {
        _commands = commands;
        _connector = connector;
        _discordAuth = discordAuth;
        _steamAuth = steamAuth;
        _discordRichPresence = discordRichPresence;
        _hubServerFetcher = hubServerFetcher;
        _launcherUpdater = launcherUpdater;
        _loginManager = loginManager;
        _serverInfoLoader = serverInfoLoader;
        _settings = settings;
        _updater = updater;
        _fileDialog = fileDialog;
        _tray = tray;
        _localServer = localServer;
    }

    public void OpenBrowser(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            Log.Warning("Refusing to open non-http(s) URL: {Url}", url);
            return;
        }

        _ = Process.Start(new ProcessStartInfo
        {
            FileName = uri.AbsoluteUri,
            UseShellExecute = true
        });
    }

    public void OpenPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || (!File.Exists(path) && !Directory.Exists(path)))
        {
            Log.Warning("Refusing to open path that does not exist: {Path}", path);
            return;
        }

        _ = Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    public async Task<IFileResult?> PickFileAsync(
        string filter = "Content bundles / replays\0*.zip;*.rt\0All Files\0*.*\0\0",
        CancellationToken cancel = default) => await Dispatcher.UIThread.InvokeAsync(async () => await _fileDialog.PickFileAsync());

    public void InitializeWindow()
    {
        _window = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

        _ = _window?.GetObservable(Window.WindowStateProperty).Subscribe(new AnonymousObserver<WindowState>(_ => WindowStateChanged?.Invoke()));
    }

    public void MinimizeWindow() => Dispatcher.UIThread.Invoke(() => _window?.WindowState = WindowState.Minimized);

    public void ToggleMaximizeWindow() => Dispatcher.UIThread.Invoke(() =>
        _window?.WindowState = _window?.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized);

    public void CloseWindow() => Dispatcher.UIThread.Invoke(() => _window?.Close());

    public bool IsWindowMaximized => Dispatcher.UIThread.Invoke(() => _window?.WindowState == WindowState.Maximized);

    public event Action? WindowStateChanged;

    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    private const uint WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCAPTION = 2;

    public void BeginWindowDrag() => Dispatcher.UIThread.Post(() =>
    {
        if (!OperatingSystem.IsWindows()) { return; }
        if (_window?.TryGetPlatformHandle()?.Handle is not { } hwnd) return;

        _ = ReleaseCapture();
        _ = SendMessage(hwnd, WM_NCLBUTTONDOWN, HTCAPTION, IntPtr.Zero);
    });
}
