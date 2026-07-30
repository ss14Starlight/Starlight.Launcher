using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.Logging;
using Starlight.Launcher.Models.Helpers;
using Starlight.Launcher.WebUI.Models.Helpers;
using Starlight.Launcher.WebUI.Services;

namespace Starlight.Launcher.Services;

/// <summary>
/// Provides file and folder picker dialogs using Avalonia's storage API.
/// </summary>
public sealed class AvaloniaFileDialogService : IFileDialogService
{
    private readonly ILogger<AvaloniaFileDialogService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AvaloniaFileDialogService"/> class.
    /// </summary>
    public AvaloniaFileDialogService(ILogger<AvaloniaFileDialogService> logger) => _logger = logger;

    /// <summary>
    /// Displays a file picker dialog and returns the selected file.
    /// </summary>
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

        _logger.LogInformation(
            "CanOpen={CanOpen}, CanPickFolder={CanPickFolder}",
            topLevel.StorageProvider.CanOpen,
            topLevel.StorageProvider.CanPickFolder);

        try
        {
            var pickerTask = topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select file",
                AllowMultiple = false,
                FileTypeFilter = ParseWin32Filter(filter),
            });

            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(30), cancel);
            var completed = await Task.WhenAny(pickerTask, timeoutTask);

            if (completed == timeoutTask)
            {
                _logger.LogWarning("PickFileAsync: OpenFilePickerAsync did not return within 30s. ");
                return null;
            }

            var results = await pickerTask;
            _logger.LogInformation("PickFileAsync: picker returned {Count} result(s)", results.Count);

            var path = results.FirstOrDefault()?.TryGetLocalPath();
            return path is null ? null : new FileResult(path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PickFileAsync failed");
            return null;
        }
    }

    /// <summary>
    /// Displays a folder picker dialog and returns the selected folder.
    /// </summary>
    public async Task<IFileResult?> PickFolderAsync(CancellationToken cancel = default)
    {
        var topLevel = GetTopLevel();
        if (topLevel is null)
        {
            _logger.LogWarning("PickFolderAsync: no TopLevel/MainWindow available");
            return null;
        }

        _logger.LogInformation(
            "CanOpen={CanOpen}, CanPickFolder={CanPickFolder}",
            topLevel.StorageProvider.CanOpen,
            topLevel.StorageProvider.CanPickFolder);

        cancel.ThrowIfCancellationRequested();

        try
        {
            var pickerTask = topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select a folder",
                AllowMultiple = false,
            });

            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(30), cancel);
            var completed = await Task.WhenAny(pickerTask, timeoutTask);

            if (completed == timeoutTask)
            {
                _logger.LogWarning("PickFolderAsync: OpenFolderPickerAsync did not return within 30s");
                return null;
            }

            var results = await pickerTask;
            _logger.LogInformation("PickFolderAsync: picker returned {Count} result(s)", results.Count);

            var path = results.FirstOrDefault()?.TryGetLocalPath();
            return path is null ? null : new FileResult(path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PickFolderAsync failed");
            return null;
        }
    }

    /// <summary>
    /// Gets the application's main window.
    /// </summary>
    private static TopLevel? GetTopLevel() =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    /// <summary>
    /// Converts a Win32-style file filter string into Avalonia file picker filters.
    /// </summary>
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
