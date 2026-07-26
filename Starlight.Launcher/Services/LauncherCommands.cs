using System.Text;
using System.Threading.Channels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Microsoft.Extensions.Hosting.Internal;
using MudBlazor;
using Robust.Launcher.Api.Models;
using Serilog;
using Starlight.Launcher.Services.Auth;
using Starlight.Launcher.WebUI.Models.Auth;

namespace Starlight.Launcher.Services;

public partial class LauncherCommands
{
    private static string s_reason = "";

    private readonly LoginManager _loginManager;
    private readonly Connector _connector;
    public readonly Channel<string> CommandChannel;

    public event Func<string, Task>? ConnectRequested;

    public LauncherCommands(LoginManager loginManager, Connector connector)
    {
        _loginManager = loginManager;
        _connector = connector;

        CommandChannel = Channel.CreateUnbounded<string>();
    }

    private void ActivateWindow()
    {
        if (Application.Current.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktopLifetime || desktopLifetime.MainWindow is not { } window)
        {
            Log.Warning("ActivateWindow: can't find active window!!!");
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            window.Show();
            window.Activate();
        });
    }

    private static void ActivateWindow(Window window)
    {
        window.Show();
        window.Activate();
    }

    private async Task Connect(string param)
    {
        var reason = s_reason == "" ? null : s_reason;
        // Sanity-check the connection.

        LoggedInAccount? activeAccount = null;
        while (true)
        {
            activeAccount = _loginManager.ActiveAccount;

            if ((activeAccount == null) || (activeAccount.Status == AccountLoginStatus.Unsure))
            {
                await Task.Delay(1000);
            }
            else
            {
                break;
            }
        }

        if (activeAccount!.Status != AccountLoginStatus.Available)
        {
            Log.Warning($"Dropping connect command: Account not available");
            return;
        }

        // Drop the command if we are already connecting.
        if (_connector.ActiveLaunches > 0)
        {
            Log.Warning($"Dropping connect command: Busy connecting to a server");
            return;
        }
        // Note that we don't want to activate the window for something we'll requeue again and again.
        ActivateWindow();
        Log.Information($"Connect command: \"{param}\", \"{reason}\"");

        var handler = ConnectRequested;
        if (handler is null)
        {
            Log.Error("Connect: no UI handler subscribed to ConnectRequested");
            return;
        }

        await handler(param);
    }

    public async ValueTask QueueCommand(string cmd) => await CommandChannel.Writer.WriteAsync(cmd);

    public void Shutdown() => CommandChannel.Writer.Complete();

    public async void RunCommandTask()
    {
        var reader = CommandChannel.Reader;
        while (await reader.WaitToReadAsync())
        {
            var cmd = await reader.ReadAsync();
            try
            {
                await RunSingleCommand(cmd);
            }
            catch (Exception e)
            {
                Log.Error(e, "Exception while processing launcher command {Command}", cmd);
            }
        }
    }

    private async Task RunSingleCommand(string cmd)
    {
        Log.Debug($"Launcher command: {cmd}");

        string? GetUntrustedTextField()
        {
            try
            {
                return Encoding.UTF8.GetString(Convert.FromHexString(cmd[1..]));
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to parse untrusted text field: {ex}");
                return null;
            }
        }

        if (cmd == PingCommand)
        {
            // Yup!
            ActivateWindow();
        }
        else if (cmd == RedialWaitCommand)
        {
            // Redialling wait
            await Task.Delay(1000);
        }
        else if (cmd.StartsWith("R"))
        {
            // Reason (encoded in UTF-8 and then into hex for safety)
            s_reason = GetUntrustedTextField() ?? "";
        }
        else if (cmd.StartsWith("r"))
        {
            // Reason (no encoding)
            s_reason = cmd[1..];
        }
        else if (cmd.StartsWith("C"))
        {
            // Uri (encoded in UTF-8 and then into hex for safety)
            var uri = GetUntrustedTextField();
            if (uri != null)
                await Connect(uri);
        }
        else if (cmd.StartsWith("c"))
        {
            // Used by the "pass URI as argument" logic, doesn't need to bother with safety measures
            await Connect(cmd[1..]);
        }
        else
        {
            Log.Error($"Unhandled launcher command: {cmd}");
        }
    }

    // Command constructors

    public const string PingCommand = ":Ping";
    public const string RedialWaitCommand = ":RedialWait";
    public const string BlankReasonCommand = "r";
    public static string ConstructConnectCommand(Uri uri) => "c" + uri.ToString();
}
