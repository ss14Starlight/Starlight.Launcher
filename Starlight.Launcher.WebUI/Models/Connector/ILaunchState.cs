using System.ComponentModel;
using Robust.Launcher.Api.Models;

namespace Starlight.Launcher.WebUI.Models.Connector;

public interface ILaunchState : INotifyPropertyChanged
{
    ConnectionStatus Status { get; }

    ServerPrivacyPolicyInfo? PrivacyPolicyInfo { get; }

    bool PrivacyPolicyDifferentVersion { get; }

    bool ClientExitedBadly { get; }

    void ConfirmPrivacyPolicy(PrivacyPolicyAcceptResult result);
}
