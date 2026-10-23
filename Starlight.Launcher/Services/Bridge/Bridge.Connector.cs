using Starlight.Launcher.WebUI.Bridge;
using Starlight.Launcher.WebUI.Models.Connector;
using Starlight.Launcher.WebUI.Models.Helpers;

namespace Starlight.Launcher.Services.Bridge;

public sealed partial class Bridge : IBridge
{
    public ILaunchState LaunchContentBundle(IFileResult file, CancellationToken cancel = default) => _connector.LaunchContentBundle(file, cancel);

    public ILaunchState Connect(string address, string? displayName, CancellationToken cancel = default) => _connector.Connect(address, displayName, cancel);
}
