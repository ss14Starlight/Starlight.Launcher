
using Starlight.Launcher.WebUI.Bridge;
using Starlight.Launcher.WebUI.Models.Auth;

namespace Starlight.Launcher.Services.Bridge;

public sealed partial class Bridge : IBridge
{
    public async Task<LoggedInAccount> LoginAsync(bool steam, CancellationToken cancel = default)
        => !steam ? await _discordAuth.LoginAsync(cancel) : await _steamAuth.LoginAsync(cancel);

    public async Task AttachToAccountAsync(bool steam, LoggedInAccount account, CancellationToken cancel = default)
    {
        if (!steam)
            await _discordAuth.AttachToAccountAsync(account, cancel);
        else
            await _steamAuth.AttachToAccountAsync(account, cancel);
    }
}
