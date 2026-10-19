using Robust.Launcher.Api.Models.ServerStatus;
using Serilog;
using Starlight.Launcher.Services.DeepLink;
using Starlight.Launcher.Services.Settings;
using Starlight.Launcher.WebUI.Localization;
using Starlight.Launcher.WebUI.Models.Journal;

namespace Starlight.Launcher.Services.Journal;

public sealed class ServerMonitor(
    SettingsService settings,
    JournalService journal,
    ServerStatusCache statusCache,
    HttpClient http,
    ILocalizationManager loc,
    LauncherCommands commands)
{
    private static readonly TimeSpan _tick = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan _favoritePollInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan _activitySampleInterval = TimeSpan.FromMinutes(5);

    private const int MaxParallelPolls = 6;

    private readonly Dictionary<string, Snapshot> _last = [with(StringComparer.OrdinalIgnoreCase)];
    private readonly Dictionary<string, DateTime> _lastPoll = [with(StringComparer.OrdinalIgnoreCase)];
    private readonly CancellationTokenSource _cts = new();

    public event Action<LauncherNotification>? NotificationRaised;

    public void Start() => _ = Task.Run(() => RunAsync(_cts.Token));

    public void Stop() => _cts.Cancel();

    public void Notify(LauncherNotification notification)
    {
        NotificationRaised?.Invoke(notification);

        if (settings.GetSettings().DesktopNotifications)
            DesktopNotifier.Show(notification.Title, notification.Message);
    }

    private async Task RunAsync(CancellationToken cancel)
    {
        // Let the hub list and the UI settle first.
        try { await Task.Delay(TimeSpan.FromSeconds(10), cancel); }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(_tick);
        do
        {
            try
            {
                await TickAsync(cancel);
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                Log.Error(e, "Server monitor tick failed");
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

    private async Task TickAsync(CancellationToken cancel)
    {
        var appSettings = settings.GetSettings();
        var watches = journal.GetWatches().ToDictionary(x => x.Address, StringComparer.OrdinalIgnoreCase);
        var favoriteList = settings.GetFavorites();
        var hubs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in favoriteList.Where(f => !string.IsNullOrEmpty(f.HubAddress)))
            hubs[f.Address] = f.HubAddress;

        foreach (var w in watches.Values.Where(w => !string.IsNullOrEmpty(w.HubAddress)))
            hubs[w.Address] = w.HubAddress!;

        var favorites = appSettings.TrackServerActivity
            ? favoriteList.Select(x => x.Address)
            : [];

        var now = DateTime.UtcNow;
        var due = watches.Keys
            .Concat(favorites)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(address => watches.ContainsKey(address)
                || !_lastPoll.TryGetValue(address, out var last)
                || now - last >= _favoritePollInterval)
            .ToList();

        foreach (var stale in _last.Keys.Where(k => !due.Contains(k, StringComparer.OrdinalIgnoreCase) && !watches.ContainsKey(k)).ToList())
        {
            if (!_lastPoll.TryGetValue(stale, out var last) || now - last > _favoritePollInterval * 2)
            {
                _ = _last.Remove(stale);
                _ = _lastPoll.Remove(stale);
            }
        }

        await Parallel.ForEachAsync(due, new ParallelOptions { MaxDegreeOfParallelism = MaxParallelPolls, CancellationToken = cancel }, async (address, ct) =>
        {
            var data = statusCache.GetStatusFor(address, hubs.GetValueOrDefault(address));
            await statusCache.UpdateStatusFor(data, http, ct);
            lock (_lastPoll)
                _lastPoll[address] = DateTime.UtcNow;
        });

        foreach (var address in due)
        {
            var data = statusCache.GetStatusFor(address);
            var current = Snapshot.From(data);

            if (current.Online
                && appSettings.TrackServerActivity
                && (journal.LastActivitySample(address) is not { } sampled || now - sampled >= _activitySampleInterval - TimeSpan.FromSeconds(5)))
            {
                journal.AddActivitySample(address, data.PlayerCount);
            }

            var previous = _last.GetValueOrDefault(address);
            _last[address] = current;

            if (watches.TryGetValue(address, out var watch))
                await Evaluate(watch, previous, current, data);
        }
    }

    private async Task Evaluate(ServerWatch watch, Snapshot? previous, Snapshot current, ServerStatusData data)
    {
        var server = data.Name ?? watch.Name ?? watch.Address;

        if (watch.AutoConnect && current.Online && !current.Full && journal.ActiveSession is null)
        {
            journal.SetWatch(watch with { AutoConnect = false });
            Notify(new LauncherNotification(
                LauncherNotificationKind.AutoConnect,
                loc.GetString("notify-autoconnect-title", ("server", server)),
                loc.GetString("notify-autoconnect-body"),
                watch.Address));
            await commands.QueueMessage(new LauncherActivationMessage(LauncherActivationKind.Connect, watch.Address, "auto-connect"));
            return;
        }

        if (previous is null || !current.Online)
            return;

        if (watch.OnLobby && previous.RoundStatus != GameRoundStatus.InLobby && current.RoundStatus == GameRoundStatus.InLobby)
        {
            Notify(new LauncherNotification(
                LauncherNotificationKind.Lobby,
                loc.GetString("notify-lobby-title", ("server", server)),
                loc.GetString("notify-lobby-body"),
                watch.Address));
        }

        if (watch.OnNewRound && previous.Online && IsNewRound(previous, current))
        {
            Notify(new LauncherNotification(
                LauncherNotificationKind.NewRound,
                loc.GetString("notify-new-round-title", ("server", server)),
                data.Map is { } map
                    ? loc.GetString("notify-new-round-body-map", ("map", map))
                    : loc.GetString("notify-new-round-body"),
                watch.Address));
        }

        if (watch.PlayersAtLeast is > 0 and var threshold && previous.Players < threshold && current.Players >= threshold)
        {
            Notify(new LauncherNotification(
                LauncherNotificationKind.Players,
                loc.GetString("notify-players-title", ("server", server)),
                loc.GetString("notify-players-body", ("players", current.Players), ("threshold", threshold)),
                watch.Address));
        }

        if (watch.OnSlotFree && (previous.Full || !previous.Online) && !current.Full)
        {
            Notify(new LauncherNotification(
                LauncherNotificationKind.SlotFree,
                loc.GetString("notify-slot-title", ("server", server)),
                loc.GetString("notify-slot-body", ("players", current.Players), ("max", data.SoftMaxPlayerCount)),
                watch.Address));
        }
    }

    private static bool IsNewRound(Snapshot previous, Snapshot current)
        => current.RoundStatus == GameRoundStatus.InRound
            && (previous.RoundStatus != GameRoundStatus.InRound
                || (current.RoundId is { } id
                    && previous.RoundId is { } prevId
                    && id != prevId)
                || (current.RoundStart is { } start
                    && previous.RoundStart is { } prevStart
                    && start != prevStart));

    private sealed record Snapshot(bool Online, int Players, bool Full, GameRoundStatus RoundStatus, int? RoundId, DateTime? RoundStart)
    {
        public static Snapshot From(ServerStatusData data) => new(
            data.Status == ServerStatusCode.Online,
            data.PlayerCount,
            data.SoftMaxPlayerCount > 0 && data.PlayerCount >= data.SoftMaxPlayerCount,
            data.RoundStatus,
            data.RoundId,
            data.RoundStartTime);
    }
}
