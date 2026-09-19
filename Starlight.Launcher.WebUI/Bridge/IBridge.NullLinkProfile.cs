using Starlight.Launcher.WebUI.Models.Auth;
using Starlight.Launcher.WebUI.Models.NullLink;

namespace Starlight.Launcher.WebUI.Bridge;

public partial interface IBridge
{
    Task<NullLinkProfileResult> GetNullLinkProfileAsync(LoggedInAccount account, CancellationToken cancel = default);
}
