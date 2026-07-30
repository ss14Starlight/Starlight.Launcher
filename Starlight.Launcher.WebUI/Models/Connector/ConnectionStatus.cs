namespace Starlight.Launcher.WebUI.Models.Connector;

public enum ConnectionStatus
{
    None,
    Updating,
    UpdateError,
    Connecting,
    AwaitingPrivacyPolicyAcceptance,
    ConnectionFailed,
    StartingClient,
    ClientRunning,
    ClientExited,
    Cancelled,
    NotAContentBundle
}
