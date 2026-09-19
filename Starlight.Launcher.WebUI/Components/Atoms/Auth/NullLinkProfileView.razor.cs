using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Serilog;
using Starlight.Launcher.WebUI.Bridge;
using Starlight.Launcher.WebUI.Localization;
using Starlight.Launcher.WebUI.Models.Auth;
using Starlight.Launcher.WebUI.Models.NullLink;

namespace Starlight.Launcher.WebUI.Components.Atoms.Auth;

public partial class NullLinkProfileView : LocalizedComponentBase
{
    private const int CollapsedTrackers = 4;

    private static readonly TimeSpan _cacheLifetime = TimeSpan.FromMinutes(1);
    private static readonly ConcurrentDictionary<Guid, (DateTime Fetched, NullLinkProfileResult Result)> _cache = new();

    [Parameter, EditorRequired] public LoggedInAccount Account { get; set; } = default!;
    [Parameter] public bool IsActive { get; set; }
    [Parameter] public bool Busy { get; set; }

    [Parameter] public Func<LoggedInAccount, Task>? OnMakeActive { get; set; }
    [Parameter] public Action<LoggedInAccount>? OnRelogin { get; set; }
    [Parameter] public Action<LoggedInAccount>? OnDiscordLink { get; set; }
    [Parameter] public Action<LoggedInAccount>? OnSteamLink { get; set; }

    [Inject] private IBridge _bridge { get; set; } = default!;

    private NullLinkProfileResult? _result;
    private bool _loading;
    private Guid? _loadedFor;
    private string? _loadedTokens;
    private CancellationTokenSource? _cts;
    private readonly HashSet<string> _expanded = [];

    private string Initial => string.IsNullOrEmpty(Account.Username) ? "?" : char.ToUpperInvariant(Account.Username[0]).ToString();

    protected override async Task OnParametersSetAsync()
    {
        // Reload when another account is shown or when this one gained a Discord/Steam session.
        var tokens = TokenFingerprint(Account);
        if (_loadedFor == Account.UserId && _loadedTokens == tokens)
            return;

        var tokensChanged = _loadedFor == Account.UserId;
        _loadedFor = Account.UserId;
        _loadedTokens = tokens;
        _expanded.Clear();
        await LoadAsync(force: tokensChanged);
    }

    private async Task LoadAsync(bool force = false)
    {
        var account = Account;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        if (!force && _cache.TryGetValue(account.UserId, out var cached) && DateTime.UtcNow - cached.Fetched < _cacheLifetime)
        {
            _result = cached.Result;
            _loading = false;
            return;
        }

        var cts = _cts = new CancellationTokenSource();

        // Keep showing the previous data of the same account while refreshing.
        if (_result?.Profile?.UserId != account.UserId)
            _result = null;
        _loading = true;
        StateHasChanged();

        NullLinkProfileResult result;
        try
        {
            result = await _bridge.GetNullLinkProfileAsync(account, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load NullLink profile of {UserId}", account.UserId);
            result = new(NullLinkProfileStatus.Unavailable);
        }

        if (result.Status is NullLinkProfileStatus.Loaded or NullLinkProfileStatus.NotLinked)
            _cache[account.UserId] = (DateTime.UtcNow, result);

        if (cts.IsCancellationRequested || account.UserId != Account.UserId)
            return;

        _result = result;
        _loading = false;
        StateHasChanged();
    }

    private Task MakeActive() => OnMakeActive?.Invoke(Account) ?? Task.CompletedTask;

    private void ToggleExpanded(string serverId)
    {
        if (!_expanded.Remove(serverId))
            _expanded.Add(serverId);
    }

    public override void Dispose()
    {
        base.Dispose();
        _cts?.Cancel();
        _cts?.Dispose();
    }

    private static string TokenFingerprint(LoggedInAccount account)
        => $"{account.LoginInfo.DiscordToken is not null}|{account.LoginInfo.SteamToken is not null}";

    private string FormatMinutes(double minutes)
    {
        var total = (long)Math.Round(minutes);
        return total < 60
            ? L.GetString("auth-profile-duration-m", ("minutes", total))
            : L.GetString("auth-profile-duration-hm", ("hours", (total / 60).ToString("N0", CultureInfo.CurrentCulture)), ("minutes", total % 60));
    }

    private static (string Name, double Minutes)? TopRole(NullLinkProfile profile)
    {
        var top = profile.Servers
            .SelectMany(x => x.Trackers)
            .Where(x => !x.Tracker.StartsWith("Admin", StringComparison.Ordinal))
            .GroupBy(x => x.Tracker)
            .Select(g => (Tracker: g.Key, Minutes: g.Sum(x => x.Minutes)))
            .OrderByDescending(x => x.Minutes)
            .FirstOrDefault();

        return top.Tracker is null ? null : (PrettifyTracker(top.Tracker), top.Minutes);
    }

    private static string ServerTitle(NullLinkServerPlayTime server)
    {
        if (!string.IsNullOrWhiteSpace(server.Title))
            return _colorTagRegex.Replace(server.Title, "$1");

        var dot = server.Id.IndexOf('.');
        return dot >= 0 ? server.Id[(dot + 1)..] : server.Id;
    }

    private static string PrettifyTracker(string tracker)
        => PrettifyId(tracker.StartsWith("Job", StringComparison.Ordinal) && tracker.Length > 3 ? tracker[3..] : tracker);

    private static string PrettifyId(string id)
    {
        var text = _camelBoundaryRegex.Replace(id.Replace('_', ' ').Replace('-', ' '), " ").Trim();
        return text.Length == 0 ? id : char.ToUpperInvariant(text[0]) + text[1..];
    }

    private static readonly Regex _colorTagRegex = new(@"\[color=[^\]]{1,32}\](.*?)\[/color\]",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex _camelBoundaryRegex = new(@"(?<=[a-z0-9])(?=[A-Z])", RegexOptions.Compiled);
}
