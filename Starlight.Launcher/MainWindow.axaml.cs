using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Serilog;
using Starlight.Launcher.Services.WebUI;
using Starlight.Launcher.WebUI.Localization;
using Starlight.Launcher.WebUI.Services;

namespace Starlight.Launcher;

public partial class MainWindow : Window
{
    /// <summary>
    ///     How long the WebView gets to bring up an adapter before we assume it never will and put an
    ///     error on screen. Cold WebKitGTK/WebView2 starts are slow, so this is deliberately generous.
    /// </summary>
    private static readonly TimeSpan _adapterTimeout = TimeSpan.FromSeconds(20);

    /// <summary>
    ///     Linux only: how long the page gets to finish loading once the adapter exists. The WebView stays
    ///     hidden until then, so a WebKit that starts but never draws would otherwise leave an empty window.
    /// </summary>
    private static readonly TimeSpan _pageLoadTimeout = TimeSpan.FromSeconds(25);

    private readonly UiActivity _activity;
    private readonly ILocalizationManager? _loc;
    private WebViewSuspender? _suspender;
    private DispatcherTimer? _adapterWatchdog;
    private DispatcherTimer? _pageWatchdog;
    private bool _adapterCreated;
    private bool _pageLoaded;

    public MainWindow() : this(null, "", new UiActivity(), null) { }

    public MainWindow(Uri? blazorUrl, string pathToWebViewData, UiActivity activity, ILocalizationManager? loc)
    {
        _activity = activity;
        _loc = loc;

        InitializeComponent();

        Web.AdapterCreated += (_, e) =>
        {
            Log.Information("WebView adapter created: {Adapter}", e);
            _adapterCreated = true;
            _adapterWatchdog?.Stop();
            WebViewFallback.IsVisible = false;

            if (OperatingSystem.IsLinux())
                StartPageWatchdog();
        };
        Web.AdapterDestroyed += (_, e) => Log.Warning("WebView adapter destroyed: {Adapter}", e);

        Activated += (_, _) => SetBackgroundPaused(false);
        Deactivated += (_, _) => SetBackgroundPaused(true);
        Web.NavigationCompleted += (_, _) => SetBackgroundPaused(!IsActive);
        Web.PropertyChanged += (_, e) =>
        {
            if (e.Property == NativeWebView.IsSuspendedProperty && !Web.IsSuspended)
                SetBackgroundPaused(!IsActive);
        };

#if DEBUG
        // DevTools are off by default; enabling them makes F12 / Ctrl+Shift+I open the inspector.
        Web.EnvironmentRequested += (_, args) => args.EnableDevTools = true;
#endif

        if (OperatingSystem.IsLinux())
        {
            LinuxWebViewSetup.Configure(Web);

            Web.IsVisible = false;
            Web.NavigationStarted += (_, e) => Log.Information("WebView navigation started: {Url}", e.Request?.GetLeftPart(UriPartial.Path));
            Web.NavigationCompleted += (_, e) =>
            {
                Log.Information("WebView navigation completed: {Url}, success {Success}", e.Request?.GetLeftPart(UriPartial.Path), e.IsSuccess);
                Web.IsVisible = true;

                if (!e.IsSuccess)
                    return;

                _pageLoaded = true;
                _pageWatchdog?.Stop();
                WebViewFallback.IsVisible = false;
            };
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

            _suspender ??= new WebViewSuspender(this, Web, _activity);

            if (!OperatingSystem.IsWindows())
                return;

            await Task.Delay(500);
            if (TryGetPlatformHandle()?.Handle is { } hwnd)
                UnlayerChildren(hwnd);
        };

        Closed += (_, _) =>
        {
            _adapterWatchdog?.Stop();
            _adapterWatchdog = null;
            _pageWatchdog?.Stop();
            _pageWatchdog = null;
            _suspender?.Dispose();
            _suspender = null;
        };
    }

    private async void SetBackgroundPaused(bool paused)
    {
        if (Web.IsSuspended)
            return;

        try
        {
            _ = await Web.InvokeScript(
                $"document.documentElement.classList.toggle('sl-bg-paused', {(paused ? "true" : "false")})");
        }
        catch (Exception e)
        {
            Log.Debug(e, "Couldn't toggle background animation");
        }
    }

    private void StartAdapterWatchdog()
    {
        if (_adapterCreated || _adapterWatchdog is not null)
            return;

        _adapterWatchdog = new DispatcherTimer { Interval = _adapterTimeout };
        _adapterWatchdog.Tick += (_, _) =>
        {
            _adapterWatchdog?.Stop();
            if (_adapterCreated)
                return;

            ShowWebViewFallback(pageNeverLoaded: false);
        };
        _adapterWatchdog.Start();
    }

    private void StartPageWatchdog()
    {
        if (_pageLoaded || _pageWatchdog is not null)
            return;

        _pageWatchdog = new DispatcherTimer { Interval = _pageLoadTimeout };
        _pageWatchdog.Tick += (_, _) =>
        {
            _pageWatchdog?.Stop();
            if (_pageLoaded)
                return;

            Web.IsVisible = false;
            ShowWebViewFallback(pageNeverLoaded: true);
        };
        _pageWatchdog.Start();
    }

    private void ShowWebViewFallback(bool pageNeverLoaded)
    {
        var logs = Path.Combine(AppPaths.AppDataDirectory, "launcher-logs");

        var details = OperatingSystem.IsLinux()
            ? $"{LinuxWebViewSetup.DescribeAvailability()}\n{LinuxWebViewSetup.DescribeSession()}\n{Loc("webview-fallback-logs", ("path", logs))}"
            : Loc("webview-fallback-logs", ("path", logs));

        WebViewFallbackTitle.Text = Loc("webview-fallback-title");
        WebViewFallbackHint.Text = (OperatingSystem.IsLinux(), pageNeverLoaded) switch
        {
            (true, true) => Loc("webview-fallback-linux-page-never-loaded"),
            (true, false) => Loc("webview-fallback-linux-no-engine"),
            _ => Loc("webview-fallback-engine-failed"),
        };

        WebViewFallbackDetails.Text = details;
        WebViewFallback.IsVisible = true;

        if (pageNeverLoaded)
            Log.Error("WebView page did not finish loading within {Timeout}; showing fallback UI. {Details}", _pageLoadTimeout, details);
        else
            Log.Error("WebView adapter was not created within {Timeout}; showing fallback UI. {Details}", _adapterTimeout, details);
    }

    private string Loc(string key, params (string, object?)[] args)
        => _loc?.GetString(key, args) ?? key;

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_LAYERED = 0x00080000;

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static partial int GetWindowLong(IntPtr hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static partial int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool EnumChildWindows(IntPtr hWndParent, delegate* unmanaged<IntPtr, IntPtr, int> lpEnumFunc, IntPtr lParam);

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW")]
    private static unsafe partial int GetClassName(IntPtr hWnd, char* lpClassName, int nMaxCount);

    private static unsafe void UnlayerChildren(IntPtr root)
        => EnumChildWindows(root, &UnlayerCallback, IntPtr.Zero);

    [UnmanagedCallersOnly]
    private static unsafe int UnlayerCallback(IntPtr h, IntPtr _)
    {
        const int Max = 256;
        var buf = stackalloc char[Max];
        var len = GetClassName(h, buf, Max);

        if (len > 0 && new ReadOnlySpan<char>(buf, len).Contains("AvaloniaDumbWindow", StringComparison.Ordinal))
        {
            var ex = GetWindowLong(h, GWL_EXSTYLE);
            if ((ex & WS_EX_LAYERED) != 0)
                _ = SetWindowLong(h, GWL_EXSTYLE, ex & ~WS_EX_LAYERED);
        }

        return 1;
    }
}
