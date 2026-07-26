
using Robust.Launcher.Api.Models.ServerStatus;
using Starlight.Launcher.WebUI.Bridge;
using Starlight.Launcher.WebUI.Models.HubServerFetcher;

namespace Starlight.Launcher.Services.Bridge;

public sealed partial class Bridge : IBridge
{
    public event Action? ServersChanged
    {
        add => _hubServerFetcher.ServersChanged += value;
        remove => _hubServerFetcher.ServersChanged -= value;
    }

    public event Action<RefreshListStatus>? StatusChanged
    {
        add => _hubServerFetcher.StatusChanged += value;
        remove => _hubServerFetcher.StatusChanged -= value;
    }

    public RefreshListStatus GetFetchStatus() => _hubServerFetcher.Status;

    public void UpdateInfoFor(ServerStatusData statusData) => ((IServerSource)_hubServerFetcher).UpdateInfoFor(statusData);

    public void RequestRefresh() => _hubServerFetcher.RequestRefresh();

    public IReadOnlyList<ServerStatusData> GetAllServers() => _hubServerFetcher.AllServers;
}
