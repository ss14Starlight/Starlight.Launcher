using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;
using Starlight.Launcher.WebUI.Bridge;
using Starlight.Launcher.WebUI.Components.Atoms.Dialogs;
using Starlight.Launcher.WebUI.Localization;
using Starlight.Launcher.WebUI.Models.Journal;
using Starlight.Launcher.WebUI.Models.Replays;

namespace Starlight.Launcher.WebUI.Components.Pages;

public partial class Journal : LocalizedComponentBase, IDisposable
{
    private const int ReplaysTab = 2;
    private const int DaysShown = 14;

    [Inject] private IBridge _bridge { get; set; } = default!;
    [Inject] private IDialogService _dialogService { get; set; } = default!;
    [Inject] private ISnackbar _snackbar { get; set; } = default!;
    [Inject] private IJSRuntime _js { get; set; } = default!;

    private List<PlaySession> _sessions = [];
    private PlaySession? _active;
    private TimeSpan _totalTime;
    private int _roundsCount;
    private List<(string Name, TimeSpan Time)> _servers = [];
    private List<(DateTime Date, TimeSpan Time)> _days = [];
    private TimeSpan _maxDay;
    private YearSummary _year = new(DateTime.Now.Year, TimeSpan.Zero, 0, 0, 0, null, null, null, null);

    private string _roundsQuery = "";
    private string _replaysQuery = "";

    private IReadOnlyList<ReplayEntry>? _replays;
    private bool _replaysOk;
    private bool _replaysLoading;
    private ReplayEntry? _downloading;
    private double _downloadProgress;
    private readonly CancellationTokenSource _cts = new();

    protected override void OnInitialized()
    {
        base.OnInitialized();
        _bridge.PlayHistoryChanged += OnHistoryChanged;
        Rebuild();
        _ = LoadReplaysAsync(false);
    }

    private async void OnHistoryChanged()
    {
        try
        {
            await InvokeAsync(() =>
            {
                Rebuild();
                StateHasChanged();
            });
        }
        catch (ObjectDisposedException) { }
    }

    private void Rebuild()
    {
        _sessions = [.. _bridge.GetPlaySessions().OrderByDescending(s => s.StartUtc)];
        _active = _bridge.GetActivePlaySession();
        _totalTime = TimeSpan.FromTicks(_sessions.Sum(s => s.Duration.Ticks));
        _roundsCount = _sessions.Sum(s => s.Rounds.Count);

        _servers = [.. _sessions
            .GroupBy(s => s.Address, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Name: g.First().ServerName ?? g.Key, Time: TimeSpan.FromTicks(g.Sum(s => s.Duration.Ticks))))
            .OrderByDescending(x => x.Time)];

        var today = DateTime.Now.Date;
        _days = [.. Enumerable.Range(0, DaysShown)
            .Select(i => today.AddDays(i - DaysShown + 1))
            .Select(day => (day, TimeOn(day)))];
        _maxDay = _days.Count > 0 ? _days.Max(d => d.Time) : TimeSpan.Zero;

        _year = BuildYear(DateTime.Now.Year);
    }

    private TimeSpan TimeOn(DateTime day)
    {
        var start = day;
        var end = day.AddDays(1);
        var total = TimeSpan.Zero;
        foreach (var s in _sessions)
        {
            var from = s.StartUtc.ToLocalTime();
            var to = (s.EndUtc ?? DateTime.UtcNow).ToLocalTime();
            var overlap = (to < end ? to : end) - (from > start ? from : start);
            if (overlap > TimeSpan.Zero)
                total += overlap;
        }
        return total;
    }

    private YearSummary BuildYear(int year)
    {
        var sessions = _sessions.Where(s => s.StartUtc.ToLocalTime().Year == year).ToList();
        if (sessions.Count == 0)
            return new YearSummary(year, TimeSpan.Zero, 0, 0, 0, null, null, null, null);

        var top = sessions
            .GroupBy(s => s.Address, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Name: g.First().ServerName ?? g.Key, Time: TimeSpan.FromTicks(g.Sum(s => s.Duration.Ticks))))
            .MaxBy(x => x.Time);

        var topMap = sessions
            .SelectMany(s => s.Rounds)
            .Where(r => r.Map is not null)
            .GroupBy(r => r.Map!)
            .Select(g => (Name: g.Key, Count: g.Count()))
            .OrderByDescending(x => x.Count)
            .Cast<(string Name, int Count)?>()
            .FirstOrDefault();

        var favoriteHour = sessions
            .GroupBy(s => s.StartUtc.ToLocalTime().Hour)
            .OrderByDescending(g => g.Count())
            .Select(g => (int?)g.Key)
            .FirstOrDefault();

        return new YearSummary(
            year,
            TimeSpan.FromTicks(sessions.Sum(s => s.Duration.Ticks)),
            sessions.Count,
            sessions.Sum(s => s.Rounds.Count),
            sessions.Select(s => s.Address).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            top,
            topMap,
            sessions.MaxBy(s => s.Duration),
            favoriteHour);
    }

    private List<PlaySession> FilteredSessions
    {
        get
        {
            var q = _roundsQuery?.Trim();
            if (string.IsNullOrEmpty(q))
                return _sessions;

            var id = q.TrimStart('#');
            return [.. _sessions.Where(s =>
                (s.ServerName?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                || s.Address.Contains(q, StringComparison.OrdinalIgnoreCase)
                || s.Rounds.Any(r =>
                    (r.RoundId?.ToString(CultureInfo.InvariantCulture) == id)
                    || (r.Map?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (r.Preset?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)))];
        }
    }

    private List<ReplayEntry> FilteredReplays
    {
        get
        {
            if (_replays is null)
                return [];

            var q = _replaysQuery?.Trim();
            if (string.IsNullOrEmpty(q))
                return [.. _replays];

            var id = q.TrimStart('#');
            return [.. _replays.Where(r =>
                r.Round.ToString(CultureInfo.InvariantCulture) == id
                || (r.Server?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                || r.Date.ToString("d").Contains(q, StringComparison.OrdinalIgnoreCase))];
        }
    }

    private void OnTabChanged(int index)
    {
        if (index == ReplaysTab && _replays is null && !_replaysLoading)
            _ = LoadReplaysAsync(false);
    }

    private async Task LoadReplaysAsync(bool force)
    {
        if (_replaysLoading || (_replays is not null && !force))
            return;

        _replaysLoading = true;
        StateHasChanged();
        try
        {
            var result = await _bridge.GetReplaysAsync(_cts.Token);
            _replays = result.Replays;
            _replaysOk = result.Ok;
        }
        catch (OperationCanceledException) { return; }
        finally
        {
            _replaysLoading = false;
        }

        try { await InvokeAsync(StateHasChanged); }
        catch (ObjectDisposedException) { }
    }

    private ReplayEntry? FindReplay(PlayedRound round)
    {
        if (_replays is null || round.RoundId is not { } id)
            return null;

        var seen = round.SeenUtc.ToLocalTime();
        return _replays.FirstOrDefault(r => r.Round == id && Math.Abs((r.Date - seen).TotalHours) <= 24);
    }

    private bool PlayedIn(ReplayEntry replay)
        => _sessions.Any(s => s.Rounds.Any(r => r.RoundId == replay.Round && Math.Abs((replay.Date - r.SeenUtc.ToLocalTime()).TotalHours) <= 24));

    private async Task WatchReplay(ReplayEntry replay)
    {
        if (_downloading is not null)
            return;

        _downloading = replay;
        _downloadProgress = 0;
        StateHasChanged();

        var progress = new Progress<double>(p =>
        {
            _downloadProgress = p;
            _ = InvokeAsync(StateHasChanged);
        });

        try
        {
            var file = await _bridge.DownloadReplayAsync(replay, progress, _cts.Token);

            var parameters = new DialogParameters<ConnectingDialog>
            {
                { x => x.ContentBundle, file },
                { x => x.Title, file.FileName }
            };

            _ = await _dialogService.ShowAsync<ConnectingDialog>(null, parameters, new DialogOptions
            {
                BackdropClick = false,
                CloseOnEscapeKey = false,
                CloseButton = false,
                MaxWidth = MaxWidth.ExtraSmall,
                FullWidth = true
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            _ = _snackbar.Add(L["journal-replay-download-failed"], Severity.Error);
        }
        finally
        {
            _downloading = null;
            try { await InvokeAsync(StateHasChanged); }
            catch (ObjectDisposedException) { }
        }
    }

    private async Task ClearHistory()
    {
        var confirmed = await _dialogService.ShowMessageBoxAsync(
            L["journal-clear-history"],
            L["journal-clear-history-confirm"],
            yesText: L["journal-clear-history"],
            cancelText: L["general-cancel"]);

        if (confirmed == true)
            _bridge.ClearPlayHistory();
    }

    private async Task CopyAsync(string text)
    {
        try
        {
            await _js.InvokeVoidAsync("navigator.clipboard.writeText", text);
            _ = _snackbar.Add(L["journal-round-copied"], Severity.Success);
        }
        catch (JSException) { }
    }

    private string FormatDuration(TimeSpan time)
        => time.TotalHours >= 1
            ? L.GetString("journal-duration-hours", ("hours", (int)time.TotalHours), ("minutes", time.Minutes))
            : L.GetString("journal-duration-minutes", ("minutes", Math.Max(0, (int)time.TotalMinutes)));

    private static string FormatHours(TimeSpan time)
        => time.TotalHours.ToString(time.TotalHours >= 10 ? "0" : "0.#", CultureInfo.CurrentCulture);

    private static string FormatSize(long bytes)
        => bytes >= 1024 * 1024
            ? $"{bytes / 1024d / 1024d:0.#} MB"
            : $"{bytes / 1024d:0} KB";

    private static string BarHeight(TimeSpan value, TimeSpan max)
        => $"height: {Percent(value, max)}%";

    private static string BarWidth(TimeSpan value, TimeSpan max)
        => $"width: {Percent(value, max)}%";

    private static string Percent(TimeSpan value, TimeSpan max)
        => (max <= TimeSpan.Zero ? 0 : Math.Max(value > TimeSpan.Zero ? 3 : 0, value / max * 100)).ToString("0.#", CultureInfo.InvariantCulture);

    public override void Dispose()
    {
        base.Dispose();
        _bridge.PlayHistoryChanged -= OnHistoryChanged;
        _cts.Cancel();
        _cts.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed record YearSummary(
        int Year,
        TimeSpan Time,
        int Sessions,
        int Rounds,
        int Servers,
        (string Name, TimeSpan Time)? TopServer,
        (string Name, int Count)? TopMap,
        PlaySession? Longest,
        int? FavoriteHour);
}
