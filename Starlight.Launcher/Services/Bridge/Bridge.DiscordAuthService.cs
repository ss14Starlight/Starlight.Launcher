
using Starlight.Launcher.WebUI.Bridge;
using Starlight.Launcher.WebUI.Models.Auth;

namespace Starlight.Launcher.Services.Bridge;

public sealed partial class Bridge : IBridge
{
    public async Task<LoggedInAccount> LoginAsync(bool steam, CancellationToken cancel = default)
    {
        if (!steam)
            return await _discordAuth.LoginAsync(cancel);
        else
            return await _steamAuth.LoginAsync(cancel);

        return default;
    }

    public async Task AttachToAccountAsync(LoggedInAccount account, CancellationToken cancel = default) => await _discordAuth.AttachToAccountAsync(account, cancel);
}
