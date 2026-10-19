namespace Starlight.Launcher.Services.Settings;

public sealed partial class SettingsService
{
    private byte[]? _loginKey;
    private readonly SemaphoreSlim _keyLock = new(1, 1);

    private async Task<byte[]> GetLoginKeyAsync()
    {
        if (_loginKey is not null) return _loginKey;
        await _keyLock.WaitAsync();
        try
        {
            return _loginKey ??= await _keyProvider.GetOrCreateKeyAsync(_loginKeyPath);
        }
        finally { _ = _keyLock.Release(); }
    }

    private static bool LooksEncrypted(byte[] data) => LoginsCipher.LooksEncrypted(data);

    private async Task<byte[]> EncryptAsync(byte[] plaintext)
        => LoginsCipher.Encrypt(await GetLoginKeyAsync(), plaintext);

    private async Task<byte[]> DecryptAsync(byte[] data)
        => LoginsCipher.Decrypt(await GetLoginKeyAsync(), data);
}
