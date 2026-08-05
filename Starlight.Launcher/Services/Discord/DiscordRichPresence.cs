using System.Net.Http.Json;
using System.Text.Json.Serialization;
using DiscordRPC;
using Microsoft.Extensions.Logging;
using Serilog;
using Starlight.Launcher.Services.Settings;
using Starlight.Launcher.WebUI.Models.DiscordRichPresence;

namespace Starlight.Launcher.Services.Discord;

public readonly record struct ServerPresence(string? Name = null, int Players = 0, int MaxPlayers = 0);

public readonly record struct PresenceContext(
    PresenceState State,
    string? ServerName = null,
    int Players = 0,
    int MaxPlayers = 0,
    int ProgressPercent = -1);

public sealed class DiscordRichPresence : IDisposable
{
    private const int MaxFieldLength = 120;

    private static readonly TimeSpan _minUpdateInterval = TimeSpan.FromSeconds(15);

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
    private readonly Timer? _flushTimer;
    private readonly Lock _gate = new();
    private readonly DateTime _launcherStartedAt = DateTime.UtcNow;

    private DiscordRpcClient? _client;
    private string _applicationId = "";
    private bool _started;
    private bool _showButtons = true;

    private PresenceState _ambient = PresenceState.Idle;
    private ServerSession? _session;

    private PresenceContext _current = new(PresenceState.Idle);
    private PresenceContext? _pending;
    private DateTime _stateEnteredAt = DateTime.UtcNow;
    private DateTime _lastSentAt = DateTime.MinValue;
    private bool _disposed;

    public PresenceState CurrentState
    {
        get { lock (_gate) return _current.State; }
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
        _flushTimer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);

        _settingsSubscription = settingsService.Subscribe(
            s => (Hidden: s.HidePresence, ApplicationId: s.DiscordRichPresenceID),
            OnPresenceSettingsChanged);

        _buttonsSubscription = settingsService.Subscribe( s => s.ShowPresenceButtons, OnButtonsSettingsChanged, true);
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

    public void Apply(PresenceState state)
    {
        lock (_gate)
        {
            _ambient = state;

            if (_session is not null)
                return;

            Rebuild();
        }
    }

    public void ApplyViewingServer(string name, int players = 0, int maxPlayers = 0)
    {
        lock (_gate)
        {
            _ambient = PresenceState.ViewingServer;

            if (_session is not null)
                return;

            Push(new PresenceContext(PresenceState.ViewingServer, name, players, maxPlayers));
        }
    }

    public ServerSession BeginServerSession(Uri statusAddress, string? fallbackName = null)
    {
        var session = new ServerSession(this, statusAddress, fallbackName);
        AttachSession(session);
        return session;
    }

    public ServerSession BeginLocalSession(string displayName)
    {
        var session = new ServerSession(this, statusAddress: null, displayName);
        AttachSession(session);
        return session;
    }

    private void AttachSession(ServerSession session)
    {
        ServerSession? previous;

        lock (_gate)
        {
            previous = _session;
            _session = session;
        }

        previous?.Dispose();
        session.Start();
    }

    private void DetachSession(ServerSession session)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_session, session))
                return;

            _session = null;
            Rebuild();
        }
    }

    private void Rebuild()
    {
        if (_session is { } session)
        {
            var server = session.Server;
            Push(new PresenceContext(
                session.State,
                server.Name,
                server.Players,
                server.MaxPlayers,
                session.ProgressPercent));
            return;
        }

        Push(new PresenceContext(_ambient));
    }

    private void Push(PresenceContext ctx)
    {
        if (_disposed || ctx == _current)
            return;

        if (ctx.State != _current.State || ctx.ServerName != _current.ServerName)
            _stateEnteredAt = DateTime.UtcNow;

        _current = ctx;

        if (_client is null)
            return;

        var wait = _minUpdateInterval - (DateTime.UtcNow - _lastSentAt);
        if (wait > TimeSpan.Zero)
        {
            _pending = ctx;
            _ = _flushTimer?.Change(wait, Timeout.InfiniteTimeSpan);
            return;
        }

        Send(ctx);
    }

    private void ResendCurrent()
    {
        lock (_gate)
        {
            if (_disposed || _client is null)
                return;

            Send(_current);
        }
    }

    private void Flush()
    {
        lock (_gate)
        {
            if (_disposed || _client is null || _pending is not { } ctx)
                return;

            _pending = null;
            Send(ctx);
        }
    }

    private void Send(in PresenceContext ctx)
    {
        var client = _client;
        if (client is null)
            return;

        _lastSentAt = DateTime.UtcNow;

        try
        {
            _client!.SetPresence(Build(ctx));
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
            Timestamps = new Timestamps(ctx.State is PresenceState.InGame or PresenceState.DownloadingContent
                ? _stateEnteredAt
                : _launcherStartedAt),
            Buttons = _showButtons ? _presenceButtons : []
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
            ? $"Viewing Server — {ctx.Players}/{ctx.MaxPlayers}"
            : "Viewing Server",
        PresenceState.SettingUp => "Setting up",
        PresenceState.UpdatingLauncher => "Updating launcher",
        PresenceState.DownloadingContent => ctx.ProgressPercent >= 0
            ? $"Downloading content — {ctx.ProgressPercent}%"
            : "Downloading content",
        PresenceState.LaunchingGame => "Launching game",
        PresenceState.InGame => ctx is { Players: > 0, MaxPlayers: > 0 }
            ? $"In Game — {ctx.Players}/{ctx.MaxPlayers}"
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
                    _ => null
                },
                SmallImageText = BuildStateText(new PresenceContext(state))
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
            : string.Concat(value.AsSpan(0, MaxFieldLength - 1), "…");
    }

    private void OnPresenceSettingsChanged((bool Hidden, string ApplicationId) settings) =>
        ApplySettings(settings.Hidden, settings.ApplicationId);

    private void OnButtonsSettingsChanged(bool showButtons)
    {
        _showButtons = showButtons;
        ResendCurrent();
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

            client = new DiscordRpcClient(applicationId)
            {
                SkipIdenticalPresence = true
            };

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
        }

        if (!client.Initialize())
        {
            _logger.LogWarning("Failed to initialize Discord RPC - status will not be shown");
            return;
        }

        ResendCurrent();
    }

    private void StopClient()
    {
        DiscordRpcClient? client;

        lock (_gate)
        {
            client = _client;
            _client = null;
            _pending = null;
            _ = _flushTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        }

        if (client is null)
            return;

        try
        {
            if (client.IsInitialized)
                client.ClearPresence();
        }
#if DEBUG
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to clear Discord presence");
        }
#elif RELEASE
        catch
        {
            // ignore
        }
#endif

        client.Dispose();
    }

    public void Dispose()
    {
        ServerSession? session;

        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            session = _session;
            _session = null;
        }

        session?.Dispose();

        _settingsSubscription.Dispose();
        _buttonsSubscription.Dispose();
        _flushTimer?.Dispose();
        StopClient();
    }

    public sealed class ServerSession : IDisposable
    {
        private static readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(30);

        private readonly DiscordRichPresence _owner;
        private readonly Uri? _statusAddress;
        private readonly CancellationTokenSource _cts = new();

        private int _disposed;

        internal PresenceState State { get; private set; } = PresenceState.LaunchingGame;
        internal ServerPresence Server { get; private set; }
        internal int ProgressPercent { get; private set; } = -1;

        internal ServerSession(DiscordRichPresence owner, Uri? statusAddress, string? fallbackName)
        {
            _owner = owner;
            _statusAddress = statusAddress;
            Server = new ServerPresence(string.IsNullOrWhiteSpace(fallbackName) ? null : fallbackName);
        }

        internal void Start()
        {
            lock (_owner._gate)
                _owner.Rebuild();

            if (_statusAddress is not null)
                _ = PollAsync(_cts.Token);
        }

        public void SetState(PresenceState state)
        {
            lock (_owner._gate)
            {
                if (State == state)
                    return;

                State = state;

                if (state != PresenceState.DownloadingContent)
                    ProgressPercent = -1;

                _owner.Rebuild();
            }
        }

        /// <summary>Можно дёргать хоть на каждый пакет — лишнее схлопнёт троттлинг.</summary>
        public void SetProgress(int percent)
        {
            lock (_owner._gate)
            {
                if (ProgressPercent == percent)
                    return;

                ProgressPercent = percent;
                _owner.Rebuild();
            }
        }

        public void SetServer(ServerPresence server)
        {
            lock (_owner._gate)
            {
                if (Server == server)
                    return;

                Server = server;
                _owner.Rebuild();
            }
        }

        private async Task PollAsync(CancellationToken cancel)
        {
            using var timer = new PeriodicTimer(_pollInterval);

            do
            {
                try
                {
                    var status = await _owner._http.GetFromJsonAsync<StatusDto>(_statusAddress, cancel);

                    if (status is not null)
                    {
                        SetServer(new ServerPresence(
                            string.IsNullOrWhiteSpace(status.Name) ? Server.Name : status.Name,
                            status.Players,
                            status.SoftMaxPlayers > 0 ? status.SoftMaxPlayers : status.MaxPlayers));
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    // Сервер может лежать/рестартовать — это не повод шуметь в логах.
                    Log.Debug(ex, "Не удалось получить /status для пресенса");
                }
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
            _owner.DetachSession(this);
        }

        private sealed record StatusDto(
            [property: JsonPropertyName("name")] string? Name,
            [property: JsonPropertyName("players")] int Players,
            [property: JsonPropertyName("soft_max_players")] int SoftMaxPlayers,
            [property: JsonPropertyName("max_players")] int MaxPlayers);
    }
}
