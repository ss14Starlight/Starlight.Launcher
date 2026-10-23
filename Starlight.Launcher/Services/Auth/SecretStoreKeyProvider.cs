using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Starlight.Launcher.Platforms.Linux.Services.Settings;
using Starlight.Launcher.Platforms.MacOS.Services.Settings;
using Starlight.Launcher.Services.Settings;

namespace Starlight.Launcher.Services.Auth;

public sealed class SecretStoreKeyProvider : ILoginKeyProvider
{
    private const int KeySize = 32;

    private readonly ILogger<SecretStoreKeyProvider> _logger;
    private readonly FileKeyProvider _fallback;
    private readonly ISecretStore? _store;

    public SecretStoreKeyProvider(ILogger<SecretStoreKeyProvider> logger, FileKeyProvider fallback)
    {
        _logger = logger;
        _fallback = fallback;
        _store = CreateStore();

        if (_store is null)
            _logger.LogWarning("No OS secret store available; the login key stays in a file");
    }

    private static ISecretStore? CreateStore()
    {
        if (OperatingSystem.IsMacOS())
            return KeychainSecretStore.TryCreate();

        if (OperatingSystem.IsLinux())
            return LibSecretStore.TryCreate();

        return null;
    }

    public async Task<byte[]> GetOrCreateKeyAsync(string keyPath)
    {
        if (_store is null)
            return await _fallback.GetOrCreateKeyAsync(keyPath);

        _ = Directory.CreateDirectory(Path.GetDirectoryName(keyPath)!);

        var markerPath = Path.ChangeExtension(keyPath, ".keyring");

        byte[]? stored;
        try
        {
            stored = await Task.Run(_store.Load);
        }
        catch (Exception ex)
        {
            if (File.Exists(markerPath) && !File.Exists(keyPath))
            {
                _logger.LogError(ex, "{store} is unavailable and holds the login key; leaving logins alone", _store.Name);
                throw;
            }

            _logger.LogWarning(ex, "{store} is unavailable; using the key file", _store.Name);
            return await _fallback.GetOrCreateKeyAsync(keyPath);
        }

        if (stored is { Length: KeySize })
        {
            _logger.LogInformation("Loaded login key from {store} fp={fp}", _store.Name, Fp(stored));
            Touch(markerPath);
            TryDelete(keyPath); // left over from an interrupted migration
            return stored;
        }

        if (stored is not null)
            _logger.LogError("Login key in {store} has a bad length; replacing it", _store.Name);

        var migrating = File.Exists(keyPath);
        var key = migrating
            ? await _fallback.GetOrCreateKeyAsync(keyPath)
            : RandomNumberGenerator.GetBytes(KeySize);

        try
        {
            await Task.Run(() => _store.Save(key));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not put the login key into {store}; using the key file", _store.Name);
            if (!migrating)
                await FileKeyProvider.StoreAsync(keyPath, key);

            TryDelete(markerPath);
            return key;
        }

        Touch(markerPath);
        TryDelete(keyPath);

        if (migrating)
            _logger.LogInformation("Moved login key from {path} into {store} fp={fp}", keyPath, _store.Name, Fp(key));
        else
            _logger.LogWarning("Generated NEW login key in {store} fp={fp}", _store.Name, Fp(key));

        return key;
    }

    private void Touch(string path)
    {
        try { if (!File.Exists(path)) File.WriteAllText(path, _store!.Name); }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to write key marker {path}", path); }
    }

    private void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete {path}", path); }
    }

    private static string Fp(byte[] key) => Convert.ToHexString(SHA256.HashData(key))[..8];
}
