namespace Starlight.Launcher.WebUI.Models.Helpers;

public interface IFileResult
{
    string FileName { get; init; }

    string FullPath { get; init; }

    Task <Stream> OpenReadAsync();

    Task<Stream> OpenWriteAsync();
}
