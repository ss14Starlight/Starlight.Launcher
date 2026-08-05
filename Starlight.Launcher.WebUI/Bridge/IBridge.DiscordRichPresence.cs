using Starlight.Launcher.WebUI.Models.DiscordRichPresence;

namespace Starlight.Launcher.WebUI.Bridge;

public partial interface IBridge
{
    void Apply(PresenceState state);
}
