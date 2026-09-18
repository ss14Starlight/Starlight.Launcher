using System;

namespace Robust.Launcher.Api.Models;

public static class LoginTokenExt
{
    /// <summary>
    ///     How long before expiry we renew a long-lived SS14 auth token.
    ///     These live for weeks, so renewing a while ahead of time costs nothing.
    /// </summary>
    public static readonly TimeSpan RefreshBuffer = TimeSpan.FromHours(6);

    /// <summary>
    ///     The most we will renew a short-lived Starlight access token ahead of its expiry.
    /// </summary>
    public static readonly TimeSpan ShortRefreshBuffer = TimeSpan.FromMinutes(10);

    /// <summary>
    ///     The least we will renew ahead of expiry, so a very short-lived token still gets renewed
    ///     before it dies rather than right as it does.
    /// </summary>
    public static readonly TimeSpan MinRefreshBuffer = TimeSpan.FromMinutes(1);

    /// <summary>
    ///    Whether the token has expired and is no longer valid.
    /// </summary>
    public static bool IsTimeExpired(this LoginToken token) => token.ExpireTime <= DateTimeOffset.UtcNow;

    /// <summary>
    ///    Whether the token should be refreshed, based on the default refresh buffer.
    /// </summary>
    public static bool ShouldRefresh(this LoginToken token) => token.ShouldRefresh(RefreshBuffer);

    /// <summary>
    ///   Whether the token should be refreshed, based on a custom refresh buffer.
    /// </summary>
    public static bool ShouldRefresh(this LoginToken token, TimeSpan buffer)
        => token.ExpireTime <= DateTimeOffset.UtcNow + buffer;

    /// <summary>
    ///     Time left before the token stops working, clamped at zero.
    /// </summary>
    public static TimeSpan TimeLeft(this LoginToken token)
    {
        var left = token.ExpireTime - DateTimeOffset.UtcNow;
        return left < TimeSpan.Zero ? TimeSpan.Zero : left;
    }

    /// <summary>
    ///     How long the token was issued for, or null if we do not know.
    /// </summary>
    public static TimeSpan? KnownLifetime(this LoginToken token)
        => token.IssuedTime == default || token.ExpireTime <= token.IssuedTime
            ? null
            : token.ExpireTime - token.IssuedTime;

    /// <summary>
    ///     How long before expiry this token should be renewed.
    /// </summary>
    public static TimeSpan RenewalBuffer(this LoginToken token, TimeSpan max)
    {
        if (token.KnownLifetime() is not { } lifetime)
            return max;

        var share = lifetime / 5;

        if (share > max)
            return max;

        return share < MinRefreshBuffer ? MinRefreshBuffer : share;
    }
}
