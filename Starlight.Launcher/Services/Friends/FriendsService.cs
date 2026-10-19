using System.Net;
using System.Net.Http.Headers;
using Serilog;
using Starlight.Launcher.Services.Auth;
using Starlight.Launcher.Services.Journal;
using Starlight.Launcher.WebUI.Localization;
using Starlight.Launcher.WebUI.Models.Auth;
using Starlight.Launcher.WebUI.Models.Friends;
using Starlight.Launcher.WebUI.Models.Journal;

namespace Starlight.Launcher.Services.Friends;

public sealed class FriendsService(
    HttpClient http,
    StarlightAuthApi starlight,
    LoginManager logins,
    JournalService journal,
    ServerMonitor monitor,
    ILocalizationManager loc)
{
    private static readonly TimeSpan _requestTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan _heartbeatInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan _requestsPollInterval = TimeSpan.FromMinutes(3);

    private readonly CancellationTokenSource _cts = new();
    private CancellationTokenSource? _heartbeatCts;
    private HashSet<Guid>? _knownIncoming;
    private Guid? _knownIncomingFor;

    public event Action? Changed;

    public void Start()
    {
        journal.ActiveSessionChanged += OnActiveSessionChanged;
        _ = Task.Run(() => PollRequestsAsync(_cts.Token));
    }

    public void Stop()
    {
        _cts.Cancel();
        Interlocked.Exchange(ref _heartbeatCts, null)?.Cancel();
    }

    public async Task<FriendsResult> GetAsync(CancellationToken cancel = default)
    {
        var (status, response) = await SendAsync(HttpMethod.Get, "api/friends", null, cancel);
        if (status != FriendsStatus.Loaded || response is null)
            return new FriendsResult(status);

        using (response)
        {
            var list = await response.Content.ReadFromJsonAsync<FriendsList>(cancel);
            return list is null ? new FriendsResult(FriendsStatus.Unavailable) : new FriendsResult(FriendsStatus.Loaded, list);
        }
    }

    public Task<FriendActionResult> SendRequestAsync(string name, CancellationToken cancel = default)
        => ActionAsync(HttpMethod.Post, "api/friends/requests", new { name = name.Trim() }, cancel);

    public Task<FriendActionResult> AcceptAsync(Guid userId, CancellationToken cancel = default)
        => ActionAsync(HttpMethod.Post, $"api/friends/requests/{userId}/accept", null, cancel);

    public Task<FriendActionResult> RemoveAsync(Guid userId, CancellationToken cancel = default)
        => ActionAsync(HttpMethod.Delete, $"api/friends/{userId}", null, cancel);

    public async Task<FriendActionResult> SetSharePresenceAsync(bool share, CancellationToken cancel = default)
    {
        var result = await ActionAsync(HttpMethod.Put, "api/friends/settings", new { sharePresence = share }, cancel);
        if (result.Status == FriendsStatus.Loaded && share && journal.ActiveSession is { } session)
            OnActiveSessionChanged(session);

        return result;
    }

    private async Task<FriendActionResult> ActionAsync(HttpMethod method, string path, object? body, CancellationToken cancel)
    {
        var (status, response) = await SendAsync(method, path, body, cancel);
        if (response is null)
            return new FriendActionResult(status);

        using (response)
        {
            string? result = null;
            if (response.Content.Headers.ContentLength is not 0)
            {
                try { result = (await response.Content.ReadFromJsonAsync<ActionResponse>(cancel))?.Result; }
                catch { /* empty or non-json body */ }
            }

            Changed?.Invoke();
            return new FriendActionResult(FriendsStatus.Loaded, result);
        }
    }

    private async Task<(FriendsStatus, HttpResponseMessage?)> SendAsync(HttpMethod method, string path, object? body, CancellationToken cancel)
    {
        if (logins.ActiveAccount is not { } account)
            return (FriendsStatus.NotLinked, null);

        for (var attempt = 0; ; attempt++)
        {
            if (Token(account) is not { } token)
                return (attempt == 0 ? FriendsStatus.NotLinked : FriendsStatus.Unauthorized, null);

            HttpResponseMessage response;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
                timeout.CancelAfter(_requestTimeout);

                using var request = new HttpRequestMessage(method, new Uri(starlight.ApiUrl, path));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                if (body is not null)
                    request.Content = JsonContent.Create(body);

                response = await http.SendAsync(request, timeout.Token);
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                Log.Warning(e, "Can't reach the backend for {Path}", path);
                return (FriendsStatus.Unavailable, null);
            }

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                response.Dispose();
                if (attempt > 0)
                    return (FriendsStatus.Unauthorized, null);

                _ = await logins.EnsureFreshAsync(account, cancel);
                continue;
            }

            if (response.IsSuccessStatusCode || response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest or HttpStatusCode.Conflict)
                return (FriendsStatus.Loaded, response);

            Log.Warning("Backend answered {Status} for {Path}", response.StatusCode, path);
            response.Dispose();
            return (FriendsStatus.Unavailable, null);
        }
    }

    private static string? Token(LoggedInAccount account)
        => (account.LoginInfo.DiscordToken ?? account.LoginInfo.SteamToken)?.Token;

    private void OnActiveSessionChanged(PlaySession? session)
    {
        var cts = session is null ? null : new CancellationTokenSource();
        var old = Interlocked.Exchange(ref _heartbeatCts, cts);
        old?.Cancel();
        old?.Dispose();

        if (session is null)
        {
            if (old is not null)
                _ = Task.Run(() => SendQuietlyAsync(HttpMethod.Delete, "api/friends/presence", null, CancellationToken.None));

            return;
        }

        _ = Task.Run(() => HeartbeatAsync(session.Address, cts!.Token));
    }

    private async Task HeartbeatAsync(string address, CancellationToken cancel)
    {
        using var timer = new PeriodicTimer(_heartbeatInterval);
        do
        {
            var name = journal.ActiveSession?.ServerName;
            await SendQuietlyAsync(HttpMethod.Post, "api/friends/presence", new { address, serverName = name }, cancel);

            try
            {
                if (!await timer.WaitForNextTickAsync(cancel))
                    return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
        } while (true);
    }

    private async Task SendQuietlyAsync(HttpMethod method, string path, object? body, CancellationToken cancel)
    {
        try
        {
            var (_, response) = await SendAsync(method, path, body, cancel);
            response?.Dispose();
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Log.Debug(e, "Presence update failed");
        }
    }

    private async Task PollRequestsAsync(CancellationToken cancel)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(20), cancel); }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(_requestsPollInterval);
        do
        {
            try
            {
                await CheckIncomingAsync(cancel);
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                Log.Debug(e, "Friend request check failed");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(cancel))
                    return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
        } while (true);
    }

    private async Task CheckIncomingAsync(CancellationToken cancel)
    {
        if (logins.ActiveAccount is not { } account || Token(account) is null)
            return;

        var result = await GetAsync(cancel);
        if (result.List is not { } list)
            return;

        var incoming = list.Incoming.Select(x => x.UserId).ToHashSet();
        var accountId = account.LoginInfo.UserId;

        if (_knownIncoming is { } known && _knownIncomingFor == accountId)
        {
            var fresh = list.Incoming.Where(x => !known.Contains(x.UserId)).ToList();
            if (fresh.Count > 0)
            {
                monitor.Notify(new LauncherNotification(
                    LauncherNotificationKind.FriendRequest,
                    loc.GetString("notify-friend-request-title"),
                    loc.GetString("notify-friend-request-body", ("name", string.Join(", ", fresh.Select(x => x.Name))))));
                Changed?.Invoke();
            }
        }

        _knownIncoming = incoming;
        _knownIncomingFor = accountId;
    }

    private sealed record ActionResponse(string? Result);
}
