
using Starlight.Launcher.WebUI.Bridge;
using Starlight.Launcher.WebUI.Models.DiscordRichPresence;

namespace Starlight.Launcher.Services.Bridge;

public sealed partial class Bridge : IBridge
{
    public void UpdatePresence(PresenceState state, string? serverName = null) => _discordRichPresence.UpdatePresence(state, serverName);
}
