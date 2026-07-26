using System.Diagnostics;
using Starlight.Launcher.Services.Auth;
using Starlight.Launcher.Services.Discord;
using Starlight.Launcher.Services.ServerStatus;
using Starlight.Launcher.Services.Settings;
using Starlight.Launcher.WebUI.Bridge;
using TerraFX.Interop.DirectX;

namespace Starlight.Launcher.Services.Bridge;

public sealed partial class Bridge : IBridge
{
    private readonly LauncherCommands _commands;
    private readonly Connector _connector;
    private readonly DiscordAuthService _discordAuth;
    private readonly DiscordRichPresence _discordRichPresence;
    private readonly HubServerFetcher _hubServerFetcher;
    private readonly LauncherUpdater _launcherUpdater;
    private readonly LoginManager _loginManager;
    private readonly ServerInfoLoader _serverInfoLoader;
    private readonly SettingsService _settings;
    private readonly Updater _updater;

    public Bridge(LauncherCommands commands, Connector connector, DiscordAuthService discordAuth,
        DiscordRichPresence discordRichPresence, HubServerFetcher hubServerFetcher, LauncherUpdater launcherUpdater,
        LoginManager loginManager, ServerInfoLoader serverInfoLoader, SettingsService settings, Updater updater)
    {
        _commands = commands;
        _connector = connector;
        _discordAuth = discordAuth;
        _discordRichPresence = discordRichPresence;
        _hubServerFetcher = hubServerFetcher;
        _launcherUpdater = launcherUpdater;
        _loginManager = loginManager;
        _serverInfoLoader = serverInfoLoader;
        _settings = settings;
        _updater = updater;
    }

    public void OpenBrowserAsync(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }
}
