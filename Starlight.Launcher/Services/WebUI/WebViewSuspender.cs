using System.Runtime;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Serilog;
using Starlight.Launcher.WebUI.Services;

namespace Starlight.Launcher.Services.WebUI;

/// <summary>
///     Freezes the WebView while the window is minimized or hidden in the tray, and tells the UI to stop
///     its own background work (<see cref="UiActivity" />), so a hidden launcher costs next to nothing.
/// </summary>
public sealed partial class WebViewSuspender : IDisposable
{
    private static readonly TimeSpan _suspendDelay = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan _trimDelay = TimeSpan.FromSeconds(30);

    private const int MaxSuspendAttempts = 3;

    private readonly NativeWebView _webView;
    private readonly Window _window;
    private readonly UiActivity _activity;
    private readonly DispatcherTimer _suspendTimer;
    private readonly DispatcherTimer _trimTimer;
    private int _suspendAttempts;
    private bool _trimmed;
    private bool _disposed;

    public WebViewSuspender(Window window, NativeWebView webView, UiActivity activity)
    {
        _window = window;
        _webView = webView;
        _activity = activity;

        _suspendTimer = new DispatcherTimer { Interval = _suspendDelay };
        _suspendTimer.Tick += (_, _) =>
        {
            _suspendTimer.Stop();
            ApplySuspend();
        };

        _trimTimer = new DispatcherTimer { Interval = _trimDelay };
        _trimTimer.Tick += (_, _) =>
        {
            _trimTimer.Stop();
            TrimMemory();
        };

        _window.PropertyChanged += OnWindowPropertyChanged;
        Update();
    }

    private bool _shouldBeActive
        => _window.IsVisible && _window.WindowState != WindowState.Minimized;

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty || e.Property == Visual.IsVisibleProperty)
            Update();
    }

    private void Update()
    {
        if (_disposed)
            return;

        if (_shouldBeActive)
        {
            _suspendTimer.Stop();
            _trimTimer.Stop();
            _suspendAttempts = 0;
            _trimmed = false;

            if (_webView.IsSuspended)
                Log.Information("Resuming WebView");

            _webView.Resume();
            _activity.SetActive(true);
            return;
        }

        _activity.SetActive(false);

        if (!_suspendTimer.IsEnabled && !_webView.IsSuspended)
            _suspendTimer.Start();

        if (!_trimTimer.IsEnabled && !_trimmed)
            _trimTimer.Start();
    }

    private async void ApplySuspend()
    {
        if (_disposed || _shouldBeActive)
            return;

        _suspendAttempts++;
        bool ok;
        try
        {
            ok = await _webView.TrySuspendAsync();
        }
        catch (Exception e)
        {
            Log.Warning(e, "Failed to suspend WebView");
            return;
        }

        if (_disposed || _shouldBeActive)
            return;

        if (ok)
        {
            Log.Information("WebView suspended");
            return;
        }

        if (_suspendAttempts < MaxSuspendAttempts)
            _suspendTimer.Start();
        else
            Log.Debug("WebView can't be suspended on this backend, relying on it throttling hidden views");
    }

    private void TrimMemory()
    {
        if (_disposed || _shouldBeActive || _trimmed)
            return;

        _trimmed = true;

        _ = Task.Run(() =>
        {
            try
            {
                var before = Environment.WorkingSet;

                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();

                if (OperatingSystem.IsWindows())
                    _ = EmptyWorkingSet(GetCurrentProcess());
                else if (OperatingSystem.IsLinux())
                    TryMallocTrim();

                Log.Information("Trimmed memory while hidden: working set {Before} MB -> {After} MB",
                    before / (1024 * 1024), Environment.WorkingSet / (1024 * 1024));
            }
            catch (Exception e)
            {
                Log.Debug(e, "Memory trim failed");
            }
        });
    }

    private static void TryMallocTrim()
    {
        try
        {
            _ = malloc_trim(0);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
        }
    }

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GetCurrentProcess();

    [LibraryImport("kernel32.dll", EntryPoint = "K32EmptyWorkingSet")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EmptyWorkingSet(IntPtr process);

    [LibraryImport("libc", EntryPoint = "malloc_trim")]
    private static partial int malloc_trim(nuint pad);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _suspendTimer.Stop();
        _trimTimer.Stop();
        _window.PropertyChanged -= OnWindowPropertyChanged;
        _activity.SetActive(true);
    }
}
