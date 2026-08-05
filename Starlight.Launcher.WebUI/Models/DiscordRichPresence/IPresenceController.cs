namespace Starlight.Launcher.WebUI.Models.DiscordRichPresence;

public readonly record struct ServerPresence(string? Name = null, int Players = 0, int MaxPlayers = 0);

public interface IPresenceController
{
    PresenceState? ResolvedState { get; }

    void SetNavigation(PresenceState state);

    IPresenceScope Activate(PresenceState state);

    IPresenceScope Activate(PresenceState state, ServerPresence server);

    void SetActive(PresenceState state, bool active);

    void SetServer(PresenceState state, ServerPresence server);

    void SetProgress(PresenceState state, int percent);
}

public interface IPresenceScope : IDisposable
{
    PresenceState State { get; }

    void SetServer(ServerPresence server);

    void SetProgress(int percent);
}
