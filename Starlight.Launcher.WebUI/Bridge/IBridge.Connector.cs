using System.ComponentModel;
using Robust.Launcher.Api.Models;
using Starlight.Launcher.WebUI.Models.Connector;
using Starlight.Launcher.WebUI.Models.Helpers;

namespace Starlight.Launcher.WebUI.Bridge;

/// <summary>
/// Bridged parts from Connector.cs
/// </summary>
public partial interface IBridge
{
    ConnectionStatus GetConnectionStatus();

    ServerPrivacyPolicyInfo? GetPrivacyPolicyInfo();

    bool GetPrivacyPolicyDifferentVersion();

    bool IsClientExitedBadly();

    void LaunchContentBundle(IFileResult file, CancellationToken cancel = default);

    void Connect(string address, CancellationToken cancel = default);

    void ConfirmPrivacyPolicy(PrivacyPolicyAcceptResult result);

    event PropertyChangedEventHandler? ConnectionPropertyChanged;
}
