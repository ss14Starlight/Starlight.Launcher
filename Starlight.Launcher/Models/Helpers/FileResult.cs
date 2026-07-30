using Starlight.Launcher.WebUI.Models.Helpers;

namespace Starlight.Launcher.Models.Helpers;

public sealed class FileResult : IFileResult
{
    public string FileName { get; init; }
    public string FullPath { get; init; }

    public FileResult(string fullPath)
    {
        FullPath = fullPath;
        FileName = Path.GetFileName(fullPath);
    }

    public Task<Stream> OpenReadAsync() =>
        Task.FromResult<Stream>(File.OpenRead(FullPath));

    public Task<Stream> OpenWriteAsync()
    {
        var directory = Path.GetDirectoryName(FullPath);
        if (!string.IsNullOrEmpty(directory))
            _ = Directory.CreateDirectory(directory);

        return Task.FromResult<Stream>(File.Open(FullPath, FileMode.OpenOrCreate, FileAccess.Write));
    }
}
