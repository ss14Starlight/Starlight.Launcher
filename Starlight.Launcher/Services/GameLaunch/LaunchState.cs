using Robust.Launcher.Api.Models;
using Serilog;
using Starlight.Launcher.WebUI.Models.Connector;
using Starlight.Launcher.WebUI.Models.Helpers;

namespace Starlight.Launcher.Services.GameLaunch;

public sealed class LaunchState : ObservableObject, ILaunchState
{
    private TaskCompletionSource<PrivacyPolicyAcceptResult>? _acceptPrivacyPolicyTcs;

    public ConnectionStatus Status
    {
        get;
        internal set => SetField(ref field, value);
    } = ConnectionStatus.None;

    public bool ClientExitedBadly
    {
        get;
        internal set => SetField(ref field, value);
    }

    public ServerPrivacyPolicyInfo? PrivacyPolicyInfo { get; private set; }

    public bool PrivacyPolicyDifferentVersion
    {
        get;
        internal set => SetField(ref field, value);
    }

    internal (string Address, string? Name)? Target { get; set; }

    internal Task<PrivacyPolicyAcceptResult> WaitForPrivacyPolicyAsync(ServerPrivacyPolicyInfo info, CancellationToken cancel)
    {
        PrivacyPolicyInfo = info;
        _acceptPrivacyPolicyTcs = new TaskCompletionSource<PrivacyPolicyAcceptResult>();

        Status = ConnectionStatus.AwaitingPrivacyPolicyAcceptance;
        return _acceptPrivacyPolicyTcs.Task.WaitAsync(cancel);
    }

    public void ConfirmPrivacyPolicy(PrivacyPolicyAcceptResult result)
    {
        if (_acceptPrivacyPolicyTcs == null)
        {
            Log.Error("_acceptPrivacyPolicyTcs is null???");
            return;
        }

        _ = _acceptPrivacyPolicyTcs.TrySetResult(result);
    }

    internal void Cleanup()
    {
        Target = null;
        PrivacyPolicyInfo = null;
        _acceptPrivacyPolicyTcs = null;
        PrivacyPolicyDifferentVersion = default;
    }
}
