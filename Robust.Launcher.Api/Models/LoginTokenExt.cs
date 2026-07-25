using System;

namespace Robust.Launcher.Api.Models;

public static class LoginTokenExt
{
    public static readonly TimeSpan RefreshBuffer = TimeSpan.FromHours(6);

    public static bool IsTimeExpired(this LoginToken token) => token.ExpireTime <= DateTimeOffset.UtcNow;

    public static bool ShouldRefresh(this LoginToken token) => token.ExpireTime <= DateTimeOffset.UtcNow + RefreshBuffer;
}
