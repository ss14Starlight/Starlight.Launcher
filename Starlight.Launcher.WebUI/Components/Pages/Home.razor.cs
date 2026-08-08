using Microsoft.AspNetCore.Components;
using MudBlazor;
using Robust.Launcher.Api.Models.ServerStatus;
using Starlight.Launcher.WebUI.Bridge;
using Starlight.Launcher.WebUI.Components.Atoms.Dialogs;
using Starlight.Launcher.WebUI.Localization;
using Starlight.Launcher.WebUI.Models.Data;
using Starlight.Launcher.WebUI.Models.HubServerFetcher;

namespace Starlight.Launcher.WebUI.Components.Pages;

public partial class Home : LocalizedComponentBase, IDisposable
{
    [Inject] private IBridge _bridge { get; set; } = default!;
    [Inject] private ServerStatusCache _statusCache { get; set; } = default!;
    [Inject] private IDialogService _dialogService { get; set; } = default!;

    private List<ServerStatusData> _favoriteServers { get; set; } = null!;
    private readonly CancellationTokenSource _disposeCts = new();
    private int _rebuildScheduled;

    public override void Dispose()
    {
        base.Dispose();
        _bridge.FavoritesChanged -= HandleFavorites;
        _bridge.ServersChanged -= OnServersChanged;
        _bridge.StatusChanged -= OnStatusChanged;
        _disposeCts.Cancel();
        _disposeCts.Dispose();
        GC.SuppressFinalize(this);
    }

    private void RefreshServers() => _statusCache.Refresh();

    protected override async Task OnInitializedAsync()
    {
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

        _favoriteServers = servers.Select(x =>
        {
            var data = _statusCache.GetStatusFor(x.Address, x.HubAddress);
            _ = _statusCache.TryInitialUpdateStatus(data);
            data.Changed += OnServerDataChanged;
            return data;
        }).ToList();
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

    private async Task OpenDirectConnect()
    {
        var dialog = await _dialogService.ShowAsync<DirectConnectDialog>(
            "Direct Connect");
        var dialogResult = await dialog.Result;

        if (dialogResult is null || dialogResult.Canceled)
            return;

        var result = (DirectConnectResult)dialogResult.Data!;

        if (result.AddToFavorites)
            await AddDirectFavorite(result.Address);

        await ShowConnecting(p => { p.Add(x => x.Address, result.Address); p.Add(x => x.Title, null); } );
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
    private Task ShowConnecting(Action<DialogParameters<ConnectingDialog>> configure)
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
