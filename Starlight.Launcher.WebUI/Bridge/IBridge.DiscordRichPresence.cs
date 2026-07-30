using Starlight.Launcher.WebUI.Models.DiscordRichPresence;

namespace Starlight.Launcher.WebUI.Bridge;

public partial interface IBridge
{
    void UpdatePresence(PresenceState state, string? serverName = null);
}
