using System.Diagnostics;
using Avalonia.Threading;
using Serilog;
using Starlight.Launcher.Services.Auth;
using Starlight.Launcher.Services.Discord;
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

    public Bridge(LauncherCommands commands, Connector connector, DiscordAuthService discordAuth,
        SteamAuthService steamAuth, DiscordRichPresence discordRichPresence, HubServerFetcher hubServerFetcher,
        LauncherUpdater launcherUpdater, LoginManager loginManager, ServerInfoLoader serverInfoLoader,
        SettingsService settings, Updater updater, IFileDialogService fileDialog,
        INativeTray tray)
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
}
