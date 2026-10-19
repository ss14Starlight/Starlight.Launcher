using Microsoft.AspNetCore.Components;
using Starlight.Launcher.WebUI.Bridge;
using Starlight.Launcher.WebUI.Localization;
using Starlight.Launcher.WebUI.Models.Settings;
using Starlight.Launcher.WebUI.Services;

namespace Starlight.Launcher.WebUI.Components.Pages;

/// <summary>
/// Shared plumbing for pages made of setting options (Settings, Development).
/// </summary>
public abstract class SettingsPageBase : LocalizedComponentBase
{
    [Inject] protected IBridge Bridge { get; set; } = default!;
    [Inject] protected AppState State { get; set; } = default!;

    private AppSettings? _appSettingsCache;
    private DateTime _lastCacheUpdate;
    private static readonly TimeSpan _cacheUpdateInterval = TimeSpan.FromSeconds(2);

    protected Task OnSettingChanged<T>(T value, Action<T>? setLocal, Func<AppSettings, T, AppSettings> update, bool callWindowUpdate = false)
    {
        setLocal?.Invoke(value);
        return UpdateSetting(s => update(s, value), callWindowUpdate);
    }

    protected async Task UpdateSetting(Func<AppSettings, AppSettings> update, bool callWindowUpdate = false)
    {
        await Task.Run(() => Bridge.UpdateSettings(update));
        _appSettingsCache = null;
        if (callWindowUpdate)
            State.CallUpdate();
    }

    protected async Task<T> FetchSettings<T>(Func<AppSettings, T> func)
    {
        if (_appSettingsCache == null || DateTime.Now - _lastCacheUpdate >= _cacheUpdateInterval)
        {
            _appSettingsCache = await Bridge.GetSettingsAsync();
            _lastCacheUpdate = DateTime.Now;
        }

        return func(_appSettingsCache);
    }

    protected void ResetSettingsCache() => _appSettingsCache = null;
}
