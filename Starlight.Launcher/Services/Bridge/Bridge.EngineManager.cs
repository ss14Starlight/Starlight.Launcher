using Starlight.Launcher.WebUI.Bridge;
using Starlight.Launcher.WebUI.Models.Data;

namespace Starlight.Launcher.Services.Bridge;

public sealed partial class Bridge : IBridge
{
    public event Func<ForeignEngineWarning, Task<bool>>? ConfirmForeignEngine
    {
        add => _engineSourceGuard.ConfirmForeignEngine += value;
        remove => _engineSourceGuard.ConfirmForeignEngine -= value;
    }
}
