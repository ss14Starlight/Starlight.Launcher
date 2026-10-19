using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;
using Starlight.Launcher.WebUI.Bridge;
using Starlight.Launcher.WebUI.Components.Atoms.Dialogs;
using Starlight.Launcher.WebUI.Localization;
using Starlight.Launcher.WebUI.Models.Friends;

namespace Starlight.Launcher.WebUI.Components.Pages;

public partial class Friends : LocalizedComponentBase, IDisposable
{
    private static readonly TimeSpan _refreshInterval = TimeSpan.FromSeconds(45);

    [Inject] private IBridge _bridge { get; set; } = default!;
    [Inject] private ISnackbar _snackbar { get; set; } = default!;
    [Inject] private IDialogService _dialogService { get; set; } = default!;

    private FriendsList? _list;
    private FriendsStatus _status = FriendsStatus.Loaded;
    private bool _loading;
    private bool _busy;
    private string _newFriend = "";
    private readonly CancellationTokenSource _cts = new();

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        _bridge.FriendsChanged += OnFriendsChanged;
        _bridge.LoginEntriesChanged += OnFriendsChanged;
        await LoadAsync();
        _ = RefreshLoopAsync(_cts.Token);
    }

    private async Task RefreshLoopAsync(CancellationToken cancel)
    {
        using var timer = new PeriodicTimer(_refreshInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancel))
                await InvokeAsync(LoadAsync);
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    private async void OnFriendsChanged()
    {
        try { await InvokeAsync(LoadAsync); }
        catch (ObjectDisposedException) { }
    }

    private async Task LoadAsync()
    {
        if (_loading)
            return;

        _loading = true;
        try
        {
            var result = await _bridge.GetFriendsAsync(_cts.Token);
            _status = result.Status;
            if (result.List is not null || result.Status != FriendsStatus.Unavailable)
                _list = result.List;
        }
        catch (OperationCanceledException) { }
        finally
        {
            _loading = false;
        }
    }

    private async Task OnAddKeyDown(KeyboardEventArgs e)
    {
        if (e.Key is "Enter" or "NumpadEnter" && !string.IsNullOrWhiteSpace(_newFriend))
            await SendRequestAsync();
    }

    private async Task SendRequestAsync()
    {
        var name = _newFriend.Trim();
        if (name.Length == 0)
            return;

        var result = await RunAsync(() => _bridge.SendFriendRequestAsync(name, _cts.Token));
        if (result is null)
            return;

        var (message, severity) = result switch
        {
            "sent" => (L.GetString("friends-result-sent", ("name", name)), Severity.Success),
            "friends" => (L.GetString("friends-result-friends", ("name", name)), Severity.Success),
            "already-friends" => (L.GetString("friends-result-already-friends", ("name", name)), Severity.Info),
            "already-sent" => (L.GetString("friends-result-already-sent", ("name", name)), Severity.Info),
            "self" => (L["friends-result-self"], Severity.Warning),
            "not-found" => (L.GetString("friends-result-not-found", ("name", name)), Severity.Warning),
            "limit" => (L["friends-result-limit"], Severity.Warning),
            _ => (L["friends-result-error"], Severity.Error),
        };

        _ = _snackbar.Add(message, severity);
        if (severity == Severity.Success)
            _newFriend = "";
    }

    private async Task AcceptAsync(FriendUser user)
    {
        if (await RunAsync(() => _bridge.AcceptFriendAsync(user.UserId, _cts.Token)) == "limit")
            _ = _snackbar.Add(L["friends-result-limit"], Severity.Warning);
    }

    private async Task RemoveAsync(Guid userId, string? confirmName)
    {
        if (confirmName is not null)
        {
            var confirmed = await _dialogService.ShowMessageBoxAsync(
                L["friends-remove"],
                L.GetString("friends-remove-confirm", ("name", confirmName)),
                yesText: L["friends-remove"],
                cancelText: L["general-cancel"]);

            if (confirmed != true)
                return;
        }

        _ = await RunAsync(() => _bridge.RemoveFriendAsync(userId, _cts.Token));
    }

    private async Task SetSharePresenceAsync(bool share)
    {
        _ = await RunAsync(() => _bridge.SetSharePresenceAsync(share, _cts.Token));
        if (_list is not null && _status == FriendsStatus.Loaded)
            _list = _list with { SharePresence = share };
    }

    private async Task<string?> RunAsync(Func<Task<FriendActionResult>> action)
    {
        _busy = true;
        try
        {
            var result = await action();
            if (result.Status != FriendsStatus.Loaded)
            {
                _ = _snackbar.Add(L["friends-result-error"], Severity.Error);
                return null;
            }

            return result.Result ?? "";
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            _busy = false;
        }
    }

    private Task<IDialogReference> Join(string address, string? name)
    {
        var parameters = new DialogParameters<ConnectingDialog>
        {
            { x => x.Address, address },
            { x => x.Title, name }
        };

        return _dialogService.ShowAsync<ConnectingDialog>(null, parameters, new DialogOptions
        {
            BackdropClick = false,
            CloseOnEscapeKey = false,
            CloseButton = false,
            MaxWidth = MaxWidth.ExtraSmall,
            FullWidth = true
        });
    }

    private static string Initial(string name) => string.IsNullOrEmpty(name) ? "?" : name[..1].ToUpperInvariant();

    private string PlayingFor(DateTime sinceUtc)
    {
        var elapsed = DateTime.UtcNow - sinceUtc;
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;

        return elapsed.TotalHours >= 1
            ? L.GetString("journal-duration-hours", ("hours", (int)elapsed.TotalHours), ("minutes", elapsed.Minutes))
            : L.GetString("journal-duration-minutes", ("minutes", (int)elapsed.TotalMinutes));
    }

    public override void Dispose()
    {
        base.Dispose();
        _bridge.FriendsChanged -= OnFriendsChanged;
        _bridge.LoginEntriesChanged -= OnFriendsChanged;
        _cts.Cancel();
        _cts.Dispose();
        GC.SuppressFinalize(this);
    }
}
