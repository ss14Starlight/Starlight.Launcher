using Starlight.Launcher.WebUI.Bridge;
using Starlight.Launcher.WebUI.Models.Friends;

namespace Starlight.Launcher.Services.Bridge;

public sealed partial class Bridge : IBridge
{
    public event Action? FriendsChanged
    {
        add => _friends.Changed += value;
        remove => _friends.Changed -= value;
    }

    public Task<FriendsResult> GetFriendsAsync(CancellationToken cancel = default) => _friends.GetAsync(cancel);

    public Task<FriendActionResult> SendFriendRequestAsync(string name, CancellationToken cancel = default) => _friends.SendRequestAsync(name, cancel);

    public Task<FriendActionResult> AcceptFriendAsync(Guid userId, CancellationToken cancel = default) => _friends.AcceptAsync(userId, cancel);

    public Task<FriendActionResult> RemoveFriendAsync(Guid userId, CancellationToken cancel = default) => _friends.RemoveAsync(userId, cancel);

    public Task<FriendActionResult> SetSharePresenceAsync(bool share, CancellationToken cancel = default) => _friends.SetSharePresenceAsync(share, cancel);
}
