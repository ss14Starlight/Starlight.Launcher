using DiscordRPC;
using Serilog;
using Starlight.Launcher.Services.Settings;
using Starlight.Launcher.WebUI.Models.DiscordRichPresence;

namespace Starlight.Launcher.Services.Discord;

public sealed partial class DiscordRichPresence : IDisposable
{
    private readonly DiscordRpcClient? _client;

    public PresenceState CurrentPresenceState { get; internal set; } = PresenceState.Idle;

    private string _currentServerName = "";

    private readonly DateTime _startedAt = DateTime.UtcNow;

    public DiscordRichPresence(SettingsService settingsService)
    {
        var settings = settingsService.GetSettings();
        if (settings.HidePresence)
        {
            Log.Information("Discord Rich Presence is disabled in settings.");
            return;
        }
        _client = new DiscordRpcClient(settings.DiscordReachPresenceID);

        _client.OnReady += (sender, e) =>
        {
            Log.Information("Discord Rich Presence initialized.");
            Log.Information("Connected to discord with user {0}", e.User.Username);
        };

        _client.OnError += (s, e) => Log.Error("Discord RPC error: {Message}", e.Message);
    }

    public void Dispose() => _client?.Dispose(); // Clean up the client when we're done with it

    public void Initialize()
    {
        _ = _client?.Initialize();
        UpdatePresence(CurrentPresenceState); // Initial presence update to set the starting state
    }

    public void UpdateServerPresence(string serverName)
    {
        CurrentPresenceState = PresenceState.LaunchingGame;
        UpdatePresence(CurrentPresenceState, serverName);

    }

    public void UpdatePresence(PresenceState state, string? serverName = null)
    {
        if (_client == null)
            return;
        if (CurrentPresenceState == state)
            return;
        var presence = new RichPresence
        {
            Details = serverName == null ? string.IsNullOrEmpty(_currentServerName) ? "" : $"Playing {_currentServerName}" : $"Playing {serverName}",
            State = state switch
            {
                PresenceState.Idle => "Idling",
                PresenceState.SearchingServers => "Browsing servers",
                PresenceState.SettingUp => "Configuring settings",
                PresenceState.LaunchingGame => "Launching Space Station 14",
                PresenceState.DownloadingContent => "Downloading Content",
                _ => "Unknown"
            },
            Assets = new Assets
            {
                LargeImageKey = "launcher_icon",
                LargeImageText = "Starlight Launcher"
            },
            Timestamps = new Timestamps
            {
                Start = _startedAt
            }
        };
        CurrentPresenceState = state;
        _currentServerName = serverName ?? _currentServerName;
        _client.SetPresence(presence);
    }
}
