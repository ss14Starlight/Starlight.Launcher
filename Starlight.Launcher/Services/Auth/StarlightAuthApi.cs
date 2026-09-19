using Serilog;
using Starlight.Launcher.Services.Settings;
using Starlight.Launcher.WebUI.Models.DiscordAuthService;
using Starlight.Launcher.WebUI.Models.NullLink;
using Starlight.Launcher.WebUI.Models.StarlightAuthService;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Starlight.Launcher.Services.Auth;

/// <summary>
///     What happened when we asked the auth server about a token.
/// </summary>
public enum TokenCheckOutcome
{
    /// <summary>The server answered and the token is good.</summary>
    Valid,

    /// <summary>The server answered and the token is dead (expired, revoked, reuse detected).</summary>
    Invalid,

    /// <summary>We could not get an answer. The token may well still be fine.</summary>
    Unavailable
}

/// <summary>
///    The result of a token refresh attempt.
/// </summary>
public sealed record TokenRefreshResult(TokenCheckOutcome Outcome, StarlightRefreshResult? Tokens)
{
    public static readonly TokenRefreshResult Invalid = new(TokenCheckOutcome.Invalid, null);
    public static readonly TokenRefreshResult Unavailable = new(TokenCheckOutcome.Unavailable, null);
}

public sealed class StarlightAuthApi(HttpClient http, SettingsService settings)
{
    /// <summary>
    ///     Cap on how long a single auth call may take. The shared <see cref="HttpClient"/> defaults to
    ///     100 seconds, which is long enough for the UI to look frozen.
    /// </summary>
    private static readonly TimeSpan _requestTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    ///     Read live so that changing the API URL in settings takes effect without a restart,
    ///     and so we do not capture a URL before settings finished loading.
    /// </summary>
    public Uri ApiUrl => new(settings.GetSettings().StarlightAPIUrl);

    /// <summary>
    ///    Builds a URL to hand the user off to the launcher login page for the given provider.
    /// </summary>
    public string BuildLauncherLoginUrl(bool steam, string state)
        => new Uri(ApiUrl, $"api/{(steam ? "steam" : "discord")}-auth/launcher-login?state={Uri.EscapeDataString(state)}").ToString();

    /// <summary>
    ///    Asks the auth server for the user ID and username associated with a Discord token.
    /// </summary>
    public async Task<DiscordUserResponse> GetDiscordUserAsync(string discordToken, CancellationToken cancel)
    {
        using var timeout = Linked(cancel);

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(ApiUrl, "api/discord-auth/find-user"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", discordToken);

        using var resp = await http.SendAsync(request, timeout.Token);

        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(timeout.Token);
            throw new DiscordAuthException($"find-user failed: {(int)resp.StatusCode} {body}");
        }

        return await resp.Content.ReadFromJsonAsync<DiscordUserResponse>(cancellationToken: timeout.Token)
               ?? throw new DiscordAuthException("Empty response.");
    }

    /// <summary>
    ///    Asks the auth server for the user ID and username associated with a Steam token.
    /// </summary>
    public async Task<SteamUserResponse> GetSteamUserAsync(string steamToken, CancellationToken cancel)
    {
        using var timeout = Linked(cancel);

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(ApiUrl, "api/steam-auth/find-user"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", steamToken);

        using var resp = await http.SendAsync(request, timeout.Token);

        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(timeout.Token);
            throw new SteamAuthException($"find-user failed: {(int)resp.StatusCode} {body}");
        }

        return await resp.Content.ReadFromJsonAsync<SteamUserResponse>(cancellationToken: timeout.Token)
               ?? throw new SteamAuthException("Empty response.");
    }

    /// <summary>
    ///    Asks the auth server whether a Discord token is still usable.
    /// </summary>
    public Task<TokenCheckOutcome> ValidateDiscordTokenAsync(string discordToken, CancellationToken cancel = default)
        => ValidateAsync("api/discord-auth/validate", discordToken, cancel);

    /// <summary>
    ///    Asks the auth server whether a Steam token is still usable.
    /// </summary>
    public Task<TokenCheckOutcome> ValidateSteamTokenAsync(string steamToken, CancellationToken cancel = default)
        => ValidateAsync("api/steam-auth/validate", steamToken, cancel);

    /// <summary>
    ///     Asks the auth server whether an access token is still usable.
    /// </summary>
    private async Task<TokenCheckOutcome> ValidateAsync(string path, string token, CancellationToken cancel)
    {
        try
        {
            using var timeout = Linked(cancel);

            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(ApiUrl, path));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var resp = await http.SendAsync(request, timeout.Token);

            if (resp.IsSuccessStatusCode)
                return TokenCheckOutcome.Valid;

            if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return TokenCheckOutcome.Invalid;

            Log.Warning("Unexpected status {Status} from {Path}; treating token as unverified", resp.StatusCode, path);
            return TokenCheckOutcome.Unavailable;
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            Log.Warning(e, "Could not reach {Path} to validate a token", path);
            return TokenCheckOutcome.Unavailable;
        }
    }

    /// <summary>
    ///     Asks the backend for the NullLink profile of the token's owner.
    ///     Returns <see cref="TokenCheckOutcome.Invalid"/> when the token was rejected.
    /// </summary>
    public async Task<(TokenCheckOutcome Outcome, NullLinkProfile? Profile)> GetProfileAsync(string token, CancellationToken cancel = default)
    {
        try
        {
            using var timeout = Linked(cancel);

            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(ApiUrl, "api/profile"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var resp = await http.SendAsync(request, timeout.Token);

            if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return (TokenCheckOutcome.Invalid, null);

            if (!resp.IsSuccessStatusCode)
            {
                Log.Warning("Profile request failed with {Status}", resp.StatusCode);
                return (TokenCheckOutcome.Unavailable, null);
            }

            var profile = await resp.Content.ReadFromJsonAsync<NullLinkProfile>(cancellationToken: timeout.Token);
            return profile is null ? (TokenCheckOutcome.Unavailable, null) : (TokenCheckOutcome.Valid, profile);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            Log.Warning(e, "Could not reach the backend to load a profile");
            return (TokenCheckOutcome.Unavailable, null);
        }
    }

    /// <summary>
    ///     Exchanges a refresh token for a new token pair.
    /// </summary>
    public async Task<TokenRefreshResult> RefreshTokenAsync(
        string sessionId, string refreshToken, CancellationToken cancel = default)
    {
        try
        {
            using var timeout = Linked(cancel);

            using var resp = await http.PostAsJsonAsync(
                new Uri(ApiUrl, "api/token/refresh"), new { sessionId, refreshToken }, timeout.Token);

            // Expired / revoked / reuse detected -> the user genuinely has to log in again.
            if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.BadRequest)
            {
                Log.Information("Refresh for session {Session} rejected with {Status}", sessionId, resp.StatusCode);
                return TokenRefreshResult.Invalid;
            }

            if (!resp.IsSuccessStatusCode)
            {
                // 5xx, rate limits, proxies... the session is probably fine, just try again later.
                Log.Warning("Refresh for session {Session} failed with {Status}", sessionId, resp.StatusCode);
                return TokenRefreshResult.Unavailable;
            }

            var tokens = await resp.Content.ReadFromJsonAsync<StarlightRefreshResult>(cancellationToken: timeout.Token);
            if (tokens is null || string.IsNullOrWhiteSpace(tokens.AccessToken))
            {
                Log.Warning("Refresh for session {Session} returned an empty body", sessionId);
                return TokenRefreshResult.Unavailable;
            }

            return new TokenRefreshResult(TokenCheckOutcome.Valid, tokens);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            Log.Warning(e, "Could not reach the auth server to refresh session {Session}", sessionId);
            return TokenRefreshResult.Unavailable;
        }
    }

    private static CancellationTokenSource Linked(CancellationToken cancel)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        cts.CancelAfter(_requestTimeout);
        return cts;
    }
}

public sealed record StarlightRefreshResult(
    string AccessToken, DateTime AccessExpiresUtc, string RefreshToken, string SessionId)
{
    /// <summary>
    ///     The access token expiry as an unambiguous instant.
    /// </summary>
    public DateTimeOffset AccessExpires => AccessExpiresUtc.Kind switch
    {
        DateTimeKind.Utc => new DateTimeOffset(AccessExpiresUtc),
        DateTimeKind.Local => new DateTimeOffset(AccessExpiresUtc).ToUniversalTime(),
        _ => new DateTimeOffset(DateTime.SpecifyKind(AccessExpiresUtc, DateTimeKind.Utc))
    };
}

/// <summary>
///    The user ID and username associated with a Discord token.
/// </summary>
public sealed record DiscordUserResponse(Guid UserId, string Username);

/// <summary>
///     The user ID and username associated with a Steam token.
/// </summary>
public sealed record SteamUserResponse(Guid UserId, string Username);
