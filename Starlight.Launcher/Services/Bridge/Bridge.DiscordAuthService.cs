
using Starlight.Launcher.WebUI.Bridge;
using Starlight.Launcher.WebUI.Models.Auth;

namespace Starlight.Launcher.Services.Bridge;

public sealed partial class Bridge : IBridge
{
    public async Task<LoggedInAccount> LoginAsync(bool steam, CancellationToken cancel = default)
        => !steam ? await _discordAuth.LoginAsync(cancel) : await _steamAuth.LoginAsync(cancel);

    public async Task AttachToAccountAsync(LoggedInAccount account, CancellationToken cancel = default) => await _discordAuth.AttachToAccountAsync(account, cancel);
}
