using Robust.Launcher.Api.Models.Data;
using Starlight.Launcher.WebUI.Models.Data;
using Starlight.Launcher.WebUI.Models.ServerStatus;
using Starlight.Launcher.WebUI.Models.Settings;

namespace Starlight.Launcher.WebUI.Bridge;

public partial interface IBridge
{
    event Action? FavoritesChanged;

    event Action? LoginsUnrecoverable;

    IReadOnlySet<string> GetFavoriteAddressesSnapshot();

    IReadOnlyList<string> GetUnreadableSettingsFiles();

    AppSettings GetSettings();

    Task<AppSettings> GetSettingsAsync();

    List<FavoriteServer> GetFavorites();

    Task<List<FavoriteServer>> GetFavoritesAsync();

    Dictionary<Guid, LoginInfo> GetLogins();

    Task<Dictionary<Guid, LoginInfo>> GetLoginsAsync();

    void WriteSettings(AppSettings settings);

    Task WriteSettingsAsync(AppSettings settings);

    void UpdateSettings(Func<AppSettings, AppSettings> update);

    Task WriteFavoritesAsync(List<FavoriteServer> favorites);

    void WriteLogins(Dictionary<Guid, LoginInfo> logins);

    Task CacheFilters(ServerListFilters filters);

    /// <summary>
    /// Settings whose default changed in this version while the user had their own value.
    /// </summary>
    IReadOnlyList<DefaultSettingChange> GetPendingDefaultChanges();

    /// <summary>
    /// Replaces the user's values of these settings with the new defaults.
    /// </summary>
    void AcceptDefaultChanges(IReadOnlyCollection<string> keys);

    /// <summary>
    /// Keeps the user's values of these settings, they won't be asked about these defaults again.
    /// </summary>
    void KeepCurrentSettings(IReadOnlyCollection<string> keys);

    /// <summary>
    /// Whether the launcher can create and write files in the folder (without admin rights).
    /// </summary>
    Task<bool> IsDirectoryWritableAsync(string path);
}
