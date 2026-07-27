namespace Starlight.Launcher.Services;

public enum LauncherActivationKind
{
    Ping,
    Connect,
    DiscordAuth,
    RedialWait,
}

public sealed record LauncherActivationMessage(LauncherActivationKind Kind, string? Payload = null, string? Reason = null)
{
    public static LauncherActivationMessage Ping() => new(LauncherActivationKind.Ping);
    public static LauncherActivationMessage RedialWait() => new(LauncherActivationKind.RedialWait);
    public static LauncherActivationMessage Connect(Uri uri, string? reason = null) => new(LauncherActivationKind.Connect, uri.ToString(), reason);
    public static LauncherActivationMessage DiscordAuth(Uri uri) => new(LauncherActivationKind.DiscordAuth, uri.ToString());
}

public static class LauncherUriRouter
{
    public static LauncherActivationMessage Classify(Uri uri) =>
        uri.Host.Equals("auth", StringComparison.OrdinalIgnoreCase)
            ? LauncherActivationMessage.DiscordAuth(uri)
            : LauncherActivationMessage.Connect(uri);
}
