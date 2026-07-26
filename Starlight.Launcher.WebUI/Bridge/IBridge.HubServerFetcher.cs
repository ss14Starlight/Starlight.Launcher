using Robust.Launcher.Api.Models.ServerStatus;
using Starlight.Launcher.WebUI.Models.HubServerFetcher;

namespace Starlight.Launcher.WebUI.Bridge;

public partial interface IBridge
{
    event Action? ServersChanged;
    event Action<RefreshListStatus>? StatusChanged;

   void UpdateInfoFor(ServerStatusData statusData);

    void RequestRefresh();

    IReadOnlyList<ServerStatusData> GetAllServers();
}
