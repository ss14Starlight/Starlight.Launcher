using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Starlight.Launcher;

public partial class MainWindow : Window
{
    public MainWindow() : this(null, "") { }

    public MainWindow(Uri? blazorUrl, string pathToWebViewData)
    {
        InitializeComponent();

        if (OperatingSystem.IsLinux())
        {
            Web.EnvironmentRequested += (_, args) =>
            {
                if (args is LinuxWpeWebViewEnvironmentRequestedEventArgs wpeArgs)
                    wpeArgs.PreferWebKitGtkInstead = true;
            };
        }
        else if (OperatingSystem.IsWindows())
        {
            Web.EnvironmentRequested += (_, args) =>
            {
                if (args is WindowsWebView2EnvironmentRequestedEventArgs w)
                {
                    if (!string.IsNullOrEmpty(pathToWebViewData))
                        w.UserDataFolder = pathToWebViewData;

                    w.AdditionalBrowserArguments = "--auto-open-devtools-for-tabs --remote-debugging-port=9222";
                }
            };
        }

        if (blazorUrl is not null)
            Web.Source = blazorUrl;

        Opened += async (_, _) =>
        {
            await Task.Delay(500);
            if (TryGetPlatformHandle()?.Handle is { } hwnd)
                UnlayerChildren(hwnd);
        };
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
