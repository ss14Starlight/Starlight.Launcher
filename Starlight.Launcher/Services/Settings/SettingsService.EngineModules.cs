using Robust.Launcher.Api.Models.Data;

namespace Starlight.Launcher.Services.Settings;

public sealed partial class SettingsService
{
    public HashSet<InstalledEngineModule> GetModules()
    {
        _modulesLock.Wait();
        try
        {
            return [.. _engineModules];
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
            _ = _engineModules.Add(module);
        }
        finally
        {
            _ = _modulesLock.Release();
        }

        ModulesChanged?.Invoke();

        ScheduleSaveInternal(ref _modulesSaveCts, () => SaveJsonAsync(_modulesPath, _modulesLock, () => _engineModules), "modules");
    }

    public void RemoveInstalledModule(InstalledEngineModule module)
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

        ModulesChanged?.Invoke();

        ScheduleSaveInternal(ref _modulesSaveCts, () => SaveJsonAsync(_modulesPath, _modulesLock, () => _engineModules), "modules");
    }

    public void WriteModules(HashSet<InstalledEngineModule> modules)
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

        ScheduleSaveInternal(ref _modulesSaveCts, () => SaveJsonAsync(_modulesPath, _modulesLock, () => _engineModules), "modules");
    }

    public async Task<HashSet<InstalledEngineModule>> GetModulesAsync()
    {
        await _modulesLock.WaitAsync();
        try
        {
            return [.. _engineModules];
        }
        finally
        {
            _ = _modulesLock.Release();
        }
    }

    public async Task WriteModulesAsync(HashSet<InstalledEngineModule> modules)
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

        ScheduleSaveInternal(ref _modulesSaveCts, () => SaveJsonAsync(_modulesPath, _modulesLock, () => _engineModules), "modules");
    }
}
