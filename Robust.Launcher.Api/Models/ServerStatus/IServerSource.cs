namespace Robust.Launcher.Api.Models.ServerStatus;

/// <summary>
/// Where we get server status and info data from.
/// </summary>
public interface IServerSource
{
    void UpdateInfoFor(ServerStatusData statusData);
}
