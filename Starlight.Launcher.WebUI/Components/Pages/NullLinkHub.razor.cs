using Microsoft.AspNetCore.Components;
using MudBlazor;
using Starlight.Launcher.Components.Atoms;
using Starlight.Launcher.Components.Atoms.Dialogs;
using Starlight.Launcher.Models.Data;
using Starlight.Launcher.Services.Localization;
using Starlight.Launcher.Services.Settings;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Color = MudBlazor.Color;

namespace Starlight.Launcher.Components.Pages;

public sealed partial class NullLinkHub : ComponentBase, IAsyncDisposable
{
    [Inject] private HttpClient _client { get; set; } = default!;
    [Inject] private SettingsService _settings { get; set; } = default!;
    [Inject] private IDialogService _dialog { get; set; } = default!;
    [Inject] private LocalizationManager _localization { get; set; } = default!;
    private List<ServerListItem>? _servers;
    private string? _error;
    private CancellationTokenSource? _cts;
    private DateTime _lastUpdated;
    private bool _isRefreshing;

    private static readonly TimeSpan _refreshInterval = TimeSpan.FromSeconds(10);

    private IReadOnlySet<string> _favoriteAddresses =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    protected override async Task OnInitializedAsync()
    {
        _cts = new CancellationTokenSource();
        await LoadAsync(_cts.Token);
        _ = PollLoopAsync(_cts.Token);

        _settings.FavoritesChanged += OnFavoritesChanged;
    }

    private async Task PollLoopAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(_refreshInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                await LoadAsync(token);
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RefreshAsync()
    {
        if (_cts is null)
            return;

        _isRefreshing = true;
        await LoadAsync(_cts.Token);
        _isRefreshing = false;
    }

    private async Task LoadAsync(CancellationToken token)
    {
        try
        {
            var baseUrl = _settings.GetSettings().StarlightAPIUrl;
            var url = new Uri(new Uri(baseUrl), "api/servers");

            var result = await _client.GetFromJsonAsync<List<ServerListItem>>(url, token);

            _servers = result ?? new();
            _lastUpdated = DateTime.UtcNow;
            _error = null;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
    }

    private async Task Connect(ServerListItem server)
    {
        var parameters = new DialogParameters<ConnectingDialog>
        {
            { x => x.Address, server.ConnectionString },
            { x => x.Title, server.Title },
        };

        var options = new DialogOptions
        {
            BackdropClick = false,
            CloseOnEscapeKey = false,
            CloseButton = false,
            MaxWidth = MaxWidth.ExtraSmall,
            FullWidth = true
        };

        await _dialog.ShowAsync<ConnectingDialog>("Connecting", parameters, options);
    }

    private bool IsFavorite(ServerListItem server)
        => _favoriteAddresses.Contains(server.ConnectionString);

    private async Task ToggleFavorite(ServerListItem server)
    {
        var favorites = _settings.GetFavorites();
        var alreadyExist = favorites.FirstOrDefault(x => x.Address == server.ConnectionString);

        if (alreadyExist is null or default(FavoriteServer?))
        {
            favorites.Add(new FavoriteServer(server.Title, server.ConnectionString, ""));
            await _settings.WriteFavoritesAsync(favorites);
        }
        else if (alreadyExist != null)
        {
            favorites.Remove(alreadyExist);
            await _settings.WriteFavoritesAsync(favorites);
        }
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

    private static Color GetStatusColor(ServerStatus? status) => status switch
    {
        ServerStatus.Lobby => Color.Success,
        ServerStatus.Round => Color.Info,
        ServerStatus.RoundEnding => Color.Warning,
        ServerStatus.Offline => Color.Error,
        _ => Color.Default
    };

    private static string GetStatusIcon(ServerStatus? status) => status switch
    {
        ServerStatus.Lobby => Icons.Material.Filled.MeetingRoom,
        ServerStatus.Round => Icons.Material.Filled.PlayArrow,
        ServerStatus.RoundEnding => Icons.Material.Filled.Timelapse,
        ServerStatus.Offline => Icons.Material.Filled.PowerSettingsNew,
        _ => Icons.Material.Filled.HelpOutline
    };

    private static double GetPopulation(ServerListItem server)
    {
        if (server.MaxPlayers is null or 0)
            return 0;

        return (double)(server.Players ?? 0) / server.MaxPlayers.Value * 100;
    }

    private static Color GetPopulationColor(ServerListItem server) => GetPopulation(server) switch
    {
        >= 90 => Color.Error,
        >= 60 => Color.Warning,
        _ => Color.Success
    };

    private static string FormatType(ServerType type) =>
        type.ToString().Replace("_minus", "−").Replace("_plus", "+");

    private static string FormatUptime(DateTime? startedAt)
    {
        if (startedAt is null)
            return "—";

        var start = startedAt.Value.Kind == DateTimeKind.Utc
            ? startedAt.Value
            : startedAt.Value.ToUniversalTime();

        var elapsed = DateTime.UtcNow - start;
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;

        if (elapsed.TotalDays >= 1)
            return $"{(int)elapsed.TotalDays}d {elapsed.Hours}h";
        if (elapsed.TotalHours >= 1)
            return $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m";

        return $"{elapsed.Minutes}m";
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync();
            _cts.Dispose();
        }

        _settings.FavoritesChanged -= OnFavoritesChanged;
    }

    private static MarkupString ParseDescription(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new MarkupString("");

        var html = text;

        html = Regex.Replace(
            html,
            @"\[color=(.*?)\](.*?)\[/color\]",
            "<span style=\"color:$1\">$2</span>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        html = html.Replace("\n", "<br>");

        return new MarkupString(html);
    }
}

public sealed record ServerListItem
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public ServerType Type { get; init; }
    public bool IsAdultOnly { get; init; }
    public required string ConnectionString { get; init; }
    public ServerStatus? Status { get; init; }
    public int? Players { get; init; }
    public int? MaxPlayers { get; init; }
    public DateTime? CurrentStateStartedAt { get; init; }
}

public enum ServerStatus : byte
{
    Offline,
    Lobby,
    Round,
    RoundEnding,
}

public enum ServerType : byte
{
    NRP,
    LRP_minus,
    LRP,
    LRP_plus,
    MRP_minus,
    MRP,
    MRP_plus,
    HRP_minus,
    HRP,
    HRP_plus,
}
