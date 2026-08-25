using System.Net.Http.Json;
using System.Text.Json.Serialization;
using DiscordRPC;
using Microsoft.Extensions.Logging;
using Starlight.Launcher.Services.Settings;
using Starlight.Launcher.WebUI.Models.DiscordRichPresence;

namespace Starlight.Launcher.Services.Discord;

public readonly record struct PresenceContext(
    PresenceState State,
    DateTime StartedAt,
    string? ServerName = null,
    int Players = 0,
    int MaxPlayers = 0,
    int ProgressPercent = -1);

public sealed class DiscordRichPresence : IPresenceController, IDisposable
{
    private const int MaxFieldLength = 120;

    private static readonly TimeSpan _minUpdateInterval = TimeSpan.FromSeconds(15);

    private readonly int[] _priorityByState;

    private static readonly Button[] _presenceButtons =
    [
        new() { Label = "Download", Url = "https://starlight.network/download" }
    ];

    private static readonly Assets[] _assetsByState = BuildAssets();

    private readonly SettingsService _settingsService;
    private readonly HttpClient _http;
    private readonly ILogger<DiscordRichPresence> _logger;
    private readonly IDisposable _settingsSubscription;
    private readonly IDisposable _buttonsSubscription;
    private readonly IDisposable _statesSubscription;
    private readonly Timer _flushTimer;
    private readonly Lock _gate = new();
    private readonly DateTime _launcherStartedAt = DateTime.UtcNow;
    private readonly StateEntry[] _entries;

    private DiscordRpcClient? _client;
    private PresenceState? _navigation;
    private string _applicationId = "";
    private bool _started;
    private bool _showButtons = true;

    private PresenceContext? _current;
    private PresenceContext? _pending;
    private bool _hasPending;
    private DateTime _lastSentAt = DateTime.MinValue;
    private bool _disposed;

    public PresenceState? ResolvedState
    {
        get { lock (_gate) return _current?.State; }
    }

    public bool IsActive
    {
        get { lock (_gate) return _client is not null; }
    }

    public DiscordRichPresence(SettingsService settingsService, HttpClient http, ILogger<DiscordRichPresence> logger)
    {
        _settingsService = settingsService;
        _http = http;
        _logger = logger;

        _priorityByState = new int[Enum.GetValues<PresenceState>().Length];
        _entries = new StateEntry[_priorityByState.Length];
        for (var i = 0; i < _entries.Length; i++)
            _entries[i] = new StateEntry();

        _flushTimer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);

        _settingsSubscription = settingsService.Subscribe(
            s => (Hidden: s.HidePresence, ApplicationId: s.DiscordRichPresenceID),
            OnPresenceSettingsChanged);

        _buttonsSubscription = settingsService.Subscribe(
            s => s.ShowPresenceButtons,
            OnButtonsSettingsChanged,
            fireImmediately: true);

        _statesSubscription = settingsService.Subscribe(
            s => s.PresenceStates,
            OnPresenceStatesChanged,
            fireImmediately: true,
            PresenceStates.ListComparer);
    }

    public void Initialize()
    {
        lock (_gate)
        {
            if (_disposed || _started)
                return;

            _started = true;
        }

        var settings = _settingsService.GetSettings();
        ApplySettings(settings.HidePresence, settings.DiscordRichPresenceID);
    }

    public void SetNavigation(PresenceState state)
    {
        lock (_gate)
        {
            if (_navigation == state)
                return;

            if (_navigation is { } previous)
            {
                var old = _entries[(int)previous];
                old.NavActive = false;

                if (!old.IsActive)
                    old.Reset();
            }

            _navigation = state;

            var entry = _entries[(int)state];

            if (!entry.IsActive)
                entry.ActivatedAt = DateTime.UtcNow;

            entry.NavActive = true;
            Resolve();
        }
    }

    public IPresenceScope Activate(PresenceState state)
    {
        lock (_gate)
        {
            var entry = _entries[(int)state];

            if (!entry.IsActive)
                entry.ActivatedAt = DateTime.UtcNow;

            entry.ScopeCount++;
            Resolve();
        }

        return new PresenceScope(this, state);
    }

    public IPresenceScope Activate(PresenceState state, ServerPresence server)
    {
        var scope = Activate(state);
        scope.SetServer(server);
        return scope;
    }

    public void SetActive(PresenceState state, bool active)
    {
        lock (_gate)
        {
            var entry = _entries[(int)state];
            if (entry.ManualActive == active)
                return;

            if (active && !entry.IsActive)
                entry.ActivatedAt = DateTime.UtcNow;

            entry.ManualActive = active;

            if (!entry.IsActive)
                entry.Reset();

            Resolve();
        }
    }

    public void SetServer(PresenceState state, ServerPresence server)
    {
        lock (_gate)
        {
            var entry = _entries[(int)state];
            if (entry.Server == server)
                return;

            entry.Server = server;
            Resolve();
        }
    }

    public void SetProgress(PresenceState state, int percent)
    {
        lock (_gate)
        {
            var entry = _entries[(int)state];
            if (entry.ProgressPercent == percent)
                return;

            entry.ProgressPercent = percent;
            Resolve();
        }
    }

    internal void ExitScope(PresenceState state)
    {
        lock (_gate)
        {
            var entry = _entries[(int)state];
            if (entry.ScopeCount > 0)
                entry.ScopeCount--;

            if (!entry.IsActive)
                entry.Reset();

            Resolve();
        }
    }

    public ServerSession BeginServerSession(Uri statusAddress, string? fallbackName = null) =>
        new(this, _http, _logger, statusAddress, fallbackName);

    public ServerSession BeginLocalSession(string displayName) =>
        new(this, _http, _logger, statusAddress: null, displayName);

    private void Resolve()
    {
        StateEntry? winner = null;
        var winnerState = PresenceState.Idle;
        var bestPriority = int.MaxValue;

        for (var i = 0; i < _entries.Length; i++)
        {
            var entry = _entries[i];
            if (!entry.Enabled || !entry.IsActive)
                continue;

            var priority = _priorityByState[i];
            if (priority >= bestPriority)
                continue;

            winner = entry;
            winnerState = (PresenceState)i;
            bestPriority = priority;
        }

        if (winner is null)
        {
            Push(_entries[(int)PresenceState.Idle].Enabled
                ? new PresenceContext(PresenceState.Idle, _launcherStartedAt)
                : null);
            return;
        }

        Push(new PresenceContext(
            winnerState,
            UsesOwnTimer(winnerState) ? winner.ActivatedAt : _launcherStartedAt,
            winner.Server.Name,
            winner.Server.Players,
            winner.Server.MaxPlayers,
            winner.ProgressPercent));
    }

    private static bool UsesOwnTimer(PresenceState state) => state
        is PresenceState.InGame
        or PresenceState.DownloadingContent
        or PresenceState.UpdatingLauncher;

    private void Push(PresenceContext? ctx)
    {
        if (_disposed || ctx == _current)
            return;

        _current = ctx;

        if (_client is null)
            return;

        var wait = _minUpdateInterval - (DateTime.UtcNow - _lastSentAt);
        if (wait > TimeSpan.Zero)
        {
            _pending = ctx;
            _hasPending = true;
            _ = _flushTimer.Change(wait, Timeout.InfiniteTimeSpan);
            return;
        }

        Send(ctx);
    }

    private void Flush()
    {
        lock (_gate)
        {
            if (_disposed || _client is null || !_hasPending)
                return;

            var ctx = _pending;
            _pending = null;
            _hasPending = false;
            Send(ctx);
        }
    }

    private void Send(PresenceContext? ctx)
    {
        var client = _client;
        if (client is null)
            return;

        _lastSentAt = DateTime.UtcNow;

        try
        {
            if (ctx is { } value)
                client.SetPresence(Build(value));
            else
                client.ClearPresence();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send Discord presence");
        }
    }

    private RichPresence Build(in PresenceContext ctx)
    {
        var presence = new RichPresence
        {
            Details = Truncate(BuildDetails(ctx)),
            State = Truncate(BuildStateText(ctx)),
            Assets = _assetsByState[(int)ctx.State],
            Timestamps = new Timestamps(ctx.StartedAt),
            Buttons = _showButtons ? _presenceButtons : null
        };

        if (ctx is { Players: > 0, MaxPlayers: > 0, ServerName: { Length: > 0 } name })
        {
            presence.Party = new Party
            {
                ID = "srv-" + (uint)StringComparer.Ordinal.GetHashCode(name),
                Size = ctx.Players,
                Max = ctx.MaxPlayers
            };
        }

        return presence;
    }

    private static string? BuildDetails(in PresenceContext ctx)
    {
        if (string.IsNullOrWhiteSpace(ctx.ServerName))
            return null;

        return ctx.State switch
        {
            PresenceState.ViewingServer => $"Viewing {ctx.ServerName}",
            PresenceState.LaunchingGame => $"Connecting to {ctx.ServerName}",
            PresenceState.Reconnecting => $"Reconnecting to {ctx.ServerName}",
            _ => ctx.ServerName
        };
    }

    /// <summary>
    /// Untranslated for now,
    /// because Discord Rich Presence is a global feature and we don't want to share user's language preference with everyone.
    /// </summary>
    private static string BuildStateText(in PresenceContext ctx) => ctx.State switch
    {
        PresenceState.Idle => "In the main menu",
        PresenceState.ManagesLogins => "Manages accounts",
        PresenceState.SearchingServers => "Searching for a server",
        PresenceState.ViewingServer => ctx is { Players: > 0, MaxPlayers: > 0 }
            ? $"Viewing Server - {ctx.Players}/{ctx.MaxPlayers}"
            : "Viewing Server",
        PresenceState.SettingUp => "Setting up",
        PresenceState.UpdatingLauncher => "Updating launcher",
        PresenceState.DownloadingContent => ctx.ProgressPercent >= 0
            ? $"Downloading content - {ctx.ProgressPercent}%"
            : "Downloading content",
        PresenceState.LaunchingGame => "Launching game",
        PresenceState.InGame => ctx is { Players: > 0, MaxPlayers: > 0 }
            ? $"In Game - {ctx.Players}/{ctx.MaxPlayers}"
            : "Playing Space Station 14",
        PresenceState.Reconnecting => "Reconnecting",
        _ => "Space Station 14"
    };

    private static Assets[] BuildAssets()
    {
        var states = Enum.GetValues<PresenceState>();
        var assets = new Assets[states.Length];

        foreach (var state in states)
        {
            assets[(int)state] = new Assets
            {
                LargeImageKey = "launcher_icon",
                LargeImageText = "Starlight Launcher",
                SmallImageKey = state switch
                {
                    PresenceState.SearchingServers or PresenceState.ViewingServer => "icon_search",
                    PresenceState.SettingUp => "icon_settings",
                    PresenceState.DownloadingContent or PresenceState.UpdatingLauncher => "icon_download",
                    PresenceState.LaunchingGame or PresenceState.Reconnecting => "icon_rocket",
                    PresenceState.InGame => "icon_play",
                    PresenceState.ManagesLogins => "icon_account",
                    _ => null
                },
                SmallImageText = BuildStateText(new PresenceContext(state, DateTime.UtcNow))
            };
        }

        return assets;
    }

    private static string? Truncate(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;

        return value.Length <= MaxFieldLength
            ? value
            : string.Concat(value.AsSpan(0, MaxFieldLength - 1), "...");
    }

    private void OnPresenceSettingsChanged((bool Hidden, string ApplicationId) settings) =>
        ApplySettings(settings.Hidden, settings.ApplicationId);

    private void OnButtonsSettingsChanged(bool showButtons)
    {
        lock (_gate)
        {
            if (_showButtons == showButtons)
                return;

            _showButtons = showButtons;

            var ctx = _current;
            _current = null;
            Push(ctx);
        }
    }

    private void OnPresenceStatesChanged(List<PresenceStateOption>? options)
    {
        var normalized = PresenceStates.Normalize(options);

        lock (_gate)
        {
            for (var i = 0; i < normalized.Count; i++)
            {
                var option = normalized[i];
                _priorityByState[(int)option.State] = i;
                _entries[(int)option.State].Enabled = option.Enabled;
            }

            Resolve();
        }
    }

    private void ApplySettings(bool hidden, string applicationId)
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            if (!_started)
            {
                _applicationId = applicationId;
                return;
            }

            if (hidden)
            {
                if (_client is null)
                    return;
            }
            else if (_client is not null && _applicationId == applicationId)
            {
                return;
            }
        }

        StopClient();

        if (hidden)
        {
            _logger.LogInformation("Discord Rich Presence disabled in settings");
            return;
        }

        StartClient(applicationId);
    }

    private void StartClient(string applicationId)
    {
        if (string.IsNullOrWhiteSpace(applicationId))
        {
            _logger.LogWarning("Discord Rich Presence is enabled, but the Application ID has not been set");
            return;
        }

        DiscordRpcClient client;

        lock (_gate)
        {
            if (_disposed || !_started || _client is not null)
                return;

            client = new DiscordRpcClient(applicationId) { SkipIdenticalPresence = true };

            client.OnReady += (_, e) =>
                _logger.LogInformation("Discord RPC connected as {User}", e.User.Username);
            client.OnError += (_, e) =>
                _logger.LogError("Discord RPC error: {Message}", e.Message);
            client.OnConnectionFailed += (_, _) =>
                _logger.LogDebug("Failed to connect (is Discord running?)");
            client.OnClose += (_, e) =>
                _logger.LogDebug("Connection closed ({Reason})", e.Reason);

            _client = client;
            _applicationId = applicationId;

            _lastSentAt = DateTime.MinValue;
            _pending = null;
            _hasPending = false;
        }

        if (!client.Initialize())
        {
            _logger.LogWarning("Failed to initialize Discord RPC - status will not be shown");
            return;
        }

        lock (_gate)
        {
            if (ReferenceEquals(_client, client))
                Send(_current);
        }
    }

    private void StopClient()
    {
        DiscordRpcClient? client;

        lock (_gate)
        {
            client = _client;
            _client = null;
            _pending = null;
            _hasPending = false;
            _ = _flushTimer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        if (client is null)
            return;

        try
        {
            if (client.IsInitialized)
                client.ClearPresence();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to clear Discord presence");
        }

        client.Dispose();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
        }

        _settingsSubscription.Dispose();
        _buttonsSubscription.Dispose();
        _statesSubscription.Dispose();
        _flushTimer.Dispose();
        StopClient();
    }

    private sealed class StateEntry
    {
        public bool Enabled = true;
        public bool NavActive;
        public bool ManualActive;
        public int ScopeCount;
        public DateTime ActivatedAt;
        public ServerPresence Server;
        public int ProgressPercent = -1;

        public bool IsActive => NavActive || ManualActive || ScopeCount > 0;

        public void Reset()
        {
            Server = default;
            ProgressPercent = -1;
        }
    }

    public sealed class PresenceScope : IPresenceScope
    {
        private DiscordRichPresence? _owner;

        public PresenceState State { get; }

        internal PresenceScope(DiscordRichPresence owner, PresenceState state)
        {
            _owner = owner;
            State = state;
        }

        public void SetServer(ServerPresence server) => _owner?.SetServer(State, server);

        public void SetProgress(int percent) => _owner?.SetProgress(State, percent);

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.ExitScope(State);
    }

    public sealed class ServerSession : IDisposable
    {
        private static readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(30);

        private readonly DiscordRichPresence _owner;
        private readonly HttpClient _http;
        private readonly ILogger _logger;
        private readonly Uri? _statusAddress;
        private readonly CancellationTokenSource _cts = new();
        private readonly Lock _sessionGate = new();

        private IPresenceScope? _scope;
        private ServerPresence _server;
        private int _disposed;

        internal ServerSession(
            DiscordRichPresence owner,
            HttpClient http,
            ILogger logger,
            Uri? statusAddress,
            string? fallbackName)
        {
            _owner = owner;
            _http = http;
            _logger = logger;
            _statusAddress = statusAddress;
            _server = new ServerPresence(string.IsNullOrWhiteSpace(fallbackName) ? null : fallbackName);

            SetState(PresenceState.LaunchingGame);

            if (statusAddress is not null)
                _ = PollAsync(_cts.Token);
        }

        public void SetState(PresenceState state)
        {
            lock (_sessionGate)
            {
                if (_disposed != 0 || _scope?.State == state)
                    return;

                var previous = _scope;

                var scope = _owner.Activate(state);
                scope.SetServer(_server);
                _scope = scope;

                previous?.Dispose();
            }
        }

        public void SetProgress(int percent)
        {
            lock (_sessionGate)
                _scope?.SetProgress(percent);
        }

        public void SetServer(ServerPresence server)
        {
            lock (_sessionGate)
            {
                _server = server;
                _scope?.SetServer(server);
            }
        }

        private async Task PollAsync(CancellationToken cancel)
        {
            using var timer = new PeriodicTimer(_pollInterval);

            do
            {
                try
                {
                    var status = await _http.GetFromJsonAsync<StatusDto>(_statusAddress, cancel);

                    if (status is not null)
                    {
                        SetServer(new ServerPresence(
                            string.IsNullOrWhiteSpace(status.Name) ? _server.Name : status.Name,
                            status.Players,
                            status.SoftMaxPlayers > 0 ? status.SoftMaxPlayers : status.MaxPlayers));
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
#if DEBUG
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Can't get /status for presence");
                }
#elif RELEASE
                catch
                {
                }
#endif
            }
            while (await SafeWaitAsync(timer, cancel));
        }

        private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken cancel)
        {
            try
            {
                return await timer.WaitForNextTickAsync(cancel);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            _cts.Cancel();
            _cts.Dispose();

            lock (_sessionGate)
            {
                _scope?.Dispose();
                _scope = null;
            }
        }

        private sealed record StatusDto(
            [property: JsonPropertyName("name")] string? Name,
            [property: JsonPropertyName("players")] int Players,
            [property: JsonPropertyName("soft_max_players")] int SoftMaxPlayers,
            [property: JsonPropertyName("max_players")] int MaxPlayers);
    }
}
