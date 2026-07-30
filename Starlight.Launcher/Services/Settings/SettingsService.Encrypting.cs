using System.Security.Cryptography;

namespace Starlight.Launcher.Services.Settings;

public sealed partial class SettingsService
{
    private const int NonceSize = 12; // AesGcm.NonceByteSizes.MaxSize
    private const int TagSize = 16;   // AesGcm.TagByteSizes.MaxSize

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

    private static readonly byte[] _magic = "SLE1"u8.ToArray(); // Starlight Logins Encrypted v1

    private static bool LooksEncrypted(byte[] data) =>
        data.Length >= _magic.Length + NonceSize + TagSize
        && data.AsSpan(0, _magic.Length).SequenceEqual(_magic);

    private async Task<byte[]> EncryptAsync(byte[] plaintext)
    {
        var key = await GetLoginKeyAsync();
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var tag = new byte[TagSize];
        var cipher = new byte[plaintext.Length];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintext, cipher, tag);

        // layout: [magic][nonce][tag][cipher]
        var result = new byte[_magic.Length + NonceSize + TagSize + cipher.Length];
        var pos = 0;
        Buffer.BlockCopy(_magic, 0, result, pos, _magic.Length); pos += _magic.Length;
        Buffer.BlockCopy(nonce, 0, result, pos, NonceSize); pos += NonceSize;
        Buffer.BlockCopy(tag, 0, result, pos, TagSize); pos += TagSize;
        Buffer.BlockCopy(cipher, 0, result, pos, cipher.Length);
        return result;
    }

    private async Task<byte[]> DecryptAsync(byte[] data)
    {
        var key = await GetLoginKeyAsync();
        var body = data.AsSpan(_magic.Length);
        var nonce = body[..NonceSize];
        var tag = body.Slice(NonceSize, TagSize);
        var cipher = body[(NonceSize + TagSize)..];
        var plaintext = new byte[cipher.Length];

        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plaintext);

        return plaintext;
    }
}
