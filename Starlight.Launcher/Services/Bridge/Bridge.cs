using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Reactive;
using Avalonia.Threading;
using Serilog;
using Starlight.Launcher.Services.Auth;
using Starlight.Launcher.Services.ContentManagement;
using Starlight.Launcher.Services.DeepLink;
using Starlight.Launcher.Services.Discord;
using Starlight.Launcher.Services.EngineManager;
using Starlight.Launcher.Services.Friends;
using Starlight.Launcher.Services.GameLaunch;
using Starlight.Launcher.Services.Journal;
using Starlight.Launcher.Services.LocalServer;
using Starlight.Launcher.Services.Replays;
using Starlight.Launcher.Services.ServerStatus;
using Starlight.Launcher.Services.Settings;
using Starlight.Launcher.WebUI.Bridge;
using Starlight.Launcher.WebUI.Models.Helpers;
using Starlight.Launcher.WebUI.Services;

namespace Starlight.Launcher.Services.Bridge;

public sealed partial class Bridge(LauncherCommands commands, Connector connector, DiscordAuthService discordAuth,
    SteamAuthService steamAuth, DiscordRichPresence discordRichPresence, HubServerFetcher hubServerFetcher,
    LauncherUpdater launcherUpdater, LoginManager loginManager, ServerInfoLoader serverInfoLoader,
    SettingsService settings, Updater updater, IFileDialogService fileDialog,
    INativeTray tray, LocalServerManager localServer, StarlightAuthApi starlightAuth,
    EngineSourceGuard engineSourceGuard, ContentManager content, IEngineManager engineManager,
    JournalService journal, ServerMonitor monitor, FriendsService friends, ReplaysService replays) : IBridge
{
    private readonly LauncherCommands _commands = commands;
    private readonly Connector _connector = connector;
    private readonly DiscordAuthService _discordAuth = discordAuth;
    private readonly SteamAuthService _steamAuth = steamAuth;
    private readonly DiscordRichPresence _discordRichPresence = discordRichPresence;
    private readonly HubServerFetcher _hubServerFetcher = hubServerFetcher;
    private readonly LauncherUpdater _launcherUpdater = launcherUpdater;
    private readonly LoginManager _loginManager = loginManager;
    private readonly ServerInfoLoader _serverInfoLoader = serverInfoLoader;
    private readonly SettingsService _settings = settings;
    private readonly Updater _updater = updater;
    private readonly IFileDialogService _fileDialog = fileDialog;
    private readonly INativeTray _tray = tray;
    private readonly LocalServerManager _localServer = localServer;
    private readonly StarlightAuthApi _starlightAuth = starlightAuth;
    private readonly EngineSourceGuard _engineSourceGuard = engineSourceGuard;
    private readonly ContentManager _content = content;
    private readonly IEngineManager _engineManager = engineManager;
    private readonly JournalService _journal = journal;
    private readonly ServerMonitor _monitor = monitor;
    private readonly FriendsService _friends = friends;
    private readonly ReplaysService _replays = replays;
    private Window? _window;

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

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ReleaseCapture();
    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    private static partial IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    private const uint WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCAPTION = 2;

    public void BeginWindowDrag() => Dispatcher.UIThread.Post(() =>
    {
        if (!OperatingSystem.IsWindows()) { return; }
        if (_window?.TryGetPlatformHandle()?.Handle is not { } hwnd) return;

        try
        {
            _ = ReleaseCapture();
            _ = SendMessage(hwnd, WM_NCLBUTTONDOWN, HTCAPTION, IntPtr.Zero);
        }
        catch (Exception e)
        {
            Log.Error(e, "Window drag failed");
        }
    });
}
