using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Serilog;
using Starlight.Launcher.Services.WebUI;

namespace Starlight.Launcher;

public partial class MainWindow : Window
{
    /// <summary>
    ///     How long the WebView gets to bring up an adapter before we assume it never will and put an
    ///     error on screen. Cold WebKitGTK/WebView2 starts are slow, so this is deliberately generous.
    /// </summary>
    private static readonly TimeSpan AdapterTimeout = TimeSpan.FromSeconds(20);

    private WebViewSuspender? _suspender;
    private DispatcherTimer? _adapterWatchdog;
    private bool _adapterCreated;

    public MainWindow() : this(null, "") { }

    public MainWindow(Uri? blazorUrl, string pathToWebViewData)
    {
        InitializeComponent();

        Web.AdapterCreated += (_, e) =>
        {
            Log.Information("WebView adapter created: {Adapter}", e);
            _adapterCreated = true;
            _adapterWatchdog?.Stop();
            WebViewFallback.IsVisible = false;
        };
        Web.AdapterDestroyed += (_, e) => Log.Warning("WebView adapter destroyed: {Adapter}", e);

#if DEBUG
        // DevTools are off by default; enabling them makes F12 / Ctrl+Shift+I open the inspector.
        Web.EnvironmentRequested += (_, args) => args.EnableDevTools = true;
#endif

        if (OperatingSystem.IsLinux())
        {
            LinuxWebViewSetup.Configure(Web);
        }
        else if (OperatingSystem.IsWindows())
        {
            WindowDecorations = WindowDecorations.BorderOnly;
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaTitleBarHeightHint = 64;

            Web.EnvironmentRequested += (_, args) =>
            {
                if (args is WindowsWebView2EnvironmentRequestedEventArgs w)
                {
                    if (!string.IsNullOrEmpty(pathToWebViewData))
                        w.UserDataFolder = pathToWebViewData;
#if DEBUG
                    w.AdditionalBrowserArguments = "--remote-debugging-port=9222";
#endif
                }
            };
        }

        if (blazorUrl is not null)
            Web.Source = blazorUrl;

        Opened += async (_, _) =>
        {
            StartAdapterWatchdog();

            if (!OperatingSystem.IsWindows())
                return;
            await Task.Delay(500);
            if (TryGetPlatformHandle()?.Handle is { } hwnd)
                UnlayerChildren(hwnd);

            _suspender ??= new WebViewSuspender(this, Web);
        };

        Closed += (_, _) =>
        {
            _adapterWatchdog?.Stop();
            _adapterWatchdog = null;
            _suspender?.Dispose();
            _suspender = null;
        };
    }

    private void StartAdapterWatchdog()
    {
        if (_adapterCreated || _adapterWatchdog is not null)
            return;

        _adapterWatchdog = new DispatcherTimer { Interval = AdapterTimeout };
        _adapterWatchdog.Tick += (_, _) =>
        {
            _adapterWatchdog?.Stop();
            if (_adapterCreated)
                return;

            ShowWebViewFallback();
        };
        _adapterWatchdog.Start();
    }

    private void ShowWebViewFallback()
    {
        var details = OperatingSystem.IsLinux()
            ? LinuxWebViewSetup.DescribeAvailability()
            : "";

        WebViewFallbackHint.Text = OperatingSystem.IsLinux()
            ? "No usable web engine was found. Install the WebKitGTK package for your distribution " +
              "(webkit2gtk-4.1 / libwebkit2gtk-4.1-0 / webkit2gtk-4.0) and start the launcher again."
            : "The embedded web engine failed to start. Please check the launcher logs.";

        WebViewFallbackDetails.Text = details;
        WebViewFallback.IsVisible = true;

        Log.Error("WebView adapter was not created within {Timeout}; showing fallback UI. {Details}", AdapterTimeout, details);
    }

    const int GWL_EXSTYLE = -20;
    const int WS_EX_LAYERED = 0x00080000;

    [DllImport("user32.dll", SetLastError = true)] static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll", SetLastError = true)] static extern int SetWindowLong(IntPtr h, int i, int v);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr h, EnumWindowsProc cb, IntPtr p);
    [DllImport("user32.dll")] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    delegate bool EnumWindowsProc(IntPtr h, IntPtr p);

    static void UnlayerChildren(IntPtr root)
        => EnumChildWindows(root, (h, _) =>
        {
            var sb = new StringBuilder(256);
            _ = GetClassName(h, sb, sb.Capacity);
            if (sb.ToString().Contains("AvaloniaDumbWindow"))
            {
                var ex = GetWindowLong(h, GWL_EXSTYLE);
                if ((ex & WS_EX_LAYERED) != 0)
                    _ = SetWindowLong(h, GWL_EXSTYLE, ex & ~WS_EX_LAYERED);
            }
            return true;
        }, IntPtr.Zero);
}
