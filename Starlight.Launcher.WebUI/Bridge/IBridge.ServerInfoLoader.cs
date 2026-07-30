using Robust.Launcher.Api.Models.ServerStatus;

namespace Starlight.Launcher.WebUI.Bridge;

public partial interface IBridge
{
    void Request(ServerStatusData? data);
}
