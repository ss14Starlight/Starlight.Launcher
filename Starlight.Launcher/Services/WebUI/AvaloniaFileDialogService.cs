using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.Logging;
using Starlight.Launcher.Models.Helpers;
using Starlight.Launcher.WebUI.Models.Helpers;
using Starlight.Launcher.WebUI.Services;

namespace Starlight.Launcher.Services;

public sealed class AvaloniaFileDialogService : IFileDialogService
{
    private readonly ILogger<AvaloniaFileDialogService> _logger;

    public AvaloniaFileDialogService(ILogger<AvaloniaFileDialogService> logger) => _logger = logger;

    public async Task<IFileResult?> PickFileAsync(
        string filter = "Content bundles / replays\0*.zip;*.rt\0All Files\0*.*\0\0",
        CancellationToken cancel = default)
    {
        var topLevel = GetTopLevel();
        if (topLevel is null)
        {
            _logger.LogWarning("PickFileAsync: no TopLevel/MainWindow available");
            return null;
        }

        cancel.ThrowIfCancellationRequested();

        try
        {
            var results = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select file",
                AllowMultiple = false,
                FileTypeFilter = ParseWin32Filter(filter),
            });

            _logger.LogInformation("PickFileAsync: picker returned {Count} result(s)", results.Count);

            var path = results.FirstOrDefault()?.TryGetLocalPath();
            return path is null ? null : new FileResult(path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PickFileAsync failed - on Linux this is often a missing xdg-desktop-portal");
            return null;
        }
    }

    public async Task<IFileResult?> PickFolderAsync(CancellationToken cancel = default)
    {
        var topLevel = GetTopLevel();
        if (topLevel is null)
        {
            _logger.LogWarning("PickFolderAsync: no TopLevel/MainWindow available");
            return null;
        }

        cancel.ThrowIfCancellationRequested();

        try
        {
            var results = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select a folder",
                AllowMultiple = false,
            });

            _logger.LogInformation("PickFolderAsync: picker returned {Count} result(s)", results.Count);

            var path = results.FirstOrDefault()?.TryGetLocalPath();
            return path is null ? null : new FileResult(path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PickFolderAsync failed - on Linux this is often a missing xdg-desktop-portal");
            return null;
        }
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
