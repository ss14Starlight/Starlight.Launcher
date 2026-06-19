using Starlight.Launcher.Services.Settings;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Starlight.Launcher.Services.Auth;

public sealed class StarlightAuthApi(HttpClient http, SettingsService settings)
{
    public readonly Uri apiUrl = new(settings.GetSettings().StarlightAPIUrl);

    public string BuildLauncherLoginUrl(string state)
        => new Uri(apiUrl, $"api/discord-auth/launcher-login?state={Uri.EscapeDataString(state)}").ToString();

    public async Task<DiscordUserResponse> GetDiscordUserAsync(
        string discordToken,
        CancellationToken cancel)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(apiUrl, $"api/discord-auth/find-user"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", discordToken);

        var resp = await http.SendAsync(request, cancel);

        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(cancel);
            throw new DiscordAuthException($"find-user failed: {(int)resp.StatusCode} {body}");
        }

        return await resp.Content.ReadFromJsonAsync<DiscordUserResponse>(
                   cancellationToken: cancel)
               ?? throw new DiscordAuthException("Empty response.");
    }

    public async Task<bool> ValidateDiscordToken(string discordToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(apiUrl, "api/discord-auth/validate"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", discordToken);

        var resp = await http.SendAsync(request);

        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync();
            throw new DiscordAuthException($"validate failed: {(int)resp.StatusCode} {body}");
        }

        return resp.IsSuccessStatusCode;
    }

    public async Task<StarlightRefreshResult?> RefreshTokenAsync(
        string sessionId, string refreshToken, CancellationToken cancel = default)
    {
        using var resp = await http.PostAsJsonAsync(new Uri(apiUrl, "api/token/refresh"), new { sessionId, refreshToken }, cancel);

        if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest)
            return null; // expired / revoked / reuse_detected -> re-login

        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<StarlightRefreshResult>(cancellationToken: cancel);
    }
}

public sealed record StarlightRefreshResult(
    string AccessToken, DateTime AccessExpiresUtc, string RefreshToken, string SessionId);

public sealed record DiscordUserResponse(Guid UserId, string Username);
