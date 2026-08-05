
using Starlight.Launcher.WebUI.Bridge;
using Starlight.Launcher.WebUI.Models.DiscordRichPresence;

namespace Starlight.Launcher.Services.Bridge;

public sealed partial class Bridge : IBridge
{
    public void Apply(PresenceState state) => _discordRichPresence.Apply(state);
}
