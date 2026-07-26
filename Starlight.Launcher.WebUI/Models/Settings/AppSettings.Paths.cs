using Robust.Launcher.Api.Utility;
using Starlight.Launcher.WebUI.Models.Data;
using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Starlight.Launcher.WebUI.Models.Settings;

public partial record AppSettings
{
    #region Paths

    /// <summary>
    /// Base directory for all launcher data. This is currently the one "real" hardcoded path.
    /// </summary>
    public string DirLauncherData { get; init; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Starlight.Launcher");

    /// <summary>
    /// Where the launcher itself is installed. Used to locate the loader/engine (release builds).
    /// </summary>
    [JsonIgnore]
    public string DirLauncherInstall { get; init; } = AppContext.BaseDirectory;

    /// <summary>
    /// SQLite content DB the loader reads versions/blobs from.
    /// <remark>
    /// IMPORTANT: this MUST point at the same file that ContentManager.GetSqliteConnection() uses,
    /// otherwise the loader won't find the version the Updater just wrote.
    /// </remark>
    /// </summary>
    public string PathContentDb => Path.Combine(DirLauncherData, "content.db");

    /// <summary>
    /// Client log outputs.
    /// </summary>
    public string PathClientMacLog => Path.Combine(DirLauncherData, "client.mac.log");
    public string PathClientStdoutLog => Path.Combine(DirLauncherData, "client.stdout.log");
    public string PathClientStderrLog => Path.Combine(DirLauncherData, "client.stderr.log");
    public string DirEngineInstallations => Path.Combine(DirLauncherData, "engines");
    public string DirModuleInstallations => Path.Combine(DirLauncherData, "modules");

    /// <summary>
    /// Path to loader signing key, i.e. where we need to "unpack" signing key from launcher.
    /// TODO: Configurable cdns and their keys
    /// </summary>
    public string PathLoaderSigningKey => Path.Combine(DirLauncherData, "loader_signing_key");

    private const string PrimaryCdnPublicKey = """
        -----BEGIN PUBLIC KEY-----
        MCowBQYDK2VwAyEAvF9h6FVrVhh9cYoSk0g/XluUVIrg40PQy8VPNaGu1vQ=
        -----END PUBLIC KEY-----
        """;

    private const string SecondaryCdnPublicKey = """
        -----BEGIN PUBLIC KEY-----
        MCowBQYDK2VwAyEApQ9mAhMLbmhQqRH7itgNo75S5rCSMsMXvVRmMv1d9NQ=
        -----END PUBLIC KEY-----
        """;

    /// <summary>
    /// Robust build CDNs in priority order. Each entry is one CDN; a CDN may list multiple
    /// mirror URLs treated as interchangeable for availability fallback.
    /// </summary>
    // TODO: Redone to configurable option.
    public static ImmutableArray<RobustCdn> RobustCdns { get; } = [
        // Primary CDN — checked first.
        new RobustCdn("https://robust-builds.starlight.network/") { PublicKey=PrimaryCdnPublicKey },

        // Secondary CDN with its own availability fallback (mirror).
        // Checked only if the requested version is missing from the primary CDN's manifest.
        new RobustCdn(
            "https://robust-builds.playss14.com/") { PublicKey = SecondaryCdnPublicKey },
    ];

    #endregion
}
