namespace Starlight.Launcher.Services;

public enum InstallKind
{
    WindowsInstaller,

    MacBundle,

    Tarball,

    AppImage,

    Deb,

    Pacman,

    Rpm,

    Flatpak,
}

public static class InstallKindDetector
{
    private const string MarkerFileName = "install-kind";

    public static InstallKind Current { get; } = Detect();

    public static bool IsFlatpak => Environment.GetEnvironmentVariable("FLATPAK_ID") is { Length: > 0 } || File.Exists("/.flatpak-info");

    private static InstallKind Detect()
    {
        if (OperatingSystem.IsWindows())
            return InstallKind.WindowsInstaller;
        if (OperatingSystem.IsMacOS())
            return InstallKind.MacBundle;

        if (IsFlatpak)
            return InstallKind.Flatpak;

        if (Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 })
            return InstallKind.AppImage;

        try
        {
            var marker = Path.Combine(AppContext.BaseDirectory, MarkerFileName);
            if (File.Exists(marker))
            {
                switch (File.ReadAllText(marker).Trim().ToLowerInvariant())
                {
                    case "deb":
                        return InstallKind.Deb;
                    case "pacman":
                        return InstallKind.Pacman;
                    case "rpm":
                        return InstallKind.Rpm;
                    case "flatpak":
                        return InstallKind.Flatpak;
                    case "appimage":
                        return InstallKind.AppImage;
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return InstallKind.Tarball;
    }
}
