namespace Starlight.Launcher.WebUI.Models.LocalServer;

/// <summary>
/// Result of resolving the latest build for a manifest against the current platform.
/// </summary>
public sealed record LocalServerLatestBuildResult(
    string? BuildHash,
    DateTimeOffset? BuildTime,
    string? ResolvedRid,
    long? DownloadSize,
    bool Supported,
    string? ErrorMessage
);
