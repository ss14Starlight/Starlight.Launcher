namespace Starlight.Launcher.Services.Settings;

/// <summary>
///     Reading the login key with a little patience.
/// </summary>
internal static class LoginKeyIo
{
    private const int Attempts = 4;
    private static readonly TimeSpan _retryDelay = TimeSpan.FromMilliseconds(150);

    public static Task<string> ReadTextWithRetryAsync(string path)
        => WithRetryAsync(() => File.ReadAllTextAsync(path));

    public static Task<byte[]> ReadBytesWithRetryAsync(string path)
        => WithRetryAsync(() => File.ReadAllBytesAsync(path));

    private static async Task<T> WithRetryAsync<T>(Func<Task<T>> read)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await read();
            }
            catch (Exception ex) when (attempt < Attempts && ex is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(_retryDelay * attempt);
            }
        }
    }
}
