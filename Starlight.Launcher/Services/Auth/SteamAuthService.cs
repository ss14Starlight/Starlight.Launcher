using Starlight.Launcher.WebUI.Models.StarlightAuthService;

namespace Starlight.Launcher.Services.Auth;

public sealed class SteamAuthService(StarlightAuthApi api, LoginManager loginManager)
    : StarlightOAuthService(api, loginManager)
{
    protected override bool IsSteam => true;

    protected override string ProviderSlug => "steam";

    protected override string DisplayName => "Steam";

    protected override Exception Error(string message) => new SteamAuthException(message);

    protected override async Task<(Guid UserId, string Username)> GetUserAsync(string token, CancellationToken cancel)
    {
        var info = await Api.GetSteamUserAsync(token, cancel)
                   ?? throw new SteamAuthException("Failed to retrieve user information.");

        return (info.UserId, info.Username);
    }
}
