namespace Starlight.Launcher.WebUI.Models.Friends;

public enum FriendsStatus
{
    Loaded,
    NotLinked,
    Unauthorized,
    Unavailable,
}

public sealed record FriendUser(Guid UserId, string Name);

public sealed record FriendPresence(string? Address, string? ServerName, DateTime SinceUtc);

public sealed record FriendEntry(Guid UserId, string Name, FriendPresence? Presence);

public sealed record FriendsList
{
    public bool SharePresence { get; init; } = true;
    public FriendEntry[] Friends { get; init; } = [];
    public FriendUser[] Incoming { get; init; } = [];
    public FriendUser[] Outgoing { get; init; } = [];
}

public sealed record FriendsResult(FriendsStatus Status, FriendsList? List = null);

public sealed record FriendActionResult(FriendsStatus Status, string? Result = null);
