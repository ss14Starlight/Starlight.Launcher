using Robust.Launcher.Api.Models.Data;

namespace Starlight.Launcher.Services.Settings;

public sealed partial class SettingsService
{
    public Dictionary<string, InstalledEngineVersion> GetEngines()
    {
        _enginesLock.Wait();
        try
        {
            return new Dictionary<string, InstalledEngineVersion>(_engineInstallations);
        }
        finally
        {
            _ = _enginesLock.Release();
        }
    }

    public void AddInstalledEngine(InstalledEngineVersion version)
    {
        _enginesLock.Wait();
        try
        {
            _engineInstallations[version.Version] = version;
        }
        finally
        {
            _ = _enginesLock.Release();
        }

        EnginesChanged?.Invoke();

        ScheduleSaveInternal(ref _enginesSaveCts, () => SaveJsonAsync(_enginesPath, _enginesLock, _engineInstallations.Values), "engines");
    }

    public void RemoveInstalledEngine(string version)
    {
        _enginesLock.Wait();
        try
        {
            _ = _engineInstallations.Remove(version);
        }
        finally
        {
            _ = _enginesLock.Release();
        }

        EnginesChanged?.Invoke();

        ScheduleSaveInternal(ref _enginesSaveCts, () => SaveJsonAsync(_enginesPath, _enginesLock, _engineInstallations.Values), "engines");
    }

    public void WriteEngines(Dictionary<string, InstalledEngineVersion> engines)
    {
        _enginesLock.Wait();
        try
        {
            _engineInstallations = engines;
        }
        finally
        {
            _ = _enginesLock.Release();
        }

        EnginesChanged?.Invoke();

        ScheduleSaveInternal(ref _enginesSaveCts, () => SaveJsonAsync(_enginesPath, _enginesLock, _engineInstallations.Values), "engines");
    }

    public async Task<Dictionary<string, InstalledEngineVersion>> GetEnginesAsync()
    {
        await _enginesLock.WaitAsync();
        try
        {
            return new Dictionary<string, InstalledEngineVersion>(_engineInstallations);
        }
        finally
        {
            _ = _enginesLock.Release();
        }
    }

    public async Task WriteEnginesAsync(Dictionary<string, InstalledEngineVersion> engines)
    {
        await _enginesLock.WaitAsync();
        try
        {
            _engineInstallations = engines;
        }
        finally
        {
            _ = _enginesLock.Release();
        }

        EnginesChanged?.Invoke();

        ScheduleSaveInternal(ref _enginesSaveCts, () => SaveJsonAsync(_enginesPath, _enginesLock, _engineInstallations.Values), "engines");
    }
}
