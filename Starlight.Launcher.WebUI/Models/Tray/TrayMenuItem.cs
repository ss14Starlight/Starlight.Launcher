namespace Starlight.Launcher.WebUI.Models.Tray;

public sealed record TrayMenuItem(string Text, Action? Invoke = null, bool IsSeparator = false)
{
    public static TrayMenuItem Separator => new(string.Empty, IsSeparator: true);
}
