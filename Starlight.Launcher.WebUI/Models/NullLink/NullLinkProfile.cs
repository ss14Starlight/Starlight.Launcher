namespace Starlight.Launcher.WebUI.Models.NullLink;

/// <summary>
///     What NullLink knows about an account. Mirrors <c>ProfileResponse</c> of <c>api/profile</c> on the Starlight backend.
/// </summary>
public sealed record NullLinkProfile
{
    public Guid UserId { get; init; }
    public string Username { get; init; } = "";

    public string Authenticator { get; init; } = "";
    public string? AvatarUrl { get; init; }
    public bool DiscordLinked { get; init; }
    public double TotalMinutes { get; init; }
    public NullLinkServerPlayTime[] Servers { get; init; } = [];
    public NullLinkAchievement[] Achievements { get; init; } = [];

    public Dictionary<string, Dictionary<string, double>> Resources { get; init; } = [];
}

public sealed record NullLinkServerPlayTime
{
    public string Id { get; init; } = "";
    public string Project { get; init; } = "";

    /// <summary>
    ///     Only known while the server is registered in the hub.
    /// </summary>
    public string? Title { get; init; }
    public bool Online { get; init; }
    public double OverallMinutes { get; init; }
    public NullLinkTracker[] Trackers { get; init; } = [];
}

public sealed record NullLinkTracker(string Tracker, double Minutes);

public sealed record NullLinkAchievement
{
    public string Id { get; init; } = "";
    public string Server { get; init; } = "";
    public string? Character { get; init; }
    public DateTime UnlockTime { get; init; }
}

public enum NullLinkProfileStatus
{
    Loaded,

    NotLinked,

    Unauthorized,

    Unavailable
}

public sealed record NullLinkProfileResult(NullLinkProfileStatus Status, NullLinkProfile? Profile = null);
