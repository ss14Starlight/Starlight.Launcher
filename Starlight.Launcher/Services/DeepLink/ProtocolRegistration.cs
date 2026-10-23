using System.Diagnostics;
using Microsoft.Win32;
using Serilog;
using Starlight.Launcher.Services.Settings;

namespace Starlight.Launcher.Services.DeepLink;

internal static class ProtocolRegistration
{
    internal const string Scheme = "starlight";
    private const string ProgId = "Starlight.Launcher.Protocol";
    private const string AppRegName = "Starlight.Launcher";

    internal static readonly string[] Schemes = [Scheme, "ss14", "ss14s"];

    internal static bool IsHandledScheme(string scheme)
        => Schemes.Any(s => s.Equals(scheme, StringComparison.OrdinalIgnoreCase));

    public static void RegisterWindows()
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            var exePath = Environment.ProcessPath;
            if (exePath is null)
                return;

            var command = $"\"{exePath}\" \"%1\"";
            var schemes = Schemes.Where(scheme =>
            {
                using var existingKey = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{scheme}\shell\open\command");
                return !ShouldYieldTo(scheme, existingKey?.GetValue("") as string, command);
            }).ToArray();

            if (schemes.Length == 0)
                return;

            using (var progIdKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}"))
            {
                progIdKey.SetValue("", "Starlight Launcher Protocol");
                using var iconKey = progIdKey.CreateSubKey("DefaultIcon");
                iconKey.SetValue("", $"\"{exePath}\",0");
                using var commandKey = progIdKey.CreateSubKey(@"shell\open\command");
                commandKey.SetValue("", command);
            }

            using (var capsKey = Registry.CurrentUser.CreateSubKey($@"Software\{AppRegName}\Capabilities"))
            {
                capsKey.SetValue("ApplicationName", "Starlight Launcher");
                capsKey.SetValue("ApplicationDescription", "Starlight Launcher");
                using var urlAssoc = capsKey.CreateSubKey("URLAssociations");
                foreach (var scheme in schemes)
                    urlAssoc.SetValue(scheme, ProgId);
            }

            using (var registeredApps = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
            {
                registeredApps.SetValue(AppRegName, $@"Software\{AppRegName}\Capabilities");
            }

            foreach (var scheme in schemes)
            {
                using var schemeKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{scheme}");
                schemeKey.SetValue("", $"URL:{scheme} Protocol");
                schemeKey.SetValue("URL Protocol", "");
                using var directIconKey = schemeKey.CreateSubKey("DefaultIcon");
                directIconKey.SetValue("", $"\"{exePath}\",0");
                using var directCommandKey = schemeKey.CreateSubKey(@"shell\open\command");
                directCommandKey.SetValue("", command);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to register {schemes} protocol handlers (Windows)", Schemes);
        }
    }

    public static void RegisterLinux()
    {
        if (!OperatingSystem.IsLinux())
            return;

        if (InstallKindDetector.Current is InstallKind.Flatpak or InstallKind.Deb or InstallKind.Pacman or InstallKind.Rpm)
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
                "[Desktop Entry]\n"
                    + "Type=Application\n"
                    + "Name=Starlight Launcher\n"
                    + "Comment=Starlight Launcher for Space Station 14\n"
                    + $"Exec=\"{exePath}\" %u\n"
                    + "Terminal=false\n"
                    + "Categories=Game;\n"
                    + $"MimeType={string.Concat(Schemes.Select(s => $"x-scheme-handler/{s};"))}\n";

            var existing = File.Exists(desktopPath) ? File.ReadAllText(desktopPath) : null;
            if (existing == desktopContent)
                return;

            if (ShouldYieldTo(Scheme, existing, desktopContent) || (existing is null && ShouldYieldToSystemDesktopEntry()))
                return;

            File.WriteAllText(desktopPath, desktopContent);

            RunBestEffort("update-desktop-database", desktopDir);
            foreach (var scheme in Schemes)
                RunBestEffort("xdg-mime", $"default starlight-launcher.desktop x-scheme-handler/{scheme}");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to self-register {schemes} protocol handlers (Linux)", Schemes);
        }
    }

    private static bool ShouldYieldTo(string scheme, string? existing, string ours)
    {
#if DEBUG
        if (existing is null || existing == ours || IsForced())
            return false;

        var holder = ExtractQuotedExe(existing);
        if (holder is null || !File.Exists(holder))
            return false;

        Log.Information("Debug build: leaving {scheme}:// registered to {holder}; set {env}=1 to take it over",
            scheme, holder, ForceRegisterEnvVar);
        return true;
#else
        // Release builds always take the handler over.
        _ = scheme;
        _ = existing;
        _ = ours;
        return false;
#endif
    }

    private static bool ShouldYieldToSystemDesktopEntry()
    {
#if DEBUG
        if (IsForced())
            return false;

        var dataDirs = Environment.GetEnvironmentVariable("XDG_DATA_DIRS") is { Length: > 0 } dirs
            ? dirs.Split(':', StringSplitOptions.RemoveEmptyEntries)
            : ["/usr/local/share", "/usr/share"];

        foreach (var dir in dataDirs)
        {
            foreach (var name in new[] { "starlight-launcher.desktop", "io.github.ss14Starlight.Launcher.desktop" })
            {
                var path = Path.Combine(dir, "applications", name);
                if (!File.Exists(path))
                    continue;

                Log.Information("Debug build: leaving {scheme}:// to installed {path}; set {env}=1 to take it over",
                    Scheme, path, ForceRegisterEnvVar);
                return true;
            }
        }
#endif
        return false;
    }

#if DEBUG
    private const string ForceRegisterEnvVar = "STARLIGHT_REGISTER_PROTOCOL";

    private static bool IsForced() => Environment.GetEnvironmentVariable(ForceRegisterEnvVar) == "1";

    private static string? ExtractQuotedExe(string text)
    {
        var start = text.IndexOf('"');
        if (start < 0)
            return null;

        var end = text.IndexOf('"', start + 1);
        return end > start ? text[(start + 1)..end] : null;
    }
#endif

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
