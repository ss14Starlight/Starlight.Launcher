using System.Globalization;

namespace Starlight.Launcher.WebUI.Models.DiscordAuthService;

/// <summary>
///     What the browser hands back to the launcher after an OAuth login.
/// </summary>
public sealed record HandoffResult(string Token, string? RefreshToken, string? SessionId, DateTimeOffset? ExpiresUtc)
{
    /// <summary>
    ///     How long we assume an access token lives when the handoff did not say.
    /// </summary>
    public static readonly TimeSpan AssumedLifetime = TimeSpan.FromMinutes(15);

    public DateTimeOffset EffectiveExpiry => ExpiresUtc ?? (DateTimeOffset.UtcNow + AssumedLifetime);

    public static DateTimeOffset? ParseExpiry(string? expires, string? expiresIn)
    {
        const double MaxLifetimeSeconds = 365 * 24 * 60 * 60;

        if (!string.IsNullOrWhiteSpace(expiresIn)
            && double.TryParse(expiresIn, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            && seconds is > 0 and < MaxLifetimeSeconds)
        {
            return DateTimeOffset.UtcNow.AddSeconds(seconds);
        }

        if (string.IsNullOrWhiteSpace(expires))
            return null;

        if (long.TryParse(expires, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix))
        {
            try
            {
                return unix > 100_000_000_000L
                    ? DateTimeOffset.FromUnixTimeMilliseconds(unix)
                    : DateTimeOffset.FromUnixTimeSeconds(unix);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        if (DateTimeOffset.TryParse(
                expires,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out var absolute))
        {
            return absolute;
        }

        return null;
    }
}
