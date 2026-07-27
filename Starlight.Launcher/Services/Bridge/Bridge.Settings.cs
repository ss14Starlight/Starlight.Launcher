using Robust.Launcher.Api.Models.Data;
using Starlight.Launcher.WebUI.Bridge;
using Starlight.Launcher.WebUI.Models.Data;
using Starlight.Launcher.WebUI.Models.ServerStatus;
using Starlight.Launcher.WebUI.Models.Settings;

namespace Starlight.Launcher.Services.Bridge;

public sealed partial class Bridge : IBridge
{
    public event Action? FavoritesChanged
    {
        add => _settings.FavoritesChanged += value;
        remove => _settings.FavoritesChanged -= value;
    }

    public event Action? LoginsUnrecoverable
    {
        add => _settings.LoginsUnrecoverable += value;
        remove => _settings.LoginsUnrecoverable -= value;
    }

    public IReadOnlySet<string> GetFavoriteAddressesSnapshot() => _settings.GetFavoriteAddressesSnapshot();

    public AppSettings GetSettings() => _settings.GetSettings();

    public async Task<AppSettings> GetSettingsAsync() => await _settings.GetSettingsAsync();

    public List<FavoriteServer> GetFavorites() => _settings.GetFavorites();

    public async Task<List<FavoriteServer>> GetFavoritesAsync() => await _settings.GetFavoritesAsync();

    public Dictionary<Guid, LoginInfo> GetLogins() => _settings.GetLogins();

    public async Task<Dictionary<Guid, LoginInfo>> GetLoginsAsync() => await _settings.GetLoginsAsync();

    public void WriteSettings(AppSettings settings) => _settings.WriteSettings(settings);

    public async Task WriteSettingsAsync(AppSettings settings) => await _settings.WriteSettingsAsync(settings);

    public async Task WriteFavoritesAsync(List <FavoriteServer> favorites) => await _settings.WriteFavoritesAsync(favorites);

    public void WriteLogins(Dictionary<Guid, LoginInfo> logins) => _settings.WriteLogins(logins);

    public async Task CacheFilters(ServerListFilters filters) => await _settings.CacheFilters(filters);

}
