namespace Starlight.Launcher.WebUI.Models.Helpers;

public interface IFileResult
{
    string FileName { get; }

    Task<Stream> OpenReadAsync();

    Task<Stream> OpenWriteAsync();
}
