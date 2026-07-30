using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;

namespace Starlight.Launcher;

public partial class MainWindow : Window
{
    private readonly NativeWebView _web;

    public MainWindow() : this(null, "") { }

    public MainWindow(Uri? blazorUrl, string pathToWebViewData)
    {
        InitializeComponent();

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
        else if (OperatingSystem.IsWindows())
        {
            _web.EnvironmentRequested += (_, args) =>
            {
                if (args is WindowsWebView2EnvironmentRequestedEventArgs webViewArgs && !string.IsNullOrEmpty(pathToWebViewData))
                    webViewArgs.UserDataFolder = pathToWebViewData;
            };
        }

        if (blazorUrl is not null)
            _web.Source = blazorUrl;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
