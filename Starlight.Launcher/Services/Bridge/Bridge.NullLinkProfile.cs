using Starlight.Launcher.Services.Auth;
using Starlight.Launcher.WebUI.Bridge;
using Starlight.Launcher.WebUI.Models.Auth;
using Starlight.Launcher.WebUI.Models.NullLink;

namespace Starlight.Launcher.Services.Bridge;

public sealed partial class Bridge : IBridge
{
    public async Task<NullLinkProfileResult> GetNullLinkProfileAsync(LoggedInAccount account, CancellationToken cancel = default)
    {
        if (ProfileToken(account) is null)
            return new(NullLinkProfileStatus.NotLinked);

        for (var attempt = 0; ; attempt++)
        {
            if (ProfileToken(account) is not { } token)
                return new(NullLinkProfileStatus.Unauthorized);

            var (outcome, profile) = await _starlightAuth.GetProfileAsync(token, cancel);
            switch (outcome)
            {
                case TokenCheckOutcome.Valid:
                    return new(NullLinkProfileStatus.Loaded, profile);
                case TokenCheckOutcome.Invalid when attempt == 0:
                    _ = await _loginManager.EnsureFreshAsync(account, cancel);
                    continue;
                case TokenCheckOutcome.Invalid:
                    return new(NullLinkProfileStatus.Unauthorized);
                default:
                    return new(NullLinkProfileStatus.Unavailable);
            }
        }
    }

    private static string? ProfileToken(LoggedInAccount account)
        => (account.LoginInfo.DiscordToken ?? account.LoginInfo.SteamToken)?.Token;
}
