namespace Starlight.Launcher.WebUI.Models.Settings;

public sealed record RobustCdnConfig
{
    /// <summary>
    /// Public name of the CDN, used for display in the UI.
    /// </summary>
    public string Name { get; init; } = "";

    /// <summary>
    /// URLs of the CDN endpoints. The launcher will try them in order until one succeeds.
    /// </summary>
    public string[] Urls { get; init; } = [];

    /// <summary>
    /// Public key used to verify the integrity of the content downloaded from this CDN.
    /// </summary>
    public string PublicKey { get; init; } = "";

    /// <summary>
    /// Determines if this CDN is enabled. If false, the launcher will not use this CDN.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Determines if this CDN is important for launcher futures like discord oauth and other features.
    /// If true, the launcher will show a warning when you trying to move priority away from this CDN, disable or remove it.
    /// </summary>
    public bool Important { get; init; } = false;
}
