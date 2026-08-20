using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Starlight.Launcher.Services.WebUI;

public sealed class WebViewSuspender : IDisposable
{
    private readonly NativeWebView _webView;
    private readonly Window _window;
    private readonly DispatcherTimer _debounce;
    private bool _disposed;

    public WebViewSuspender(Window window, NativeWebView webView, TimeSpan? delay = null)
    {
        _window = window;
        _webView = webView;

        _debounce = new DispatcherTimer { Interval = delay ?? TimeSpan.FromSeconds(2) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); ApplySuspend(); };

        _window.PropertyChanged += OnWindowPropertyChanged;
    }

    private bool _shouldBeActive =>
        _window.IsVisible && _window.WindowState != WindowState.Minimized;

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Window.WindowStateProperty && e.Property != Visual.IsVisibleProperty)
            return;

        if (_shouldBeActive)
        {
            _debounce.Stop();
            _webView.Resume();
        }
        else if (!_debounce.IsEnabled)
        {
            _debounce.Start();
        }
    }

    private async void ApplySuspend()
    {
        if (_disposed || _shouldBeActive)
            return;

        var ok = await _webView.TrySuspendAsync();

        if (!ok && !_shouldBeActive && !_disposed)
            _debounce.Start();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _debounce.Stop();
        _window.PropertyChanged -= OnWindowPropertyChanged;
    }
}
