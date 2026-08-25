namespace Starlight.Launcher.WebUI.Models.LocalServer;

public enum LocalServerPhase
{
    Idle,
    FetchingManifest,
    Downloading,
    Extracting,
    Starting,
    Running,
    Stopping,
    Stopped,
    Error
}

public sealed record LocalServerState(
    LocalServerPhase Phase,
    string? SourceName = null,
    string? BuildHash = null,
    DateTimeOffset? BuildTime = null,
    long? DownloadedBytes = null,
    long? TotalBytes = null,
    string? ErrorMessage = null
);
