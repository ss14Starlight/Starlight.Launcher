using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace Starlight.Launcher.Services.Settings;

public sealed class DpapiKeyProvider : ILoginKeyProvider
{
    private readonly ILogger<DpapiKeyProvider> _logger;
    private static readonly byte[] _entropy = "starlight.logins.v1"u8.ToArray();

    private static readonly string _legacyKeyPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Starlight.Launcher", "logins.key");

    public DpapiKeyProvider(ILogger<DpapiKeyProvider> logger) => _logger = logger;

    public async Task<byte[]> GetOrCreateKeyAsync(string keyPath)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new Exception("Windows methods isn't supported on Linux or MAC!");

        _ = Directory.CreateDirectory(Path.GetDirectoryName(keyPath)!);

        if (!File.Exists(keyPath) && File.Exists(_legacyKeyPath))
        {
            try
            {
                File.Move(_legacyKeyPath, keyPath, false);
                _logger.LogInformation("Migrated login key {old} -> {new}", _legacyKeyPath, keyPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Legacy key migration failed; will read in place if possible");
            }
        }

        var readPath = File.Exists(keyPath) ? keyPath
                     : File.Exists(_legacyKeyPath) ? _legacyKeyPath
                     : null;

        if (readPath is not null)
        {
            try
            {
                var blob = await File.ReadAllBytesAsync(readPath);
                var key = ProtectedData.Unprotect(blob, _entropy, DataProtectionScope.CurrentUser);
                _logger.LogInformation("Loaded login key from {path} fp={fp}", readPath, Fp(key));
                return key;
            }
            catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
            {
                _logger.LogError(ex, "Login key at {path} is unusable; regenerating (re-auth required).", readPath);
                TryDelete(keyPath);
                TryDelete(_legacyKeyPath);
            }
        }

        return await CreateAndStoreAsync(keyPath);
    }

    private async Task<byte[]> CreateAndStoreAsync(string keyPath)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new Exception("Windows methods isn't supported on Linux or MAC!");

        var newKey = RandomNumberGenerator.GetBytes(32);
        var blob = ProtectedData.Protect(newKey, _entropy, DataProtectionScope.CurrentUser);
        var tmp = keyPath + ".tmp";
        await File.WriteAllBytesAsync(tmp, blob);
        File.Move(tmp, keyPath, true);
        _logger.LogWarning("Generated NEW login key at {path} fp={fp}", keyPath, Fp(newKey));
        return newKey;
    }

    private void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete key file {path}", path); }
    }

    private static string Fp(byte[] key) => Convert.ToHexString(SHA256.HashData(key))[..8];
}
