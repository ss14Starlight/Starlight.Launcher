namespace Starlight.Launcher.Models.Tray;

public enum TrayMenuIcon
{
    None,
    AppLogo,
    Open,
    Quit,
}

public sealed record TrayMenuItem(
    string Text,
    Action? Invoke = null,
    bool IsSeparator = false,
    TrayMenuIcon Icon = TrayMenuIcon.None,
    bool IsHeader = false,
    bool IsDanger = false)
{
    public static TrayMenuItem Separator => new(string.Empty, IsSeparator: true);
}
