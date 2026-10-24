namespace Starlight.Launcher.WebUI.Models.Data;

public sealed record StorageUsage(long ContentDbBytes, List<ForkStorageUsage> Forks, List<EngineStorageUsage> Engines)
{
    public long EnginesBytes => Engines.Sum(e => e.Bytes);

    public long TotalBytes => ContentDbBytes + EnginesBytes;
}

public sealed record ForkStorageUsage(string? ForkId, int Versions, long Bytes, DateTime LastUsed);

public sealed record EngineStorageUsage(string Name, string Version, long Bytes)
{
    public bool IsEngine => Name == "Robust";
}
