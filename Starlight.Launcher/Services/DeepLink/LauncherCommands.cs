using System.Threading.Channels;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Robust.Launcher.Api.Models;
using Starlight.Launcher.Services.Auth;
using Starlight.Launcher.WebUI.Models.Auth;

namespace Starlight.Launcher.Services;

public partial class LauncherCommands(ILogger<LauncherCommands> logger, LoginManager loginManager, Connector connector, DiscordAuthService discordAuth, SteamAuthService steamAuth)
{
    private readonly ILogger<LauncherCommands> _logger = logger;
    private readonly LoginManager _loginManager = loginManager;
    private readonly Connector _connector = connector;
    private readonly DiscordAuthService _discordAuth = discordAuth;
    private readonly SteamAuthService _steamAuth = steamAuth;
    public readonly Channel<LauncherActivationMessage> CommandChannel = Channel.CreateUnbounded<LauncherActivationMessage>();

    public event Func<string, Task>? ConnectRequested;

    private void ActivateWindow()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktopLifetime || desktopLifetime.MainWindow is not { } window)
        {
            _logger.LogWarning("ActivateWindow: can't find active window!!!");
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            window.Show();
            window.Activate();
        });
    }

    private async Task Connect(string address, string? reason)
    {
        LoggedInAccount? activeAccount;
        while (true)
        {
            activeAccount = _loginManager.ActiveAccount;

            if (activeAccount == null || activeAccount.Status == AccountLoginStatus.Unsure)
                await Task.Delay(1000);
            else
                break;
        }

        if (activeAccount!.Status != AccountLoginStatus.Available)
        {
            _logger.LogWarning("Dropping connect command: Account not available");
            return;
        }

        if (_connector.ActiveLaunches > 0)
        {
            _logger.LogWarning("Dropping connect command: Busy connecting to a server");
            return;
        }

        // Note that we don't want to activate the window for something we'll requeue again and again.
        ActivateWindow();
        _logger.LogInformation("Connect command: \"{Address}\", \"{Reason}\"", address, reason);

        var handler = ConnectRequested;
        if (handler is null)
        {
            _logger.LogError("Connect: no UI handler subscribed to ConnectRequested");
            return;
        }

        await handler(address);
    }

    public async ValueTask QueueMessage(LauncherActivationMessage message) => await CommandChannel.Writer.WriteAsync(message);

    public void Shutdown() => CommandChannel.Writer.Complete();

    public async void RunCommandTask()
    {
        var reader = CommandChannel.Reader;
        while (await reader.WaitToReadAsync())
        {
            var message = await reader.ReadAsync();
            try
            {
                await Dispatch(message);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Exception while processing activation message {Kind}", message.Kind);
            }
        }
    }

    private async Task Dispatch(LauncherActivationMessage message)
    {
        _logger.LogDebug("Activation message: {Kind} payload={Payload}", message.Kind, message.Payload);

        switch (message)
        {
            case { Kind: LauncherActivationKind.Ping }:
                ActivateWindow();
                break;

            case { Kind: LauncherActivationKind.RedialWait }:
                await Task.Delay(1000);
                break;

            case { Kind: LauncherActivationKind.Connect, Payload: { } address }:
                await Connect(address, message.Reason);
                break;

            case { Kind: LauncherActivationKind.DiscordAuth, Payload: { } uriString }:
                if (Uri.TryCreate(uriString, UriKind.Absolute, out var discordUri))
                {
                    _logger.LogInformation("Dispatching Discord auth deep link: {uri}", discordUri);
                    _discordAuth.HandleDeepLink(discordUri);
                }
                else
                {
                    _logger.LogError("Bad auth deep link payload: {Payload}", uriString);
                }
                break;

            case { Kind: LauncherActivationKind.SteamAuth, Payload: { } uriString }:
                if (Uri.TryCreate(uriString, UriKind.Absolute, out var steamUri))
                {
                    _logger.LogInformation("Dispatching Steam auth deep link: {uri}", steamUri);
                    _steamAuth.HandleDeepLink(steamUri);
                }
                else
                {
                    _logger.LogError("Bad auth deep link payload: {Payload}", uriString);
                }
                break;

            default:
                _logger.LogError("Unhandled or malformed activation message: {@Message}", message);
                break;
        }
    }
}
