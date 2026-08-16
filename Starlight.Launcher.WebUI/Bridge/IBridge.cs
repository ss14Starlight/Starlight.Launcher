using Starlight.Launcher.WebUI.Models.Helpers;

namespace Starlight.Launcher.WebUI.Bridge;

public partial interface IBridge
{
    void OpenBrowser(string url);

    void OpenPath(string path);

    Task<IFileResult?> PickFileAsync(
        string filter = "Content bundles / replays\0*.zip;*.rt\0All Files\0*.*\0\0",
        CancellationToken cancel = default);

    void InitializeWindow();
    void MinimizeWindow();
    void ToggleMaximizeWindow();
    void CloseWindow();
    void BeginWindowDrag();
    bool IsWindowMaximized { get; }
    event Action? WindowStateChanged;
}
