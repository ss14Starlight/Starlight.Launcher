namespace Starlight.Launcher.WebUI.Models;

public interface IFileResult
{
    string FileName { get; }

    Task<Stream> OpenReadAsync();

    Task<Stream> OpenWriteAsync();
}
