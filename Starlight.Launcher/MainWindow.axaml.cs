using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;

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

        if (OperatingSystem.IsLinux())
        {
            _web.EnvironmentRequested += (_, args) =>
            {
                if (args is LinuxWpeWebViewEnvironmentRequestedEventArgs wpeArgs)
                    wpeArgs.PreferWebKitGtkInstead = true;
            };
        }

        if (blazorUrl is not null)
            _web.Source = blazorUrl;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
