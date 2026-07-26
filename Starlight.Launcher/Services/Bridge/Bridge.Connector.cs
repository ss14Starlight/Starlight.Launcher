using System.ComponentModel;
using Robust.Launcher.Api.Models;
using Starlight.Launcher.WebUI.Bridge;
using Starlight.Launcher.WebUI.Models.Connector;
using Starlight.Launcher.WebUI.Models.Helpers;

namespace Starlight.Launcher.Services.Bridge;

public sealed partial class Bridge : IBridge
{
    public event PropertyChangedEventHandler? ConnectionPropertyChanged
    {
        add => _connector.PropertyChanged += value;
        remove => _connector.PropertyChanged -= value;
    }

    public ConnectionStatus GetConnectionStatus() => _connector.Status;

    public ServerPrivacyPolicyInfo? GetPrivacyPolicyInfo() => _connector.PrivacyPolicyInfo;

    public bool GetPrivacyPolicyDifferentVersion() => _connector.PrivacyPolicyDifferentVersion;

    public bool IsClientExitedBadly() => _connector.ClientExitedBadly;

    public void LaunchContentBundle(IFileResult file, CancellationToken cancel = default) => _connector.LaunchContentBundle(file, cancel);

    public void Connect(string address, CancellationToken cancel = default) => _connector.Connect(address, cancel);

    public void ConfirmPrivacyPolicy(PrivacyPolicyAcceptResult result) => _connector.ConfirmPrivacyPolicy(result);
}
