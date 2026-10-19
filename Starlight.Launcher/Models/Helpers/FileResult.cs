using Starlight.Launcher.WebUI.Models.Helpers;

namespace Starlight.Launcher.Models.Helpers;

public sealed class FileResult(string fullPath) : IFileResult
{
    public string FileName { get; init; } = Path.GetFileName(fullPath);
    public string FullPath { get; init; } = fullPath;

    public Task<Stream> OpenReadAsync()
        => Task.FromResult<Stream>(File.OpenRead(FullPath));

    public Task<Stream> OpenWriteAsync()
    {
        var directory = Path.GetDirectoryName(FullPath);
        if (!string.IsNullOrEmpty(directory))
            _ = Directory.CreateDirectory(directory);

        return Task.FromResult<Stream>(File.Open(FullPath, FileMode.OpenOrCreate, FileAccess.Write));
    }
}
