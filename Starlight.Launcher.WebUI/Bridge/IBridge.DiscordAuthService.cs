using Starlight.Launcher.WebUI.Models.Auth;

namespace Starlight.Launcher.WebUI.Bridge;

public partial interface IBridge
{
    Task<LoggedInAccount> LoginAsync(bool steam, CancellationToken cancel = default);

    Task AttachToAccountAsync(bool steam, LoggedInAccount account, CancellationToken cancel = default);
}
