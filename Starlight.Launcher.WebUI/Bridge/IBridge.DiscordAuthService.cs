using Starlight.Launcher.WebUI.Models.Auth;

namespace Starlight.Launcher.WebUI.Bridge;

public partial interface IBridge
{
    Task<LoggedInAccount> LoginAsync(CancellationToken cancel = default);

    Task AttachToAccountAsync(LoggedInAccount account, CancellationToken cancel = default);
}
