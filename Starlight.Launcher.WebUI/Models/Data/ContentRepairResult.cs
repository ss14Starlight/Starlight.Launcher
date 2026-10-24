namespace Starlight.Launcher.WebUI.Models.Data;

public sealed record ContentRepairResult(
    DataResetResult Status,
    int CheckedBlobs = 0,
    int CorruptBlobs = 0,
    int RemovedVersions = 0,
    int RemovedOrphans = 0,
    bool DatabaseRebuilt = false)
{
    public bool FoundProblems => CorruptBlobs > 0 || RemovedVersions > 0 || RemovedOrphans > 0 || DatabaseRebuilt;
}
