using Microsoft.AspNetCore.Components;
using Robust.Launcher.Api.Models.ServerStatus;
using Starlight.Launcher.Models.Data;
using Starlight.Launcher.Models.ServerStatus;
using Starlight.Launcher.Services.Localization;
using Starlight.Launcher.Services.ServerStatus;
using Starlight.Launcher.Services.Settings;
using System.Collections.Concurrent;
using System.Globalization;

namespace Starlight.Launcher.Components.Pages;

public partial class Servers : ComponentBase, IDisposable
{
    private const int ServerRefreshThrottleMs = 200;
    private const int FilterDebounceMs = 150;
    private const int FilterPersistDelayMs = 500;

    [Inject] private ServerStatusCache _cache { get; set; } = default!;
    [Inject] private SettingsService _settings { get; set; } = default!;
    [Inject] private HubServerFetcher _fetcher { get; set; } = default!;
    [Inject] private LocalizationManager _localization { get; set; } = default!;

    private ServerListFilters _filters = new();
    private readonly CancellationTokenSource _disposeCts = new();

    private IReadOnlyList<ServerStatusData> _allServers = [];
    private List<ServerStatusData> _filteredServers = [];
    private IReadOnlyList<string> _availableRPTags = [];
    private IReadOnlyList<string> _availableLangTags = [];
    private IReadOnlyList<string> _availableRegionTags = [];
    private int _totalCount;

    private CancellationTokenSource? _filterCts;
    private int _rebuildScheduled;

    private IReadOnlySet<string> _favoriteAddresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private bool _bottomSearch { get; set; }
    private ElementPosition _searchBarPosition { get; set; }
    private ElementPosition _tagsBarPosition { get; set; }

    protected override async Task OnInitializedAsync()
    {
        var settings = await _settings.GetSettingsAsync();
        _bottomSearch = settings.ServerListToolbarBottomSearch;
        _searchBarPosition = settings.ServerListToolBarSearchPosition;
        _tagsBarPosition = settings.ServerListToolBarBottomTagsPosition;

        _filters = settings.CachedFilters;
        _filters.TagsExpanded = settings.ServerListToolBarTagsBarOpen;
        _filters.Changed += OnFiltersChanged;

        _favoriteAddresses = _settings.GetFavoriteAddressesSnapshot();
        _settings.FavoritesChanged += OnFavoritesChanged;

        _fetcher.ServersChanged += OnServersChanged;
        _fetcher.StatusChanged += OnStatusChanged;

        RebuildFromFetcher();
    }

    private async void OnServersChanged()
    {
        if (Interlocked.CompareExchange(ref _rebuildScheduled, 1, 0) != 0)
            return;

        try
        {
            await Task.Delay(ServerRefreshThrottleMs, _disposeCts.Token);
            await InvokeAsync(() =>
            {
                Interlocked.Exchange(ref _rebuildScheduled, 0);
                RebuildFromFetcher();
                StateHasChanged();
            });
        }
        catch (OperationCanceledException) { Interlocked.Exchange(ref _rebuildScheduled, 0); }
        catch (ObjectDisposedException) { Interlocked.Exchange(ref _rebuildScheduled, 0); }
    }

    private async void OnStatusChanged(RefreshListStatus _)
    {
        try { await InvokeAsync(StateHasChanged); }
        catch (ObjectDisposedException) { }
    }

    private async void OnFavoritesChanged()
    {
        try
        {
            await InvokeAsync(() =>
            {
                _favoriteAddresses = _settings.GetFavoriteAddressesSnapshot();
                StateHasChanged();
            });
        }
        catch (ObjectDisposedException) { }
    }

    private void OnFiltersChanged()
    {
        _filterCts?.Cancel();
        _filterCts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token);
        _ = DebounceFiltersAsync(_filterCts.Token);
    }

    private async Task DebounceFiltersAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(FilterDebounceMs, token);
            await InvokeAsync(() =>
            {
                ApplyFilters();
                StateHasChanged();
            });

            await Task.Delay(FilterPersistDelayMs, token);
            await _settings.CacheFilters(_filters);
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    private void RebuildFromFetcher()
    {
        _allServers = _fetcher.AllServers;
        _totalCount = _allServers.Count;
        ExtractTags(_allServers, out _availableRPTags, out _availableLangTags, out _availableRegionTags);
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        IEnumerable<ServerStatusData> query = _allServers;

        if (!string.IsNullOrWhiteSpace(_filters.SearchQuery))
        {
            var q = _filters.SearchQuery.Trim();
            query = query.Where(s =>
                (s.Name?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                s.Address.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        if (_filters.SelectedRP.Count > 0)
        {
            query = query.Where(s =>
            {
                var rpTag = ParseRPTag(GetTags(s).FirstOrDefault(t => t.StartsWith("rp")) ?? "");
                return _filters.SelectedRP.Contains(rpTag);
            });
        }

        if (_filters.SelectedRegion.Count > 0)
            query = query.Where(s => GetRegion(s) is { } r && _filters.SelectedRegion.Contains(ParseRegionTag(r)));

        if (_filters.SelectedLang.Count > 0)
            query = query.Where(s => GetLanguage(s) is { } l && _filters.SelectedLang.Contains(ParseLangTag(l)));

        if (_filters.HideAdult)
            query = query.Where(s => !IsAdult(s));
        else if (_filters.OnlyAdult)
            query = query.Where(IsAdult);

        if (_filters.HideEmpty)
            query = query.Where(s => GetPlayers(s) > 0);
        if (_filters.HideFull)
            query = query.Where(s => GetPlayers(s) < GetMaxPlayers(s));

        query = _filters.SortBy switch
        {
            ServerSortMode.Players => query.OrderByDescending(GetPlayers),
            ServerSortMode.Name => query.OrderBy(s => s.Name ?? s.Address, StringComparer.OrdinalIgnoreCase),
            ServerSortMode.Ping => query.OrderBy(GetPing),
            _ => query,
        };

        _filteredServers = [.. query];
    }

    private void HandleRefresh() => _fetcher.RequestRefresh();

    private void ClearFilters()
    {
        _filters.SearchQuery = "";
        _filters.SelectedRP.Clear();
        _filters.SelectedLang.Clear();
        _filters.SelectedRegion.Clear();
        _filters.HideEmpty = false;
        _filters.HideFull = false;
        ApplyFilters();
    }

    private async Task HandleFavorite(ServerStatusData server)
    {
        var favorites = _settings.GetFavorites();
        var alreadyExist = favorites.FirstOrDefault(x => x.Address == server.Address);

        if ((alreadyExist == null || alreadyExist == default) && server.HubAddress != null)
        {
            favorites.Add(new FavoriteServer(server.Name, server.Address, server.HubAddress));
            await _settings.WriteFavoritesAsync(favorites);
        }
        else if (alreadyExist != null)
        {
            favorites.Remove(alreadyExist);
            await _settings.WriteFavoritesAsync(favorites);
        }
    }

    private void HandleInfoNeeded(ServerStatusData server)
    {
        ((IServerSource)_fetcher).UpdateInfoFor(server);
        _cache.TryInitialPing(server);
    }

    private static void ExtractTags(IEnumerable<ServerStatusData> servers, out IReadOnlyList<string> rpTags, out IReadOnlyList<string> langTags, out IReadOnlyList<string> regionTags)
    {
        var allTags = servers
            .SelectMany(GetTags)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();
        rpTags = [.. allTags.Where(t => t.StartsWith("rp")).Select(ParseRPTag).Distinct()];
        langTags = [.. allTags.Where(t => t.StartsWith("lang:")).Select(ParseLangTag).Distinct()];
        regionTags = [.. allTags.Where(t => t.StartsWith("region:")).Select(ParseRegionTag).Distinct()];
    }

    private static readonly ConcurrentDictionary<string, string> _rpTagCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, string> _langTagCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, string> _regionTagCache = new(StringComparer.OrdinalIgnoreCase);

    public static string ParseRPTag(string tag) => _rpTagCache.GetOrAdd(tag, ParseRPTagCore);
    public static string ParseLangTag(string tag) => _langTagCache.GetOrAdd(tag, ParseLangTagCore);
    public static string ParseRegionTag(string tag) => _regionTagCache.GetOrAdd(tag, ParseRegionTagCore);

    private static string ParseRPTagCore(string tag)
    {
        foreach (var kvp in _rPTagTypes)
        {
            if (kvp.Value.Contains(tag, StringComparer.OrdinalIgnoreCase))
                return kvp.Key;
        }

        return tag;
    }

    private static string ParseLangTagCore(string tag)
    {
        if (!tag.StartsWith("lang:", StringComparison.OrdinalIgnoreCase))
            return tag;

        try
        {
            var cultureId = tag[5..];
            var culture = CultureInfo.GetCultureInfo(cultureId);
            return culture.TwoLetterISOLanguageName == culture.Name
                ? culture.EnglishName
                : CultureInfo.GetCultureInfo(culture.TwoLetterISOLanguageName).EnglishName;
        }
        catch (CultureNotFoundException)
        {
            return tag;
        }
    }

    private static string ParseRegionTagCore(string tag)
    {
        if (!tag.StartsWith("region:", StringComparison.OrdinalIgnoreCase))
            return tag;

        var regionId = tag[7..];
        if (_regionTransformations.TryGetValue(regionId, out var regionName))
            return regionName;

        try
        {
            var region = new RegionInfo(regionId);
            return region.TwoLetterISORegionName == region.Name
                ? region.EnglishName
                : new RegionInfo(region.TwoLetterISORegionName).EnglishName;
        }
        catch (ArgumentException)
        {
            return tag;
        }
    }

    private static readonly Dictionary<string, List<string>> _rPTagTypes = new()
    {
        ["NRP"] = ["rp:none", "rp:nrp", "rp"],
        ["LRP"] = ["rp:low", "rp:lrp"],
        ["MRP"] = ["rp:medium", "rp:mrp", "rp:med"],
        ["HRP"] = ["rp:high", "rp:hrp"]
    };

    private static readonly Dictionary<string, string> _regionTransformations = new()
    {
        ["af_c"] = "Africa Central",
        ["af_n"] = "Africa North",
        ["af_s"] = "Africa South",
        ["ata"] = "Antarctica",
        ["as_e"] = "Asia East",
        ["as_n"] = "Asia North",
        ["as_se"] = "Asia South East",
        ["am_c"] = "America Central",
        ["eu_e"] = "Europe East",
        ["eu_w"] = "Europe West",
        ["grl"] = "Greenland",
        ["ind"] = "India",
        ["me"] = "Middle East", // Wizdens, whyyy???
        ["luna"] = "Moon", // Wizdens, whyyy???
        ["am_n_c"] = "North America Central",
        ["am_n_e"] = "North America East",
        ["am_n_w"] = "North America West",
        ["oce"] = "Oceania",
        ["am_s_e"] = "South America East",
        ["am_s_s"] = "South America South",
        ["am_s_w"] = "South America West",
        ["eu"] = "Europe"
    };

    private static int GetPlayers(ServerStatusData s) => s.PlayerCount;
    private static int GetMaxPlayers(ServerStatusData s) => s.SoftMaxPlayerCount;
    private static int GetPing(ServerStatusData s) => (int?)(s.Ping?.TotalMilliseconds) ?? 0;
    private static IEnumerable<string> GetTags(ServerStatusData s) => s.Tags.Where(t => !string.IsNullOrWhiteSpace(t))!;
    private static string? GetLanguage(ServerStatusData s) => s.Tags.FirstOrDefault(x => x.StartsWith("lang:"));
    private static string? GetRegion(ServerStatusData s) => s.Tags.FirstOrDefault(x => x.StartsWith("region:"));

    private static bool IsAdult(ServerStatusData s)
    {
        foreach (var t in GetTags(s))
            if (t is "18+" or "+18")
                return true;
        return false;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _fetcher.ServersChanged -= OnServersChanged;
        _fetcher.StatusChanged -= OnStatusChanged;
        _filters.Changed -= OnFiltersChanged;
        _settings.FavoritesChanged -= OnFavoritesChanged;
        _disposeCts.Cancel();
        _disposeCts.Dispose();
        _filterCts?.Dispose();
    }
}
