using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace Starlight.Launcher.Services.Settings;

public sealed class FileKeyProvider : ILoginKeyProvider
{
    private readonly ILogger<FileKeyProvider> _logger;

    public FileKeyProvider(ILogger<FileKeyProvider> logger) => _logger = logger;

    public async Task<byte[]> GetOrCreateKeyAsync(string keyPath)
    {
        _ = Directory.CreateDirectory(Path.GetDirectoryName(keyPath)!);

        if (File.Exists(keyPath))
        {
            string text;
            try
            {
                text = await LoginKeyIo.ReadTextWithRetryAsync(keyPath);
            }
            catch (Exception ex)
            {
                // Antivirus, a locked file, a slow network drive... Regenerating here would throw
                // away every saved login, so refuse instead and let the next start try again.
                _logger.LogError(ex, "Could not read the login key at {path}; leaving it alone", keyPath);
                throw;
            }

            try
            {
                var key = Convert.FromBase64String(text);
                _logger.LogInformation("Loaded login key from {path} fp={fp}", keyPath, Fp(key));
                return key;
            }
            catch (FormatException ex)
            {
                // The file really is garbage; there is nothing to preserve.
                _logger.LogError(ex, "Login key at {path} is corrupt; regenerating (re-auth required).", keyPath);
                TryDelete(keyPath);
            }
        }

        return await CreateAndStoreAsync(keyPath);
    }

    private async Task<byte[]> CreateAndStoreAsync(string keyPath)
    {
        var newKey = RandomNumberGenerator.GetBytes(32);

        var tmp = keyPath + ".tmp";
        await File.WriteAllTextAsync(tmp, Convert.ToBase64String(newKey));

        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite);

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
