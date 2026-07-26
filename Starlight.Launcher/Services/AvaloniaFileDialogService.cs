using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Starlight.Launcher.Models.Helpers;
using Starlight.Launcher.WebUI.Models.Helpers;
using Starlight.Launcher.WebUI.Services;

namespace Starlight.Launcher.Services;

public sealed class AvaloniaFileDialogService : IFileDialogService
{
    public async Task<IFileResult?> PickFileAsync(
        string filter = "Content bundles / replays\0*.zip;*.rt\0All Files\0*.*\0\0",
        CancellationToken cancel = default)
    {
        var topLevel = GetTopLevel();
        if (topLevel is null) return null;

        cancel.ThrowIfCancellationRequested();

        var results = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select file",
            AllowMultiple = false,
            FileTypeFilter = ParseWin32Filter(filter),
        });

        var path = results.FirstOrDefault()?.TryGetLocalPath();
        return path is null ? null : new FileResult(path);
    }

    public async Task<IFileResult?> PickFolderAsync(CancellationToken cancel = default)
    {
        var topLevel = GetTopLevel();
        if (topLevel is null) return null;

        cancel.ThrowIfCancellationRequested();

        var results = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select a folder",
            AllowMultiple = false,
        });

        var path = results.FirstOrDefault()?.TryGetLocalPath();
        return path is null ? null : new FileResult(path);
    }

    private static TopLevel? GetTopLevel() =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    private static FilePickerFileType[] ParseWin32Filter(string filter)
    {
        var parts = filter.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var types = new List<FilePickerFileType>();

        for (var i = 0; i + 1 < parts.Length; i += 2)
        {
            var patterns = parts[i + 1].Split(';', StringSplitOptions.RemoveEmptyEntries);
            types.Add(new FilePickerFileType(parts[i]) { Patterns = patterns });
        }

        return types.ToArray();
    }
}
