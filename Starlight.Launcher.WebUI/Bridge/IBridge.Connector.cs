using Starlight.Launcher.WebUI.Models.Connector;
using Starlight.Launcher.WebUI.Models.Helpers;

namespace Starlight.Launcher.WebUI.Bridge;

/// <summary>
/// Bridged parts from Connector.cs
/// </summary>
public partial interface IBridge
{
    ILaunchState LaunchContentBundle(IFileResult file, CancellationToken cancel = default);

    ILaunchState Connect(string address, string? displayName, CancellationToken cancel = default);
}
