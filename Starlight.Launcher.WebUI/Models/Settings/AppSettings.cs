using System.Text.Json.Serialization;
using Starlight.Launcher.WebUI.Models.Data;
using Starlight.Launcher.WebUI.Models.DiscordRichPresence;
using Starlight.Launcher.WebUI.Models.Logging;
using Starlight.Launcher.WebUI.Models.ServerStatus;

namespace Starlight.Launcher.WebUI.Models.Settings;

public partial record AppSettings
{
    #region Paths

    /// <summary>
    /// Base directory for all launcher data. This is currently the one "real" hardcoded path.
    /// </summary>
    public string DirLauncherData { get; init; } = GetDefaultDataDirectory();

    private static string GetDefaultDataDirectory()
    {
        var baseDir = OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Library", "Application Support")
            : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        return Path.Combine(baseDir, "Starlight.Launcher");
    }

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

    public string DirEngineInstallations => Path.Combine(DirLauncherData, "engines");
    public string DirModuleInstallations => Path.Combine(DirLauncherData, "modules");

    #endregion

    #region General

    /// <summary>
    /// Save interval in milliseconds
    /// </summary>
    public int SaveIntervalMs { get; init; } = 500;
    /// <summary>
    /// A list of hub urls to use for server lists
    /// </summary>
    public List<Hub> Hubs { get; init; } = [ new Hub() { HubUri = new Uri("https://hub.playss14.com/"), Priority = 0} ];
    /// <summary>
    /// List of server names and IPs which will be ignored in servers list.
    /// </summary>
    public List<IgnoredServer> IgnoredServers { get; init; } = [];
    /// <summary>
    /// Currently selected language. Should be a key from LocalizationsIndex. Default is "en-US"
    /// </summary>
    public string? SelectedLanguage { get; init; } = null;

    /// <summary>
    /// Prevents launch of multiple game instances
    /// </summary>
    public bool PreventMultipleClients { get; set; } = true;

    /// <summary>
    /// Last version for which the changelog popup was shown to the user.
    /// Empty means never shown.
    /// </summary>
    public string LastSeenChangelogVersion { get; set; } = "";
    #endregion

    #region Appearance
    /// <summary>
    /// App theme
    /// </summary>
    public AppTheme Theme { get; init; } = AppTheme.System;

    /// <summary>
    /// Determines should we place navigation menu at the bottom of app or at the left side
    /// </summary>
    public ElementPosition Navigation { get; init; } = ElementPosition.Bottom;

    /// <summary>
    /// Determines should we place search bar at the bottom of TOOLBAR or at the top
    /// </summary>
    public bool ServerListToolbarBottomSearch { get; init; }

    /// <summary>
    /// Determines should we place search bar at the bottom of APP or at the top
    /// </summary>
    public ElementPosition ServerListToolBarSearchPosition { get; init ; } = ElementPosition.Top;

    /// <summary>
    /// Determines should we place TAGS bar at the bottom of APP or at the top
    /// </summary>
    public ElementPosition ServerListToolBarBottomTagsPosition { get; init; } = ElementPosition.Left;
    /// <summary>
    /// Determines should we open TAGS bar by default or it should be closed by default
    /// </summary>
    public bool ServerListToolBarTagsBarOpen { get; init; } = true;

    /// <summary>
    /// Determines should we collapse app to tray on start or not
    /// </summary>
    public bool CollapseInTrayOnStart { get; init; }

    /// <summary>
    /// Determines should we collapse app to tray after launching game or not
    /// </summary>
    public bool CollapseInTrayAfterRun { get; init; }

    /// <summary>
    /// Determines should we uncollapse app from tray after game closing or not
    /// </summary>
    public bool UnCollapseFromTrayAfterEnd { get; init; }

    /// <summary>
    /// Determines should we collapse app to tray on close or not
    /// </summary>
    public bool CollapseInTrayOnClose { get; init; }

    /// <summary>
    /// Determines should we collapse app to tray on minimize or not
    /// </summary>
    public bool CollapseInTrayOnMinimize { get; init; }

    /// <summary>
    /// Determines which Discord Rich Presence will be used by default, should be a key from DiscordRichPresencesIndex.
    /// </summary>
    public string DiscordRichPresenceID { get; set; } = "1512750736927228005";

    /// <summary>
    /// Determines should we hide Discord Rich Presence or not. If true, presence won't be started.
    /// </summary>
    public bool HidePresence { get; set; }

    /// <summary>
    /// Determines should we show Discord Rich Presence buttons or not. If true, presence buttons won't be shown.
    /// </summary>
    public bool ShowPresenceButtons { get; set; } = true;

    public List<PresenceStateOption> PresenceStates { get; set; } = DiscordRichPresence.PresenceStates.CreateDefault();
    #endregion

    #region Cache
    public ServerListFilters CachedFilters { get; set; } = new ServerListFilters();
    #endregion

    #region Game / launch
    /// <summary>
    /// Force render compatibility mode (GLES2).
    /// </summary>
    public bool CompatMode { get; init; }

    /// <summary>
    /// Disable engine signature verification. For debugging/development only.
    /// </summary>
    public bool DisableSigning { get; init; }

    /// <summary>
    /// Enable local overriding of engine versions.
    /// </summary>
    /// <remarks>
    /// If enabled and on a development build,
    /// the launcher will pull all engine versions and modules from <see cref="EngineOverridePath"/>.
    /// This can be set to <c>RobustToolbox/release/</c> to instantly pull in packaged engine builds.
    /// </remarks>
    public bool EngineOverrideEnabled { get; init; }

    public string EngineOverridePath { get; init; } = "";

    /// <summary>
    /// How long to keep cached copies of Robust manifests (builds/modules) before redownloading. Set to zero or negative to disable caching entirely.
    /// </summary>

    public TimeSpan RobustManifestCacheTime = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Maximum amount of TOTAL versions to keep in the content database.
    /// </summary>
    public int MaxVersionsToKeep { get; init; } = 15;

    /// <summary>
    /// Maximum amount of versions to keep of a specific fork ID.
    /// </summary>
    public int MaxForkVersionsToKeep { get; init; } = 3;

    /// <summary>
    /// If a download gets interrupted, keep the files for a week.
    /// </summary>
    public int InterruptibleDownloadKeepHours = 7 * 24;

    #endregion

    #region Starlight API

    /// <summary>
    /// Basic Api URL used for auth and hub.
    /// </summary>
    public string StarlightAPIUrl { get; set; } = "https://starlight.network/";

    #endregion

    #region Privacy policies

    /// <summary>
    /// Determines if user accepted policy when entering development tab. 
    /// </summary>
    public bool DevPolicyAccepted { get; set; } = false;

    /// <summary>
    ///Privacy policies accepted by the user, the key is the policy identifier.
    /// </summary>
    public Dictionary<string, AcceptedPrivacyPolicy> AcceptedPrivacyPolicies { get; init; } = new();
    #endregion

    #region Auth
    /// <summary>
    /// Currently selected login.
    /// </summary>
    public Guid? SelectedLoginId { get; set; } = null;

    /// <summary>
    /// Auth servers in priority order. User-editable, any count.
    /// </summary>
    public List<string> AuthServerUrls { get; init; } = ["https://auth.playss14.com/"];

    /// <summary>
    /// Currently selected auth server.
    /// </summary>

    public string? SelectedAuthServer = "https://auth.playss14.com/";

    /// <summary>
    /// Fallback name which will be used if there's no logins.
    /// </summary>

    public const string FallbackUsername = "JoeGenero";

    /// <summary>
    /// Determines should we deauth launcher if auth server was changed.
    /// </summary>
    public bool DeauthOnChange = true;

    #endregion

    #region Logs

    /// <summary>
    /// Where client logs are written. If null, client logs will be written to AppSettings.DirClientLogsDefault.
    /// </summary>
    public string? ClientLogDirectory { get; set; }
    /// <summary>
    /// How to split client logs into multiple files. If set to "Single", the logs will be written to client.stdout.log and client.stderr.log. If set to "Date", the logs will be split by date, e.g. client-2024-06-01.stdout.log and client-2024-06-01.stderr.log. If set to "Launch", the logs will be split by launch, e.g. client-launch-1.stdout.log and client-launch-2.stderr.log.
    /// </summary>
    public ClientLogSplitMode ClientLogSplitMode { get; set; } = ClientLogSplitMode.Launch;
    /// <summary>
    /// If true, client stdout/stderr logs will be combined into a single file instead of split into two.
    /// </summary>
    public bool ClientLogCombineStreams { get; set; }
    /// <summary>
    /// How many client log files to retain. Older files will be deleted when this limit is exceeded.
    /// </summary>
    public int ClientLogRetainFiles { get; set; } = 20;
    /// <summary>
    /// Default directory for client logs if <see cref="ClientLogDirectory"/> is null.
    /// </summary>
    [JsonIgnore] // This is a computed property, not a setting.
    public string DirClientLogsDefault => Path.Combine(DirLauncherData, "client-logs");
    #endregion

    #region Cdns

    /// <summary>
    /// Path to loader signing key, i.e. where we need to "unpack" signing key from launcher.
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
    /// User's configured CDN list. If empty, <see cref="DefaultRobustCdns"/> will be used.
    /// </summary>
    public List<RobustCdnConfig>? RobustCdns { get; set; } = null;

    /// <summary>
    /// Internal default CDN list. Used if <see cref="RobustCdns"/> is empty.
    /// </summary>
    public static List<RobustCdnConfig> DefaultRobustCdns { get; } =
    [
        new() { Name = "Starlight", Urls = ["https://robust-builds.starlight.network/"], PublicKey = PrimaryCdnPublicKey, Important=true },
        new() { Name = "PlaySS14",  Urls = ["https://robust-builds.playss14.com/"],      PublicKey = SecondaryCdnPublicKey },
    ];

    /// <summary>
    /// Determines should we allow user to change cdn's public/verify key.
    /// </summary>
    public bool AllowCdnsKeyChange { get; set; }

    #endregion

}

public struct IgnoredServer(string name, string address)
{
    public string Name { get; set; } = name;
    public string Address { get; set; } = address;
}
