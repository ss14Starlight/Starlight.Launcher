using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Robust.Launcher.Api.Models.Data;

namespace Starlight.Launcher.Services.Settings;

public sealed partial class SettingsService
{
    private volatile bool _loginsLoaded;
    private readonly TaskCompletionSource _loginsLoadedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public event Action? LoginsUnrecoverable;

    public Task WaitForLoginsLoadedAsync() => _loginsLoadedTcs.Task;

    public async Task InitializeLoginsAsync()
    {
        var loaded = await LoadLoginsAsync();
        await _loginsLock.WaitAsync();
        try
        {
            foreach (var (id, info) in loaded)
                if (!_logins.ContainsKey(id))
                    _logins[id] = info;
        }
        finally { _ = _loginsLock.Release(); }
        _loginsLoaded = true;
        _ = _loginsLoadedTcs.TrySetResult();

        ScheduleSaveInternal(ref _loginsSaveCts, SaveLoginsEncryptedAsync, "logins");
        LoginsChanged?.Invoke();
    }

    public Dictionary<Guid, LoginInfo> GetLogins()
    {
        _loginsLock.Wait();
        try
        {
            return new Dictionary<Guid, LoginInfo>(_logins);
        }
        finally
        {
            _ = _loginsLock.Release();
        }
    }

    public void UpdateLogin(LoginInfo login)
    {
        _loginsLock.Wait();
        try
        {
            if (_logins.ContainsKey(login.UserId))
            {
                _logins[login.UserId] = login;
            }
            else
            {
                _logins.Add(login.UserId, login);
            }
        }
        finally
        {
            _ = _loginsLock.Release();
        }

        LoginsChanged?.Invoke();

        ScheduleSaveInternal(ref _loginsSaveCts, SaveLoginsEncryptedAsync, "logins");
    }

    public void WriteLogins(Dictionary<Guid, LoginInfo> logins)
    {
        _loginsLock.Wait();
        try
        {
            _logins = logins;
        }
        finally
        {
            _ = _loginsLock.Release();
        }

        LoginsChanged?.Invoke();

        ScheduleSaveInternal(ref _loginsSaveCts, SaveLoginsEncryptedAsync, "logins");
    }

    public async Task<Dictionary<Guid, LoginInfo>> GetLoginsAsync()
    {
        await _loginsLock.WaitAsync();
        try
        {
            return new Dictionary<Guid, LoginInfo>(_logins);
        }
        finally
        {
            _ = _loginsLock.Release();
        }
    }

    public async Task WriteLoginsAsync(Dictionary<Guid, LoginInfo> logins)
    {
        await _loginsLock.WaitAsync();
        try
        {
            _logins = logins;
        }
        finally
        {
            _ = _loginsLock.Release();
        }

        LoginsChanged?.Invoke();

        ScheduleSaveInternal(ref _loginsSaveCts, SaveLoginsEncryptedAsync, "logins");
    }

    private static async Task WriteBytesSafeAsync(byte[] content, string dir, string filePath)
    {
        _ = Directory.CreateDirectory(dir);
        var tempFile = filePath + ".tmp";
        await File.WriteAllBytesAsync(tempFile, content);
        File.Move(tempFile, filePath, true);
    }

    private async Task SaveLoginsEncryptedAsync()
    {
        if (!_loginsLoaded)
        {
            _logger.LogWarning("Skipped logins save before load completed");
            return;
        }

        try { await WriteLoginsCoreAsync(); }
        catch (Exception ex) { _logger.LogError(ex, "Failed to save encrypted logins"); }
    }

    private async Task WriteLoginsCoreAsync()
    {
        await _loginsLock.WaitAsync();
        try
        {
            var json = JsonSerializer.Serialize(_logins.Values, _jsonOptions);
            var encrypted = await EncryptAsync(Encoding.UTF8.GetBytes(json));
            _logger.LogInformation("Successfully encrypted {Number} logins!", _logins.Count);
            await WriteBytesSafeAsync(encrypted, Path.GetDirectoryName(_loginsPath)!, _loginsPath);
        }
        finally
        {
            _loginsLock.Release();
        }
    }

    private async Task<Dictionary<Guid, LoginInfo>> LoadLoginsAsync()
    {
        if (!File.Exists(_loginsPath))
            return new();

        var raw = await File.ReadAllBytesAsync(_loginsPath);

        if (LooksEncrypted(raw))
        {
            try
            {
                var json = Encoding.UTF8.GetString(await DecryptAsync(raw));
                var list = JsonSerializer.Deserialize<List<LoginInfo>>(json) ?? new();

                _logger.LogInformation("Successfully loaded {Number} logins from file {File}!", list.Count, _loginsPath);

                return list.ToDictionary(x => x.UserId);
            }
            catch (CryptographicException ex)
            {
                var backup = _loginsPath + $".undecryptable-{DateTime.UtcNow:yyyyMMddHHmmss}";
                try { File.Move(_loginsPath, backup, true); } catch { /* best effort */ }
                _logger.LogError(ex, "Logins could not be decrypted (key mismatch). Backed up to {backup}; re-auth required.", backup);
                LoginsUnrecoverable?.Invoke();   // surface a toast/dialog
                return new();
            }
        }

        try
        {
            var json = Encoding.UTF8.GetString(raw);
            var list = JsonSerializer.Deserialize<List<LoginInfo>>(json) ?? new();
            await _loginsLock.WaitAsync();

            try { _logins = list.ToDictionary(x => x.UserId); }
            finally { _ = _loginsLock.Release(); }

            await WriteLoginsCoreAsync();

            _logger.LogInformation("Successfuly loaded logins from file {File}!", _loginsPath);

            return _logins;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load logins; starting empty");
            return new();
        }
    }
}
