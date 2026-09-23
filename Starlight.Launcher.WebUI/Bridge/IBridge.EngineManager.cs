using Starlight.Launcher.WebUI.Models.Data;

namespace Starlight.Launcher.WebUI.Bridge;

/// <summary>
/// Bridged parts from EngineSourceGuard.cs
/// </summary>
public partial interface IBridge
{
    event Func<ForeignEngineWarning, Task<bool>>? ConfirmForeignEngine;
}
