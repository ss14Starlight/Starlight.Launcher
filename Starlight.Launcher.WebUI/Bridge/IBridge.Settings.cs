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

    AppSettings GetSettings();

    Task<AppSettings> GetSettingsAsync();

    List<FavoriteServer> GetFavorites();

    Task<List<FavoriteServer>> GetFavoritesAsync();

    Dictionary<Guid, LoginInfo> GetLogins();

    Task<Dictionary<Guid, LoginInfo>> GetLoginsAsync();

    void WriteSettings(AppSettings settings);

    Task WriteSettingsAsync(AppSettings settings);

    Task WriteFavoritesAsync(List<FavoriteServer> favorites);

    void WriteLogins(Dictionary<Guid, LoginInfo> logins);

    Task CacheFilters(ServerListFilters filters);

    /// <summary>
    /// Whether the launcher can create and write files in the folder (without admin rights).
    /// </summary>
    Task<bool> IsDirectoryWritableAsync(string path);
}
