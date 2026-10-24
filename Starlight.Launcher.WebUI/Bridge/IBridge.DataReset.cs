using Starlight.Launcher.WebUI.Models.Data;

namespace Starlight.Launcher.WebUI.Bridge;

public partial interface IBridge
{
    Task<DataResetResult> ClearEnginesAsync();

    Task<DataResetResult> ClearContentAsync();

    Task<ContentRepairResult> VerifyContentAsync(CancellationToken cancel = default);

    Task<StorageUsage> GetStorageUsageAsync();
}
