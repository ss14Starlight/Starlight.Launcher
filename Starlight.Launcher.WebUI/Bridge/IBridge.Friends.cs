using Starlight.Launcher.WebUI.Models.Friends;

namespace Starlight.Launcher.WebUI.Bridge;

public partial interface IBridge
{
    event Action? FriendsChanged;

    Task<FriendsResult> GetFriendsAsync(CancellationToken cancel = default);

    Task<FriendActionResult> SendFriendRequestAsync(string name, CancellationToken cancel = default);

    Task<FriendActionResult> AcceptFriendAsync(Guid userId, CancellationToken cancel = default);

    Task<FriendActionResult> RemoveFriendAsync(Guid userId, CancellationToken cancel = default);

    Task<FriendActionResult> SetSharePresenceAsync(bool share, CancellationToken cancel = default);
}
