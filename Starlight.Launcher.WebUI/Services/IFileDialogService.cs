using Starlight.Launcher.WebUI.Models.Helpers;

namespace Starlight.Launcher.WebUI.Services;

public interface IFileDialogService
{
    Task<IFileResult?> PickFileAsync(string filter = "Content bundles / replays\0*.zip;*.rt\0All Files\0*.*\0\0", CancellationToken cancel = default);
    Task<IFileResult?> PickFolderAsync(CancellationToken cancel = default);
}
