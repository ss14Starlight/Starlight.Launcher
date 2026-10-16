using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Starlight.Launcher.Services.Auth;
using Starlight.Launcher.Services.Settings;

namespace Starlight.Launcher.Tests;

public sealed class LoginsEncryptionTests
{
    private static readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    private static readonly byte[] _plaintext = Encoding.UTF8.GetBytes("""[{"UserId":"00000000-0000-0000-0000-000000000001","Username":"user"}]""");

    [Fact]
    public void RoundTrip_RestoresPlaintext()
    {
        var encrypted = LoginsCipher.Encrypt(_key, _plaintext);

        Assert.True(LoginsCipher.LooksEncrypted(encrypted));
        Assert.Equal(_plaintext, LoginsCipher.Decrypt(_key, encrypted));
    }

    [Fact]
    public void Encrypt_UsesFreshNonceEachTime()
    {
        Assert.NotEqual(LoginsCipher.Encrypt(_key, _plaintext), LoginsCipher.Encrypt(_key, _plaintext));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("""[{"UserId":"00000000-0000-0000-0000-000000000001"}]""")]
    [InlineData("SLE1")] // magic alone, too short to hold nonce + tag
    public void LegacyOrTruncatedFiles_AreNotTreatedAsEncrypted(string content)
    {
        Assert.False(LoginsCipher.LooksEncrypted(Encoding.UTF8.GetBytes(content)));
    }

    [Fact]
    public void Decrypt_WithWrongKey_ThrowsCryptographicException()
    {
        var encrypted = LoginsCipher.Encrypt(_key, _plaintext);

        _ = Assert.ThrowsAny<CryptographicException>(() => LoginsCipher.Decrypt(RandomNumberGenerator.GetBytes(32), encrypted));
    }

    [Fact]
    public void Decrypt_TamperedData_ThrowsCryptographicException()
    {
        var encrypted = LoginsCipher.Encrypt(_key, _plaintext);
        encrypted[^1] ^= 0x01;

        _ = Assert.ThrowsAny<CryptographicException>(() => LoginsCipher.Decrypt(_key, encrypted));
    }

    [Fact]
    public async Task FileKeyProvider_ReusesStoredKey_SoLoginsSurviveRestart()
    {
        var dir = Path.Combine(Path.GetTempPath(), "starlight-tests-" + Guid.NewGuid().ToString("N"));
        var keyPath = Path.Combine(dir, "logins.key");
        try
        {
            var first = await new FileKeyProvider(NullLogger<FileKeyProvider>.Instance).GetOrCreateKeyAsync(keyPath);
            var second = await new FileKeyProvider(NullLogger<FileKeyProvider>.Instance).GetOrCreateKeyAsync(keyPath);

            Assert.Equal(32, first.Length);
            Assert.Equal(first, second);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
