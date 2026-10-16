namespace Starlight.Launcher.Services.DeepLink;

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

    public string ToLogString() => $"{Kind} payload={RedactPayload(Kind, Payload)} reason={Reason}";

    public static string? RedactPayload(LauncherActivationKind kind, string? payload)
    {
        if (payload is null || kind is not (LauncherActivationKind.DiscordAuth or LauncherActivationKind.SteamAuth))
            return payload;

        return Uri.TryCreate(payload, UriKind.Absolute, out var uri)
            ? uri.GetLeftPart(UriPartial.Path) + (string.IsNullOrEmpty(uri.Query) ? "" : "?<redacted>")
            : "<redacted>";
    }
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
