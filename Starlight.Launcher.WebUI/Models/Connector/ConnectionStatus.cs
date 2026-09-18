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
    NotAContentBundle,
    /// <summary>
    /// The launcher couldn't write to the data folder (e.g. it requires admin rights).
    /// </summary>
    AccessDenied
}
