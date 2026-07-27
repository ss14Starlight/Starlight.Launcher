using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.WebView.Desktop;
using Serilog;
using Starlight.Launcher.Services;

namespace Starlight.Launcher;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
#if DEBUG
            if (OperatingSystem.IsWindows())
                ConsoleHelper.CreateConsole();
#endif

            if (OperatingSystem.IsWindows())
            {
                var userData = Path.Combine(AppPaths.AppDataDirectory, "WebView2");
                Directory.CreateDirectory(userData);
                Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", userData);
            }

            var logger = new LoggerConfiguration()
                .WriteTo.Console()
                .WriteTo.File(Path.Combine(AppPaths.AppDataDirectory, "log.txt"), rollingInterval: RollingInterval.Day)
                .CreateLogger();
            Log.Logger = logger;


            // Single-instance / protocol hand-off check. Same logic as before, just no
            var messaging = new LauncherMessaging();
            string[] commands = { LauncherCommands.PingCommand };
            var commandSendAnyway = false;

            if (args.Length == 1)
            {
                if (Uri.TryCreate(args[0], UriKind.Absolute, out var result))
                {
                    commands = result.Host.Equals("auth", StringComparison.OrdinalIgnoreCase)
                        ? [LauncherCommands.ConstructAuthCommand(result)]
                        : [LauncherCommands.BlankReasonCommand, LauncherCommands.ConstructConnectCommand(result)];
                    commandSendAnyway = true;
                }
            }
            else if (args.Length >= 2 && args[0] == "--commands")
            {
                commands = args.Skip(1).ToArray();
                commandSendAnyway = true;
            }

            if (messaging.SendCommandsOrClaim(commands, commandSendAnyway))
                return 0;

            // Stash so App.OnFrameworkInitializationCompleted can register the SAME
            // instance into DI instead of constructing a second LauncherMessaging.
            App.PendingMessaging = messaging;

            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            var text = ex.ToString();
            System.Diagnostics.Debug.WriteLine(text);
            try
            {
                File.WriteAllText(Path.Combine(AppPaths.AppDataDirectory, "startup-crash.txt"), text);
            }
            catch
            {
                /* best effort - don't let logging failure mask the real crash */
            }

            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .UseDesktopWebView(); // from WebView.Avalonia.Desktop - wires WebView2 / WKWebView / WebKitGTK per-OS
}

/// <summary>
/// MAUI Essentials' FileSystem.Current.AppDataDirectory replacement.
/// LocalApplicationData is XDG-correct on Linux (~/.local/share) and correct on Windows;
/// macOS gets special-cased since Personal/LocalApplicationData don't map to
/// ~/Library/Application Support the way you'd want for a desktop app.
/// </summary>
internal static class AppPaths
{
    public static string AppDataDirectory
    {
        get
        {
            var baseDir = OperatingSystem.IsMacOS()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Library", "Application Support")
                : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            var dir = Path.Combine(baseDir, "Starlight.Launcher");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
