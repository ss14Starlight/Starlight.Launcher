using System.Diagnostics;
using Serilog;

namespace Starlight.Launcher.Services.Journal;

public static class DesktopNotifier
{
    private const string WindowsAppId = @"{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\WindowsPowerShell\v1.0\powershell.exe";

    private const string WindowsScript = """
        $ErrorActionPreference = 'Stop'
        [Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
        [Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime] | Out-Null
        $t = [Security.SecurityElement]::Escape($env:SL_TOAST_TITLE)
        $b = [Security.SecurityElement]::Escape($env:SL_TOAST_BODY)
        $xml = New-Object Windows.Data.Xml.Dom.XmlDocument
        $xml.LoadXml("<toast><visual><binding template='ToastGeneric'><text>$t</text><text>$b</text></binding></visual></toast>")
        [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier($env:SL_TOAST_APP).Show([Windows.UI.Notifications.ToastNotification]::new($xml))
        """;

    public static void Show(string title, string message)
    {
        try
        {
            var start = CreateStartInfo(title, message);
            if (start is null)
                return;

            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            using var process = Process.Start(start);
        }
        catch (Exception e)
        {
            Log.Warning(e, "Can't show a desktop notification");
        }
    }

    private static ProcessStartInfo? CreateStartInfo(string title, string message)
    {
        if (OperatingSystem.IsWindows())
        {
            var start = new ProcessStartInfo("powershell.exe");
            foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden", "-ExecutionPolicy", "Bypass", "-Command", WindowsScript })
                start.ArgumentList.Add(arg);

            start.Environment["SL_TOAST_TITLE"] = title;
            start.Environment["SL_TOAST_BODY"] = message;
            start.Environment["SL_TOAST_APP"] = WindowsAppId;
            return start;
        }

        if (OperatingSystem.IsMacOS())
        {
            var start = new ProcessStartInfo("osascript");
            start.ArgumentList.Add("-e");
            start.ArgumentList.Add("on run argv\ndisplay notification (item 2 of argv) with title (item 1 of argv)\nend run");
            start.ArgumentList.Add(title);
            start.ArgumentList.Add(message);
            return start;
        }

        if (OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD())
        {
            var start = new ProcessStartInfo("notify-send");
            start.ArgumentList.Add("--app-name=Starlight Launcher");
            start.ArgumentList.Add(title);
            start.ArgumentList.Add(message);
            return start;
        }

        return null;
    }
}
