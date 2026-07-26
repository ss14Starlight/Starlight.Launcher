
using Starlight.Launcher.WebUI.Bridge;

namespace Starlight.Launcher.Services.Bridge;

public sealed partial class Bridge : IBridge
{
    public event Func<string, Task>? ConnectRequested
    {
        add => _commands.ConnectRequested += value;
        remove => _commands.ConnectRequested -= value;
    }
}
