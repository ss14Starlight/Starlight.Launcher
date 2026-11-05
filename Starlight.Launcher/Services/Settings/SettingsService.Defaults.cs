using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Starlight.Launcher.WebUI.Models.Settings;

namespace Starlight.Launcher.Services.Settings;

public sealed partial class SettingsService
{
    private JsonObject _defaultsBaseline = [];
    private IReadOnlyList<DefaultSettingChange> _pendingDefaultChanges = [];
    private readonly Lock _defaultsLock = new();
    private string _defaultsBaselinePath = "";

    /// <summary>
    /// Settings whose default changed while the user had their own value. See <see cref="SettingsDefaults"/>.
    /// </summary>
    public IReadOnlyList<DefaultSettingChange> GetPendingDefaultChanges()
    {
        lock (_defaultsLock)
            return _pendingDefaultChanges;
    }

    /// <summary>
    /// Merges the developers' changes into the user's values (<see cref="DefaultSettingChange.MergedValue"/>).
    /// </summary>
    public void AcceptDefaultChanges(IReadOnlyCollection<string> keys)
    {
        var resolved = ResolveDefaultChanges(keys);
        if (resolved.Count == 0)
            return;

        var values = resolved.ToDictionary(c => c.Key, c => JsonNode.Parse(c.MergedValue));
        UpdateSettings(s => SettingsDefaults.ApplyValues(s, values));
        _logger.LogInformation("Accepted new defaults for {keys}", string.Join(", ", values.Keys));
    }

    /// <summary>
    /// Keeps the user's values; they won't be asked about these defaults again.
    /// </summary>
    public void KeepCurrentSettings(IReadOnlyCollection<string> keys)
    {
        var resolved = ResolveDefaultChanges(keys);
        if (resolved.Count > 0)
            _logger.LogInformation("Kept own values over new defaults for {keys}", string.Join(", ", resolved.Select(c => c.Key)));
    }

    private List<DefaultSettingChange> ResolveDefaultChanges(IReadOnlyCollection<string> keys)
    {
        List<DefaultSettingChange> resolved;
        string json;
        lock (_defaultsLock)
        {
            resolved = [.. _pendingDefaultChanges.Where(c => keys.Contains(c.Key))];
            if (resolved.Count == 0)
                return resolved;

            var defaults = SettingsDefaults.DefaultsSnapshot();
            foreach (var change in resolved)
                _defaultsBaseline[change.Key] = defaults[change.Key]?.DeepClone();

            _pendingDefaultChanges = [.. _pendingDefaultChanges.Except(resolved)];
            json = _defaultsBaseline.ToJsonString(_jsonOptions);
        }

        _ = SaveDefaultsBaselineAsync(json);
        return resolved;
    }

    private void ReconcileDefaults(bool settingsFileExisted)
    {
        _defaultsBaselinePath = Path.Combine(Path.GetDirectoryName(_filePath)!, "settings.defaults.json");

        if (_unreadablePaths.ContainsKey(_filePath))
            return;

        var defaults = SettingsDefaults.DefaultsSnapshot();

        if (!settingsFileExisted)
        {
            _defaultsBaseline = defaults;
            WriteDefaultsBaseline();
            return;
        }

        var plan = SettingsDefaults.Reconcile(
            SettingsDefaults.Snapshot(_settings),
            defaults,
            ReadDefaultsBaseline(),
            SettingsDefaults.LegacyDefaults);

        _defaultsBaseline = plan.Baseline;
        _pendingDefaultChanges = plan.Conflicts;

        if (plan.ApplySilently.Count > 0)
        {
            _settings = SettingsDefaults.ApplyDefaults(_settings, plan.ApplySilently);
            _logger.LogInformation("Updated settings left at old defaults: {keys}", string.Join(", ", plan.ApplySilently));
            ScheduleSaveInternal(ref _settingsSaveCts, () => SaveJsonAsync(_filePath, _settingsLock, () => _settings), "settings");
        }

        if (plan.Conflicts.Count > 0)
            _logger.LogInformation("Changed defaults to ask about: {keys}", string.Join(", ", plan.Conflicts.Select(c => c.Key)));

        WriteDefaultsBaseline();
    }

    private JsonObject? ReadDefaultsBaseline()
    {
        try
        {
            return JsonNode.Parse(File.ReadAllText(_defaultsBaselinePath)) as JsonObject;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read {path}, treating defaults as never recorded", _defaultsBaselinePath);
            return null;
        }
    }

    private void WriteDefaultsBaseline()
    {
        try
        {
            var tmp = _defaultsBaselinePath + ".tmp";
            _ = Directory.CreateDirectory(Path.GetDirectoryName(_defaultsBaselinePath)!);
            File.WriteAllText(tmp, _defaultsBaseline.ToJsonString(_jsonOptions));
            File.Move(tmp, _defaultsBaselinePath, true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save {path}", _defaultsBaselinePath);
        }
    }

    private async Task SaveDefaultsBaselineAsync(string json)
    {
        var writeLock = _writeLocks.GetOrAdd(_defaultsBaselinePath, _ => new SemaphoreSlim(1, 1));
        await writeLock.WaitAsync();
        try
        {
            await WriteFileSafeAsync(json, Path.GetDirectoryName(_defaultsBaselinePath)!, _defaultsBaselinePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save {path}", _defaultsBaselinePath);
        }
        finally
        {
            _ = writeLock.Release();
        }
    }
}
