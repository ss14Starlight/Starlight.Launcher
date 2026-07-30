using System.Diagnostics;
using Microsoft.Win32;
using Serilog;

namespace Starlight.Launcher;

internal static class ProtocolRegistration
{
    private const string Scheme = "starlight";
    private const string ProgId = "Starlight.Launcher.Protocol";
    private const string AppRegName = "Starlight.Launcher";

    public static void RegisterWindows()
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            var exePath = Environment.ProcessPath;
            if (exePath is null)
                return;

            using (var progIdKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}"))
            {
                progIdKey!.SetValue("", "Starlight Launcher Protocol");
                using var iconKey = progIdKey.CreateSubKey("DefaultIcon");
                iconKey!.SetValue("", $"\"{exePath}\",0");
                using var commandKey = progIdKey.CreateSubKey(@"shell\open\command");
                commandKey!.SetValue("", $"\"{exePath}\" \"%1\"");
            }

            using (var capsKey = Registry.CurrentUser.CreateSubKey($@"Software\{AppRegName}\Capabilities"))
            {
                capsKey!.SetValue("ApplicationName", "Starlight Launcher");
                capsKey.SetValue("ApplicationDescription", "Starlight Launcher");
                using var urlAssoc = capsKey.CreateSubKey("URLAssociations");
                urlAssoc!.SetValue(Scheme, ProgId);
            }

            using (var registeredApps = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
            {
                registeredApps!.SetValue(AppRegName, $@"Software\{AppRegName}\Capabilities");
            }

            using var schemeKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{Scheme}");
            schemeKey!.SetValue("", $"URL:{Scheme} Protocol");
            schemeKey.SetValue("URL Protocol", "");
            using var directIconKey = schemeKey.CreateSubKey("DefaultIcon");
            directIconKey!.SetValue("", $"\"{exePath}\",0");
            using var directCommandKey = schemeKey.CreateSubKey(@"shell\open\command");
            directCommandKey!.SetValue("", $"\"{exePath}\" \"%1\"");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to register {scheme} protocol handler (Windows)", Scheme);
        }
    }

    public static void RegisterLinux()
    {
        if (!OperatingSystem.IsLinux())
            return;

        try
        {
            var exePath = Environment.GetEnvironmentVariable("APPIMAGE") ?? Environment.ProcessPath;
            if (exePath is null)
                return;

            var desktopDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "applications");
            _ = Directory.CreateDirectory(desktopDir);

            var desktopPath = Path.Combine(desktopDir, "starlight-launcher.desktop");

            var desktopContent =
                "[Desktop Entry]\n" +
                "Type=Application\n" +
                "Name=Starlight Launcher\n" +
                "Comment=Starlight Launcher for Space Station 14\n" +
                $"Exec=\"{exePath}\" %u\n" +
                "Terminal=false\n" +
                "Categories=Game;\n" +
                "MimeType=x-scheme-handler/starlight;\n";

            if (File.Exists(desktopPath) && File.ReadAllText(desktopPath) == desktopContent)
                return;

            File.WriteAllText(desktopPath, desktopContent);

            RunBestEffort("update-desktop-database", desktopDir);
            RunBestEffort("xdg-mime", "default starlight-launcher.desktop x-scheme-handler/starlight");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to self-register {scheme} protocol handler (Linux)", Scheme);
        }
    }

    private static void RunBestEffort(string fileName, string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            _ = p?.WaitForExit(5000);
        }
        catch
        {
        }
    }
}
