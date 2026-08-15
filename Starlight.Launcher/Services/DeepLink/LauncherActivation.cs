namespace Starlight.Launcher.Services;

public enum LauncherActivationKind
{
    Ping,
    Connect,
    DiscordAuth,
    SteamAuth,
    RedialWait,
}

public sealed record LauncherActivationMessage(LauncherActivationKind Kind, string? Payload = null, string? Reason = null)
{
    public static LauncherActivationMessage Ping() => new(LauncherActivationKind.Ping);
    public static LauncherActivationMessage RedialWait() => new(LauncherActivationKind.RedialWait);
    public static LauncherActivationMessage Connect(Uri uri, string? reason = null) => new(LauncherActivationKind.Connect, uri.ToString(), reason);
    public static LauncherActivationMessage DiscordAuth(Uri uri) => new(LauncherActivationKind.DiscordAuth, uri.ToString());
    public static LauncherActivationMessage SteamAuth(Uri uri) => new(LauncherActivationKind.SteamAuth, uri.ToString());
}

public static class LauncherUriRouter
{
    public static LauncherActivationMessage Classify(Uri uri)
    {
        if (!uri.Host.Equals("auth", StringComparison.OrdinalIgnoreCase))
            return LauncherActivationMessage.Connect(uri);

        var provider = uri.Segments
            .Select(s => s.Trim('/'))
            .FirstOrDefault(s => !string.IsNullOrEmpty(s));

        return provider?.ToLowerInvariant() switch
        {
            "steam" => LauncherActivationMessage.SteamAuth(uri),
            "discord" or null => LauncherActivationMessage.DiscordAuth(uri),
            _ => LauncherActivationMessage.Connect(uri),
        };
    }
}
