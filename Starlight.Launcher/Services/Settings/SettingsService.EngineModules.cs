using Robust.Launcher.Api.Models.Data;

namespace Starlight.Launcher.Services.Settings;

sealed partial class SettingsService
{
    public HashSet<(string Version, string Name)> GetModules()
    {
        _modulesLock.Wait();
        try
        {
            return _engineModules.ToHashSet();
        }
        finally
        {
            _ = _modulesLock.Release();
        }
    }

    public void AddInstalledModule(InstalledEngineModule module)
    {
        _modulesLock.Wait();
        try
        {
            _ = _engineModules.Add((module.Version, module.Name));
        }
        finally
        {
            _ = _modulesLock.Release();
        }

        ModulesChanged?.Invoke();

        ScheduleSaveInternal(ref _enginesSaveCts, () => SaveJsonAsync(_enginesPath, _enginesLock, _engineInstallations.Values), "engines");
    }

    public void RemoveInstalledModule((string, string) module)
    {
        _modulesLock.Wait();
        try
        {
            _ = _engineModules.Remove(module);
        }
        finally
        {
            _ = _modulesLock.Release();
        }

        EnginesChanged?.Invoke();

        ScheduleSaveInternal(ref _modulesSaveCts, () => SaveJsonAsync(_modulesPath, _modulesLock, _engineModules), "modules");
    }

    public void WriteModules(HashSet<(string, string)> modules)
    {
        _modulesLock.Wait();
        try
        {
            _engineModules = modules;
        }
        finally
        {
            _ = _modulesLock.Release();
        }

        ModulesChanged?.Invoke();

        ScheduleSaveInternal(ref _modulesSaveCts, () => SaveJsonAsync(_modulesPath, _modulesLock, _engineModules), "modules");
    }

    public async Task<HashSet<(string, string)>> GetModulesAsync()
    {
        await _modulesLock.WaitAsync();
        try
        {
            return _engineModules.ToHashSet();
        }
        finally
        {
            _ = _modulesLock.Release();
        }
    }

    public async Task WriteModulesAsync(HashSet<(string, string)> modules)
    {
        await _modulesLock.WaitAsync();
        try
        {
            _engineModules = modules;
        }
        finally
        {
            _ = _modulesLock.Release();
        }

        ModulesChanged?.Invoke();

        ScheduleSaveInternal(ref _modulesSaveCts, () => SaveJsonAsync(_modulesPath, _modulesLock, _engineModules), "modules");
    }
}
