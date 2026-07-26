using Robust.Launcher.Api.Api;
using Robust.Launcher.Api.Models.ServerStatus;
using Robust.Launcher.Api.Utility;
using Serilog;
using Starlight.Launcher.Services.Settings;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Starlight.Launcher.Services.ServerStatus;

public sealed partial class HubServerFetcher(HubApi hub, SettingsService settings, ServerStatusCache cache) : IServerSource, IDisposable
{
    #region Injections

    private readonly HubApi _hubApi = hub;
    private readonly SettingsService _settings = settings;
    private readonly ServerStatusCache _cache = cache;

    #endregion

    #region Data and state

    private volatile bool _disposed;

    private readonly List<ServerStatusData> _allServers = [];
    private const int MaxConcurrentPerHub = 2;

    #endregion

    #region Public Data Access

    public RefreshListStatus Status { get; private set; } = RefreshListStatus.NotUpdated;
    public IReadOnlyList<ServerStatusData> AllServers { get; private set; } = [];

    #endregion

    #region Synchronization

    private CancellationTokenSource? _refreshCancel;

    private readonly Lock _serversLock = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _hubThrottles =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, DateTimeOffset> _hubBackoffUntil = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _hubFailCount = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _backoffLock = new();

    #endregion

    #region Actions

    public event Action? ServersChanged;
    public event Action<RefreshListStatus>? StatusChanged;

    #endregion

    #region Public Methods

    /// <summary>
    /// This function requests the initial update from the server if one hasn't already been requested.
    /// </summary>
    public void RequestInitialUpdate()
    {
        if (_disposed) return;

        _lock.Wait();
        try
        {
            if (Status == RefreshListStatus.NotUpdated)
                RequestRefresh();
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// This function performs a refresh.
    /// </summary>
    public void RequestRefresh()
    {
        if (_disposed) return;

        var newCts = new CancellationTokenSource();
        var oldCts = Interlocked.Exchange(ref _refreshCancel, newCts);

        try { oldCts?.Cancel(); }
        catch (ObjectDisposedException) { }

        if (oldCts != null)
            _ = DisposeCtsAfterDelay(oldCts);

        _ = RefreshServerListSafe(newCts.Token);
    }

    #endregion

    #region Main Logic

    /// <summary>
    /// Requests server lists from all hubs and updates the internal list. This is called by RequestRefresh, which also handles cancellation and error catching.
    /// </summary>
    private async Task RefreshServerList(CancellationToken cancel)
    {
        var sw = Stopwatch.StartNew();

        lock (_serversLock)
            _allServers.Clear();
        SetStatus(RefreshListStatus.UpdatingMaster);

        var entries = new Dictionary<string, HubServerListEntry>(StringComparer.OrdinalIgnoreCase);
        var allSucceeded = true;
        var skippedDueToBackoff = 0;
        var skippedDueToFailure = 0;

        var settings = await _settings.GetSettingsAsync().ConfigureAwait(false);
        Log.Information("Refreshing server list from {Count} hubs", settings.Hubs.Count);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));
        var requestToken = timeoutCts.Token;

        var requests = new List<(Task<HubApi.ServerListEntry[]> Task, Uri Hub)>();

        // Queue requests
        foreach (var hub in settings.Hubs.OrderBy(h => h.Priority))
        {
            if (IsHubBackedOff(hub.HubUri.AbsoluteUri, out var remaining))
            {
                Log.Information("Skipping hub {Hub}: backoff {Remaining} remaining",
                    hub.HubUri, remaining);
                skippedDueToBackoff++;
                allSucceeded = false;
                continue;
            }
            requests.Add((_hubApi.GetServers(UrlFallbackSet.FromSingle(hub.HubUri), requestToken), hub.HubUri));
        }

        foreach (var (task, _) in requests)
        {
            try { await task.ConfigureAwait(false); }
            catch { }
        }

        cancel.ThrowIfCancellationRequested();

        // Process responses
        foreach (var (task, hub) in requests)
        {
            if (!task.IsCompletedSuccessfully)
            {
                allSucceeded = false;
                skippedDueToFailure++;
                HandleHubFailure(task, hub, cancel);
                continue;
            }

            ResetHubFailures(hub.AbsoluteUri);
            var hubEntries = task.Result;
            Log.Information("Hub {Hub} returned {Count} servers", hub, hubEntries.Length);

            foreach (var entry in hubEntries)
            {
                var maybeNewEntry = new HubServerListEntry(entry.Address, hub.AbsoluteUri, entry.StatusData);
                if (entry.Address != null && !entries.TryAdd(entry.Address, maybeNewEntry))
                {
                    Log.Verbose("Skipping {Entry} from {ThisHub}: already from {PreviousHub}",
                        entry.Address, hub.AbsoluteUri, entries[entry.Address].HubAddress);
                }
            }
        }

        lock (_serversLock)
        {
            _allServers.Clear();
            _allServers.AddRange(entries.Select(kv =>
            {
                var s = _cache.GetStatusFor(kv.Value.Address, kv.Value.HubAddress);
                ServerStatusCache.ApplyStatus(s, kv.Value.StatusData);
                return s;
            }));
            AllServers = _allServers.ToArray();
        }

        ServersChanged?.Invoke();

        var totalCount = entries.Count;
        Log.Information(
            "Refresh done in {Elapsed}ms: {Total} servers, {SkippedBackoff} hubs skipped due to backoff, {Skipped} skipped due to failure, success={Success}",
            sw.ElapsedMilliseconds, totalCount, skippedDueToBackoff, skippedDueToFailure, allSucceeded);

        if (totalCount == 0)
            SetStatus(RefreshListStatus.Error);
        else if (!allSucceeded)
            SetStatus(RefreshListStatus.PartialError);
        else
            SetStatus(RefreshListStatus.Updated);
    }

    public Task UpdateInfoForAsync(ServerStatusData statusData)
    {
        if (statusData.HubAddress == null)
        {
            Log.Error("Tried to get info for hubbed server {Name} without HubAddress", statusData.Name);
            statusData.StatusInfo = ServerStatusInfoCode.Error;
            return Task.CompletedTask;
        }

        return FireAndForgetUpdateInfo(statusData);
    }

    /// <summary>
    /// Trying to update info for a server. This is fire-and-forget and updates the ServerStatusData in-place. It also handles backoff for hubs that return 429 or time out.
    /// </summary>
    private async Task FireAndForgetUpdateInfo(ServerStatusData statusData)
    {
        var hubAddress = statusData.HubAddress!;

        if (IsHubBackedOff(hubAddress, out var remaining))
        {
            Log.Verbose("Skip info for {Address}: hub {Hub} in backoff for {Remaining}",
                statusData.Address, hubAddress, remaining);
            statusData.StatusInfo = ServerStatusInfoCode.Error;
            return;
        }

        var throttle = GetHubThrottle(hubAddress);

        try
        {
            await _cache.UpdateInfoForCore(
                statusData,
                async token =>
                {
                    await throttle.WaitAsync(token).ConfigureAwait(false);
                    try
                    {
                        return await _hubApi
                            .GetServerInfo(statusData.Address, hubAddress, token)
                            .ConfigureAwait(false);
                    }
                    finally
                    {
                        throttle.Release();
                    }
                }).ConfigureAwait(false);
        }
        catch (HubApiException ex) when (ex.IsRateLimited)
        {
            SetHubBackoff(hubAddress, ex.RetryAfter);
            Log.Warning("GetServerInfo for {Address} hit 429 on {Hub}",
                statusData.Address, hubAddress);
            statusData.StatusInfo = ServerStatusInfoCode.Error;
        }
        catch (HubApiException ex) when (ex.IsTimeout)
        {
            Log.Warning("GetServerInfo for {Address} timed out", statusData.Address);
            statusData.StatusInfo = ServerStatusInfoCode.Error;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Error(ex, "Error fetching info for {Address} from {Hub}",
                statusData.Address, hubAddress);
            statusData.StatusInfo = ServerStatusInfoCode.Error;
        }
    }

    #endregion

    #region BackOff(Drop on failure)

    /// <summary>
    /// Returns true if the hub is currently backed off, and sets the remaining backoff time. If the backoff has expired, it will clear the backoff state and return false.
    /// </summary>
    private bool IsHubBackedOff(string hubAddress, out TimeSpan remaining)
    {
        lock (_backoffLock)
        {
            if (_hubBackoffUntil.TryGetValue(hubAddress, out var until))
            {
                remaining = until - DateTimeOffset.UtcNow;
                if (remaining > TimeSpan.Zero)
                    return true;
                _hubBackoffUntil.Remove(hubAddress);
            }
        }
        remaining = TimeSpan.Zero;
        return false;
    }

    /// <summary>
    /// Sets hub as "backedoff" to avoid making requests to it for a certain duration. The duration increases exponentially with the number of consecutive failures, and can be overridden by a hint (e.g. Retry-After header). Returns the backoff duration that was set.
    /// </summary>
    private TimeSpan SetHubBackoff(string hubAddress, TimeSpan? hint = null)
    {
        TimeSpan duration;
        lock (_backoffLock)
        {
            var count = _hubFailCount.TryGetValue(hubAddress, out var c) ? c + 1 : 1;
            _hubFailCount[hubAddress] = count;
            if (hint.HasValue && hint.Value > TimeSpan.FromSeconds(5))
            {
                duration = hint.Value;
            }
            else
            {
                // 30s, 60s, 120s, 240s, ..., max 600s
                var seconds = Math.Min(30 * Math.Pow(2, count - 1), 600);
                duration = TimeSpan.FromSeconds(seconds);
            }

            _hubBackoffUntil[hubAddress] = DateTimeOffset.UtcNow + duration;
        }
        Log.Warning("Hub {Hub} backed off for {Duration} (hint: {Hint})",
            hubAddress, duration, hint);
        return duration;
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Returns unique throttle for the given hub address. This is used to limit concurrent requests to the same hub, to avoid hitting rate limits too quickly.
    /// </summary>
    private SemaphoreSlim GetHubThrottle(string hubAddress)
        => _hubThrottles.GetOrAdd(hubAddress, _ => new SemaphoreSlim(MaxConcurrentPerHub, MaxConcurrentPerHub));

    /// <summary>
    /// Updates the info for a server. This is called by ServerStatusCache when it wants to update info for a server that came from a hub. It will call FireAndForgetUpdateInfo, which does the actual work of fetching the info and updating the ServerStatusData in-place.
    /// </summary>
    void IServerSource.UpdateInfoFor(ServerStatusData statusData) => _ = UpdateInfoForAsync(statusData);

    /// <summary>
    /// Handles a failed hub request. This is called by RefreshServerList when processing the results of hub requests. It checks the type of failure and updates the backoff state for the hub accordingly. It also logs the failure with appropriate severity and details.
    /// </summary>
    private void HandleHubFailure(Task task, Uri hub, CancellationToken cancel)
    {
        var ex = task.Exception?.InnerException ?? task.Exception;
        if (ex == null && task.IsCanceled)
        {
            if (cancel.IsCancellationRequested)
                Log.Information("Hub {Hub} request canceled by caller", hub);
            else
                Log.Warning("Hub {Hub} request timed out", hub);
            return;
        }

        if (ex is HubApiException hubEx)
        {
            if (hubEx.IsRateLimited)
            {
                SetHubBackoff(hub.AbsoluteUri, hubEx.RetryAfter);
                Log.Warning("Hub {Hub} returned 429 (Retry-After: {RetryAfter})",
                    hub, hubEx.RetryAfter);
            }
            else if (hubEx.IsTimeout)
            {
                Log.Warning("Hub {Hub} timed out", hub);
                // SetHubBackoff(hub.AbsoluteUri);
            }
            else
            {
                Log.Warning(hubEx, "Hub {Hub} failed: status={Status}", hub, hubEx.StatusCode);
                if ((int?)hubEx.StatusCode >= 500)
                    SetHubBackoff(hub.AbsoluteUri);
            }
        }
        else
        {
            Log.Warning(ex, "Hub {Hub} request failed with unexpected error", hub);
        }
    }

    /// <summary>
    /// Helper method to dispose a CancellationTokenSource after a delay. This is used to avoid disposing a CTS that might still be in use by an ongoing request, while also ensuring that we don't leak CTS instances indefinitely.
    /// </summary>
    private static async Task DisposeCtsAfterDelay(CancellationTokenSource cts)
    {
        await Task.Delay(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
        try { cts.Dispose(); } catch { }
    }

    /// <summary>
    /// Helper method to call RefreshServerList with error handling. This is used by RequestRefresh to perform the refresh while catching any unhandled exceptions and updating the status accordingly. This ensures that an exception in RefreshServerList doesn't crash the application and provides feedback on the failure. The actual refresh logic is in RefreshServerList, which can assume that exceptions will be caught by this method.
    /// </summary>
    private async Task RefreshServerListSafe(CancellationToken cancel)
    {
        try
        {
            await RefreshServerList(cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unhandled exception in RefreshServerList");
            SetStatus(RefreshListStatus.Error);
        }
    }

    /// <summary>
    /// Helper method to reset the failure count and backoff state for a hub. This is called when a hub request succeeds, to allow future requests to the hub to proceed without unnecessary backoff. This is important to ensure that a temporary failure doesn't cause long-term unavailability of a hub if it recovers.
    /// </summary>
    private void ResetHubFailures(string hubAddress)
    {
        lock (_backoffLock)
        {
            _hubFailCount.Remove(hubAddress);
            _hubBackoffUntil.Remove(hubAddress);
        }
    }

    /// <summary>
    /// Helper method to update the status and raise the StatusChanged event. This is used to centralize the logic for changing the status, including logging and avoiding redundant updates. This ensures that all status changes are logged consistently and that the event is only raised when the status actually changes.
    /// </summary>
    private void SetStatus(RefreshListStatus newStatus)
    {
        if (Status == newStatus) return;
        Log.Information("Server list status: {Old} → {New}", Status, newStatus);
        Status = newStatus;
        StatusChanged?.Invoke(newStatus);
    }

    #endregion

    #region Implementations

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _refreshCancel?.Cancel(); } catch { }
        try { _refreshCancel?.Dispose(); } catch { }

        foreach (var sem in _hubThrottles.Values)
        {
            try { sem.Dispose(); } catch { }
        }
        _hubThrottles.Clear();

        try { _lock.Dispose(); } catch { }
    }

    #endregion
}
