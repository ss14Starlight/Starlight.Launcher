using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Logging;
using Serilog;
using Starlight.Launcher.Helpers;
using Starlight.Launcher.Services.DeepLink;
using Starlight.Launcher.Services.Logging;

namespace Starlight.Launcher;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        PinBundledLibsodium();
        PinBundledSkia();

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
                SetNativeEnvDefault("WEBKIT_DISABLE_COMPOSITING_MODE", "1");
                SetNativeEnvDefault("WEBKIT_DISABLE_DMABUF_RENDERER", "1");
                // Newer WebKitGTK always uses the DMA-BUF renderer and ignores the variable above, then
                // fails to allocate GBM buffers on some drivers (NVIDIA) and renders nothing. Hand frames
                // over through shared memory instead.
                SetNativeEnvDefault("WEBKIT_DMABUF_RENDERER_FORCE_SHM", "1");
            }

            var logger = new LoggerConfiguration()
                .WriteTo.Console()
                .WriteTo.File(Path.Combine(AppPaths.AppDataDirectory, "launcher-logs", "log-.log"), restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Debug, rollingInterval: RollingInterval.Day)
                .CreateLogger();
            Log.Logger = logger;
            GlobalExceptionHandlers.Install();

            var messaging = new LauncherMessaging();
            var messages = new[] { LauncherActivationMessage.Ping() };
            var sendAnyway = false;

            if (args.Length == 1)
            {
                if (Uri.TryCreate(args[0], UriKind.Absolute, out var uri))
                {
                    var classified = LauncherUriRouter.Classify(uri);
                    logger.Information("Classified activation URI {uri} as {kind}", uri.GetLeftPart(UriPartial.Path), classified.Kind);
                    messages = [classified];
                    sendAnyway = true;
                }
                else
                {
                    logger.Warning("Got exactly one argv entry but it didn't parse as a URI: {arg}", args[0]);
                }
            }

            logger.Information("IPC: sending/claiming with {Kinds}", messages.Select(m => m.Kind));

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

            Log.Fatal(ex, "Launcher crashed");
            throw;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    // NSec checks the exact libsodium version, so a foreign libsodium found via PATH
    // (e.g. one shipped with PHP) breaks it. Force our bundled copy for every assembly that P/Invokes it.
    private static void PinBundledLibsodium() =>
        PinBundledNativeLibrary("libsodium", "NSec.Cryptography", "SpaceWizards.Sodium.Interop");

    private static void PinBundledSkia()
    {
        PinBundledNativeLibrary("libSkiaSharp", "SkiaSharp");
        PinBundledNativeLibrary("libHarfBuzzSharp", "HarfBuzzSharp");
    }

    private static void PinBundledNativeLibrary(string libName, params string[] assemblies)
    {
        var extension = OperatingSystem.IsWindows() ? ".dll"
            : OperatingSystem.IsMacOS() ? ".dylib"
            : ".so";
        var fileName = libName + extension;

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

        nint resolver(string name, Assembly _1, DllImportSearchPath? _2) =>
            name == libName || name == libName + ".dll" || name == libName + ".so" || name == libName + ".dylib"
                ? handle
                : IntPtr.Zero;

        foreach (var asmName in assemblies)
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

    /// <summary>
    ///     Sets an environment variable unless the user has already set it. Needed because native libs
    ///		(seemingly) can't see stuff set by <see cref="Environment.SetEnvironmentVariable(string, string)"/>.
    /// </summary>
    private static void SetNativeEnvDefault(string name, string value)
    {
        if (Environment.GetEnvironmentVariable(name) is not null)
            return;

        Environment.SetEnvironmentVariable(name, value);
        if (!OperatingSystem.IsWindows())
            _ = setenv(name, value, 0);
    }

#pragma warning disable CA2101, SYSLIB1054
    [DllImport("libc", SetLastError = true)]
    private static extern int setenv(string name, string value, int overwrite);
#pragma warning restore SYSLIB1054, CA2101

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
