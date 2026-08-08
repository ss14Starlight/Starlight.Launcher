using Avalonia;
using Serilog;
using Serilog.Events;
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

            Console.WriteLine($"argv: [{string.Join(", ", args.Select(a => $"\"{a}\""))}]");
#endif

            if (OperatingSystem.IsWindows())
                ProtocolRegistration.RegisterWindows();
            else if (OperatingSystem.IsLinux())
                ProtocolRegistration.RegisterLinux();

            if (OperatingSystem.IsWindows())
            {
                var userData = Path.Combine(AppPaths.AppDataDirectory, "WebView2");
                Directory.CreateDirectory(userData);
                Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", userData);
            }

            var logger = new LoggerConfiguration()
                .WriteTo.Console()
                .WriteTo.File(Path.Combine(AppPaths.AppDataDirectory, "launcher-logs", "log-.log"), restrictedToMinimumLevel: LogEventLevel.Information, rollingInterval: RollingInterval.Day)
                .CreateLogger();
            Log.Logger = logger;

            var messaging = new LauncherMessaging();
            var messages = new[] { LauncherActivationMessage.Ping() };
            var sendAnyway = false;

            if (args.Length == 1)
            {
                if (Uri.TryCreate(args[0], UriKind.Absolute, out var uri))
                {
                    var classified = LauncherUriRouter.Classify(uri);
                    logger.Information("Classified activation URI {uri} as {kind}", uri, classified.Kind);
                    messages = new[] { classified };
                    sendAnyway = true;
                }
                else
                {
                    logger.Warning("Got exactly one argv entry but it didn't parse as a URI: {arg}", args[0]);
                }
            }

            logger.Information("IPC: sending/claiming with {@Messages}", messages);

            if (messaging.SendMessagesOrClaim(messages, sendAnyway))
                return 0;

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
            .LogToTrace();
}

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
            _ = Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
