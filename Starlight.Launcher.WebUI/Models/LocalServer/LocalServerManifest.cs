namespace Starlight.Launcher.WebUI.Models.LocalServer;

/// <summary>
/// Shape of the build manifest a local-server source points at: a flat map of
/// build hash -> build info, each build offering a client zip and per-platform server zips.
/// </summary>
public sealed record LocalServerManifest(
    Dictionary<string, LocalServerBuildInfo> Builds
);

public sealed record LocalServerBuildInfo(
    DateTimeOffset Time,
    LocalServerAssetInfo? Client,
    Dictionary<string, LocalServerAssetInfo> Server
);

public sealed record LocalServerAssetInfo(
    string Url,
    string Sha256,
    long? Size = null
);
