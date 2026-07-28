using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using AvaloniaWebView;

namespace Starlight.Launcher;

public partial class MainWindow : Window
{
    private readonly NativeWebView _web;

    private readonly Uri? _blazorUrl;

    public MainWindow() : this(null) { }

    public MainWindow(Uri? blazorUrl)
    {
        InitializeComponent();

        _blazorUrl = blazorUrl;

        _web = this.FindControl<NativeWebView>("Web")
            ?? throw new InvalidOperationException("WebView not found.");

        if (blazorUrl is not null)
            _web.Source = blazorUrl;

        // so... if you see this, it's just thingy to re-render window on launch only for linux to fix "black" screen =)
        if (OperatingSystem.IsLinux())
        {
            Opened += async (_, _) =>
            {
                await Task.Delay(300);
                var originalWidth = ClientSize.Width;
                Width = originalWidth + 1;
                await Task.Delay(50);
                Width = originalWidth;
            };
        }

#if DEBUG
        Opened += (_, _) => Process.Start(new ProcessStartInfo
        {
            FileName = _blazorUrl!.ToString(),
            UseShellExecute = true
        });
#endif
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
