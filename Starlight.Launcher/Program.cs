using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Logging;
using Serilog;
using Starlight.Launcher.Services;

namespace Starlight.Launcher;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        PinBundledLibsodium();

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
            else if (OperatingSystem.IsLinux())
            {
                // Backend selection and the rest of the Linux WebView wiring live in
                // Services/WebUI/LinuxWebViewSetup.cs; only the env vars that have to be set before
                // any WebKit code runs belong here.
                //
                // WebKitGTK's hardware-accelerated compositing (and its DMA-BUF renderer path in
                // particular) is unreliable outside of mainstream GNOME/KDE sessions - it's a known
                // source of blank/transparent windows and outright segfaults on Wayland compositors
                // like Niri, COSMIC, and gamescope (SteamOS). Force the software path unless the
                // user has already made an explicit choice via the environment.
                if (Environment.GetEnvironmentVariable("WEBKIT_DISABLE_COMPOSITING_MODE") is null)
                    Environment.SetEnvironmentVariable("WEBKIT_DISABLE_COMPOSITING_MODE", "1");
                if (Environment.GetEnvironmentVariable("WEBKIT_DISABLE_DMABUF_RENDERER") is null)
                    Environment.SetEnvironmentVariable("WEBKIT_DISABLE_DMABUF_RENDERER", "1");
            }

            var logger = new LoggerConfiguration()
                .WriteTo.Console()
                .WriteTo.File(Path.Combine(AppPaths.AppDataDirectory, "launcher-logs", "log-.log"), restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Debug, rollingInterval: RollingInterval.Day)
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
                    messages = [classified];
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

    // NSec checks the exact libsodium version, so a foreign libsodium found via PATH
    // (e.g. one shipped with PHP) breaks it. Force our bundled copy for every assembly that P/Invokes it.
    private static void PinBundledLibsodium()
    {
        var fileName = OperatingSystem.IsWindows() ? "libsodium.dll"
            : OperatingSystem.IsMacOS() ? "libsodium.dylib"
            : "libsodium.so";

        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, fileName),
            Path.Combine(baseDir, "runtimes", RuntimeInformation.RuntimeIdentifier, "native", fileName),
            Path.Combine(baseDir, "runtimes", GetPortableRid(), "native", fileName),
        };

        var path = candidates.FirstOrDefault(File.Exists);
        if (path == null || !NativeLibrary.TryLoad(path, out var handle))
            return;

        DllImportResolver resolver = (name, _, _) =>
            name is "libsodium" or "libsodium.dll" or "libsodium.so" or "libsodium.dylib" ? handle : IntPtr.Zero;

        foreach (var asmName in new[] { "NSec.Cryptography", "SpaceWizards.Sodium.Interop" })
        {
            try
            {
                NativeLibrary.SetDllImportResolver(Assembly.Load(asmName), resolver);
            }
            catch (Exception e) when (e is FileNotFoundException or InvalidOperationException)
            {
                // Assembly absent or resolver already set.
            }
        }
    }

    private static string GetPortableRid()
    {
        var os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
        var arch = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        return $"{os}-{arch}";
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace(LogEventLevel.Debug, LogArea.Control, LogArea.Visual);
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
