using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor;
using Robust.Launcher.Api.Models.ServerStatus;
using Starlight.Launcher.WebUI.Bridge;
using Starlight.Launcher.WebUI.Components.Atoms.Dialogs;
using Starlight.Launcher.WebUI.Localization;
using Starlight.Launcher.WebUI.Models.Data;
using Starlight.Launcher.WebUI.Models.HubServerFetcher;
using Starlight.Launcher.WebUI.Models.Settings;

namespace Starlight.Launcher.WebUI.Components.Pages;

public partial class Home : LocalizedComponentBase, IAsyncDisposable
{
    [Inject] private IBridge _bridge { get; set; } = default!;
    [Inject] private ServerStatusCache _statusCache { get; set; } = default!;
    [Inject] private IDialogService _dialogService { get; set; } = default!;
    [Inject] private IJSRuntime _js { get; set; } = default!;
    [Inject] private ILogger<Home> _logger { get; set; } = default!;

    private List<ServerStatusData> _favoriteServers { get; set; } = null!;

    private readonly CancellationTokenSource _disposeCts = new();
    private int _rebuildScheduled;

    private ElementReference _panel;
    private IJSObjectReference? _reorderModule;
    private DotNetObjectReference<Home>? _selfRef;
    private TaskCompletionSource? _renderedTcs;
    private bool _reorderLocked;

    private bool _disposed;

    public override void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        base.Dispose();
        _bridge.FavoritesChanged -= HandleFavorites;
        _bridge.ServersChanged -= OnServersChanged;
        _bridge.StatusChanged -= OnStatusChanged;
        _disposeCts.Cancel();
        _disposeCts.Dispose();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        _renderedTcs?.TrySetResult();

        if (_reorderModule is not null)
        {
            try
            {
                await _reorderModule.InvokeVoidAsync("detach", _panel);
                await _reorderModule.DisposeAsync();
            }
            catch (JSDisconnectedException) { }
        }

        _selfRef?.Dispose();
        GC.SuppressFinalize(this);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        _renderedTcs?.TrySetResult();

        if (!firstRender)
            return;

        _selfRef = DotNetObjectReference.Create(this);
        try
        {
            _reorderModule = await _js.InvokeAsync<IJSObjectReference>("import", "./js/list-reorder.js");
            await _reorderModule.InvokeVoidAsync("attach", _panel, _selfRef);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Can't use js interpop to allow server list reordering!");
        }
    }

    private void RefreshServers() => _statusCache.Refresh([.. _favoriteServers]);

    protected override async Task OnInitializedAsync()
    {
        _reorderLocked = _bridge.GetSettings().FavoritesReorderLocked;
        UpdateFavorites(await _bridge.GetFavoritesAsync());
        _bridge.FavoritesChanged += HandleFavorites;
        _bridge.ServersChanged += OnServersChanged;
        _bridge.StatusChanged += OnStatusChanged;
        await base.OnInitializedAsync();
    }

    private async void OnServersChanged()
    {
        if (Interlocked.CompareExchange(ref _rebuildScheduled, 1, 0) != 0)
            return;

        try
        {
            await Task.Delay(200, _disposeCts.Token);
            await InvokeAsync(() =>
            {
                _ = Interlocked.Exchange(ref _rebuildScheduled, 0);
                UpdateFavorites(_bridge.GetFavorites());
                StateHasChanged();
            });
        }
        catch (OperationCanceledException) { _ = Interlocked.Exchange(ref _rebuildScheduled, 0); }
        catch (ObjectDisposedException) { _ = Interlocked.Exchange(ref _rebuildScheduled, 0); }
    }

    private async void OnStatusChanged(RefreshListStatus _)
    {
        try { await InvokeAsync(StateHasChanged); }
        catch (ObjectDisposedException) { }
    }

    private void UpdateFavorites(List<FavoriteServer> servers)
    {
        foreach (var s in _favoriteServers ?? Enumerable.Empty<ServerStatusData>())
            s.Changed -= OnServerDataChanged;

        var settings = _bridge.GetSettings();

        var filteredServers = servers.Where(x => !settings.IgnoredServers.Any(s => s.Name == x.Name || s.Address == x.Address));

        _favoriteServers = [.. filteredServers.Select(x =>
        {
            var data = _statusCache.GetStatusFor(x.Address, x.HubAddress);
            _ = _statusCache.TryInitialUpdateStatus(data);
            data.Changed += OnServerDataChanged;
            return data;
        })];
    }

    private void UpdateFavorites(List<ServerStatusData> servers)
    {
        foreach (var s in _favoriteServers ?? Enumerable.Empty<ServerStatusData>())
            s.Changed -= OnServerDataChanged;

        var settings = _bridge.GetSettings();

        var filteredServers = servers.Where(x => !settings.IgnoredServers.Any(s => s.Name == x.Name || s.Address == x.Address));

        _favoriteServers = [.. filteredServers.Select(x =>
        {
            _ = _statusCache.TryInitialUpdateStatus(x);
            x.Changed += OnServerDataChanged;
            return x;
        })];
    }

    private async void OnServerDataChanged()
    {
        try { await InvokeAsync(StateHasChanged); }
        catch (ObjectDisposedException) { }
    }

    private async void HandleFavorites()
    {
        try
        {
            await InvokeAsync(async () =>
            {
                UpdateFavorites(await _bridge.GetFavoritesAsync());
                StateHasChanged();
            });
        }
        catch (ObjectDisposedException) { }
    }

    private async Task HandleFavorite(ServerStatusData server)
    {
        var favorites = _bridge.GetFavorites();
        var alreadyExist = favorites.FirstOrDefault(x => x.Address == server.Address);

        if ((alreadyExist == null || alreadyExist == default) && server.HubAddress != null)
        {
            favorites.Add(new FavoriteServer(server.Name, server.Address, server.HubAddress));
            await _bridge.WriteFavoritesAsync(favorites);
        }
        else if (alreadyExist != null)
        {
            _ = favorites.Remove(alreadyExist);
            await _bridge.WriteFavoritesAsync(favorites);
        }
    }

    [JSInvokable]
    public async Task MoveItem(string address, int toIndex)
    {
        var from = _favoriteServers.FindIndex(x => x.Address == address);
        if (from < 0)
            return;

        var server = _favoriteServers[from];
        _favoriteServers.RemoveAt(from);
        _favoriteServers.Insert(Math.Clamp(toIndex, 0, _favoriteServers.Count), server);

        _renderedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        StateHasChanged();
        await _renderedTcs.Task;
        _renderedTcs = null;

        await SaveFavoritesOrder();
    }

    private async Task SaveFavoritesOrder()
    {
        var order = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < _favoriteServers.Count; i++)
            _ = order.TryAdd(_favoriteServers[i].Address, i);

        var favorites = _bridge.GetFavorites();

        var visible = new Queue<FavoriteServer>(favorites
            .Where(f => order.ContainsKey(f.Address))
            .OrderBy(f => order[f.Address]));

        var reordered = favorites
            .ConvertAll(f => order.ContainsKey(f.Address) ? visible.Dequeue() : f)
;

        if (reordered.SequenceEqual(favorites))
            return;

        await _bridge.WriteFavoritesAsync(reordered);
    }

    private void ToggleReorderLock()
    {
        _reorderLocked = !_reorderLocked;
        _bridge.UpdateSettings(s => s with { FavoritesReorderLocked = _reorderLocked });
    }

    private void HandleIgnore(ServerStatusData server)
    {
        var settings = _bridge.GetSettings();

        if (settings.IgnoredServers.Any(x => x.Name == server.Name || x.Address == server.Address))
            return;

        _bridge.UpdateSettings(s => s with { IgnoredServers = [.. s.IgnoredServers, new IgnoredServer(server.Name, server.Address)] });

        UpdateFavorites(_favoriteServers);
    }

    private async Task OpenDirectConnect()
    {
        var dialog = await _dialogService.ShowAsync<DirectConnectDialog>(
            L["home-menu-dirrect-connect"]);
        var dialogResult = await dialog.Result;

        if (dialogResult?.Canceled != false)
            return;

        var result = (DirectConnectResult)dialogResult.Data!;

        if (result.AddToFavorites)
            await AddDirectFavorite(result.Address);

        await ShowConnecting(p =>
        {
            p.Add(x => x.Address, result.Address);
            p.Add(x => x.Title, null);
        });
    }

    private async Task LoadReplay()
    {
        var file = await _bridge.PickFileAsync();
        if (file is null)
            return;

        var parameters = new DialogParameters<ConnectingDialog>
        {
            { x => x.ContentBundle, file },
            { x => x.Title, file.FileName }
        };

        var options = new DialogOptions
        {
            BackdropClick = false,
            CloseOnEscapeKey = false,
            CloseButton = false,
            MaxWidth = MaxWidth.ExtraSmall,
            FullWidth = true
        };

        _ = await _dialogService.ShowAsync<ConnectingDialog>("Loading replay", parameters, options);
    }

    private async Task AddDirectFavorite(string address)
    {
        var favorites = _bridge.GetFavorites();
        if (favorites.Any(x => x.Address == address))
            return;

        favorites.Add(new FavoriteServer(address, address, ""));
        await _bridge.WriteFavoritesAsync(favorites);
    }

    private Task<IDialogReference> ShowConnecting(Action<DialogParameters<ConnectingDialog>> configure)
    {
        var parameters = new DialogParameters<ConnectingDialog>();
        configure(parameters);

        var options = new DialogOptions
        {
            CloseOnEscapeKey = false,
            BackdropClick = false,
            CloseButton = false,
        };

        return _dialogService.ShowAsync<ConnectingDialog>("Connecting", parameters, options);
    }
}
