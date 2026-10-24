using Serilog;
using Starlight.Launcher.Services.ContentManagement;
using Starlight.Launcher.WebUI.Bridge;
using Starlight.Launcher.WebUI.Models.Data;

namespace Starlight.Launcher.Services.Bridge;

public sealed partial class Bridge : IBridge
{
    public async Task<DataResetResult> ClearEnginesAsync()
        => _updater.IsUpdating
        ? DataResetResult.Busy
        : await Task.Run(() =>
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

    public async Task<ContentRepairResult> VerifyContentAsync()
    {
        try
        {
            var (ran, result) = await _updater.TryRunExclusiveAsync(() => _content.VerifyAndRepair());
            return ran ? result : new ContentRepairResult(DataResetResult.Busy);
        }
        catch (Exception e)
        {
            Log.Error(e, "Failed to verify content DB");
            return new ContentRepairResult(DataResetResult.Failed);
        }
    }

    public async Task<StorageUsage> GetStorageUsageAsync()
    {
        long dbBytes = 0;
        List<ForkStorageUsage> forks = [];
        try
        {
            (dbBytes, forks) = await _content.GetStorageUsage();
        }
        catch (Exception e)
        {
            Log.Warning(e, "Failed to measure content DB usage");
        }

        var engines = await Task.Run(() =>
        {
            var list = new List<EngineStorageUsage>();

            foreach (var engine in _settings.GetEngines().Values)
            {
                try
                {
                    var file = new FileInfo(_engineManager.GetEnginePath(engine.Version));
                    list.Add(new EngineStorageUsage("Robust", engine.Version, file.Exists ? file.Length : 0));
                }
                catch (Exception e)
                {
                    Log.Debug(e, "Failed to measure engine {EngineVersion}", engine.Version);
                }
            }

            foreach (var module in _settings.GetModules())
            {
                try
                {
                    var dir = new DirectoryInfo(_engineManager.GetEngineModule(module.Name, module.Version));
                    var size = dir.Exists ? dir.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) : 0;
                    list.Add(new EngineStorageUsage(module.Name, module.Version, size));
                }
                catch (Exception e)
                {
                    Log.Debug(e, "Failed to measure module {ModuleName} {ModuleVersion}", module.Name, module.Version);
                }
            }

            return list
                .OrderByDescending(e => e.IsEngine)
                .ThenBy(e => e.Name, StringComparer.Ordinal)
                .ThenByDescending(e => e.Version, StringComparer.Ordinal)
                .ToList();
        });

        return new StorageUsage(dbBytes, forks, engines);
    }
}
