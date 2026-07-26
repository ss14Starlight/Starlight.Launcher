using Starlight.Launcher.WebUI.Models.Data;
using Starlight.Launcher.WebUI.Models.Settings;

namespace Starlight.Launcher.WebUI.Bridge;

public partial interface IBridge
{
    event Action? FavoritesChanged;

    Task<AppSettings> GetSettingsAsync();

    List<FavoriteServer> GetFavorites();

    Task<List<FavoriteServer>> GetFavoritesAsync();

    Task WriteFavoritesAsync(List<FavoriteServer> favorites);
}
