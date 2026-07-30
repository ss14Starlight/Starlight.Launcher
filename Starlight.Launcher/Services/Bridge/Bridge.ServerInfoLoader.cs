using Robust.Launcher.Api.Models.ServerStatus;
using Starlight.Launcher.WebUI.Bridge;

namespace Starlight.Launcher.Services.Bridge;

public sealed partial class Bridge : IBridge
{
    public void Request(ServerStatusData? data) => _serverInfoLoader.Request(data);
}
