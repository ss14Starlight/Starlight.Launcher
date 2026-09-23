using Serilog;
using Starlight.Launcher.WebUI.Bridge;
using Starlight.Launcher.WebUI.Models.Data;

namespace Starlight.Launcher.Services.Bridge;

public sealed partial class Bridge : IBridge
{
    public async Task<DataResetResult> ClearEnginesAsync()
    {
        if (_updater.IsUpdating)
            return DataResetResult.Busy;

        return await Task.Run(() =>
        {
            try
            {
                // A running client keeps its engine zip open, and deleting it from under the game would break it.
                using (var con = _content.GetSqliteConnection())
                {
                    if (ContentManager.GetRunningClientVersions(con).Count > 0)
                        return DataResetResult.ClientRunning;
                }

                _engineManager.ClearAllEngines();
                Log.Information("Cleared all installed engines by user request");
                return DataResetResult.Success;
            }
            catch (Exception e)
            {
                Log.Error(e, "Failed to clear installed engines");
                return DataResetResult.Failed;
            }
        });
    }

    public async Task<DataResetResult> ClearContentAsync()
    {
        if (_updater.IsUpdating)
            return DataResetResult.Busy;

        try
        {
            if (!await _content.ClearAll())
                return DataResetResult.ClientRunning;

            Log.Information("Cleared all downloaded server content by user request");
            return DataResetResult.Success;
        }
        catch (Exception e)
        {
            Log.Error(e, "Failed to clear server content");
            return DataResetResult.Failed;
        }
    }
}
