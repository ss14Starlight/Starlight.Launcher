using Starlight.Launcher.WebUI.Models.Auth;

namespace Starlight.Launcher.WebUI.Bridge;

public partial interface IBridge
{
    LoggedInAccount? GetActiveAccount();
}
