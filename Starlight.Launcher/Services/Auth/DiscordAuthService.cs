using Starlight.Launcher.WebUI.Models.DiscordAuthService;

namespace Starlight.Launcher.Services.Auth;

public sealed class DiscordAuthService(StarlightAuthApi api, LoginManager loginManager)
    : StarlightOAuthService(api, loginManager)
{
    protected override bool IsSteam => false;

    protected override string ProviderSlug => "discord";

    protected override string DisplayName => "Discord";

    protected override Exception Error(string message) => new DiscordAuthException(message);

    protected override async Task<(Guid UserId, string Username)> GetUserAsync(string token, CancellationToken cancel)
    {
        var info = await Api.GetDiscordUserAsync(token, cancel)
                   ?? throw new DiscordAuthException("Failed to retrieve user information.");

        return (info.UserId, info.Username);
    }
}
