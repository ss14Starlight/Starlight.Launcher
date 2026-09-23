using Serilog;
using Starlight.Launcher.WebUI.Models.Data;

namespace Starlight.Launcher.Services.EngineManager;

/// <summary>
///     Asks the player before running an engine build that didn't come from the Starlight CDN.
/// </summary>
public sealed class EngineSourceGuard
{
    private readonly Lock _lock = new();
    private readonly HashSet<(string Version, string Cdn)> _accepted = [];

    /// <summary>
    ///     Raised to ask the UI; resolves to true when the player wants to continue anyway.
    /// </summary>
    public event Func<ForeignEngineWarning, Task<bool>>? ConfirmForeignEngine;

    public async Task<bool> ConfirmAsync(ForeignEngineWarning warning, CancellationToken cancel)
    {
        var key = (warning.EngineVersion, warning.CdnName);

        lock (_lock)
        {
            // Only ask once per version and CDN for the lifetime of the launcher.
            if (_accepted.Contains(key))
                return true;
        }

        var handler = ConfirmForeignEngine;
        if (handler is null)
        {
            // No window to ask through (e.g. launched from a deep link while hidden); don't block the launch.
            Log.Warning("No UI to confirm engine {Version} from {Cdn}; continuing", warning.EngineVersion, warning.CdnName);
            return true;
        }

        bool accepted;
        try
        {
            accepted = await handler(warning).WaitAsync(cancel);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Log.Warning(e, "Could not show the foreign engine warning; continuing");
            return true;
        }

        if (accepted)
        {
            lock (_lock)
            {
                _ = _accepted.Add(key);
            }
        }

        return accepted;
    }
}
