using Starlight.Launcher.WebUI.Models.Data;

namespace Starlight.Launcher.Services.Settings;

public sealed partial class SettingsService
{
    public bool HasAcceptedPrivacyPolicy(string identifier, out string? acceptedVersion)
    {
        _settingsLock.Wait();
        try
        {
            if (_settings.AcceptedPrivacyPolicies.TryGetValue(identifier, out var policy))
            {
                acceptedVersion = policy.Version;
                return true;
            }

            acceptedVersion = null;
            return false;
        }
        finally
        {
            _ = _settingsLock.Release();
        }
    }

    public void AcceptPrivacyPolicy(string identifier, string version)
    {
        _settingsLock.Wait();
        try
        {
            _settings.AcceptedPrivacyPolicies[identifier] = new AcceptedPrivacyPolicy
            {
                Version = version
            };
        }
        finally
        {
            _ = _settingsLock.Release();
        }

        ScheduleSaveInternal(ref _settingsSaveCts, () => SaveJsonAsync(_filePath, _settingsLock, _settings), "settings");
    }

    public void UpdateConnectedToPrivacyPolicy(string identifier)
    {
        _settingsLock.Wait();
        try
        {
            if (_settings.AcceptedPrivacyPolicies.TryGetValue(identifier, out var policy))
                _settings.AcceptedPrivacyPolicies[identifier] = policy with { LastConnected = DateTimeOffset.UtcNow };
        }
        finally
        {
            _ = _settingsLock.Release();
        }

        ScheduleSaveInternal(ref _settingsSaveCts, () => SaveJsonAsync(_filePath, _settingsLock, _settings), "settings");
    }
}
