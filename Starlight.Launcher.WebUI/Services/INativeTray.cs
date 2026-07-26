using Starlight.Launcher.WebUI.Models.Tray;

namespace Starlight.Launcher.WebUI.Services;

public interface INativeTray : IDisposable
{
    void Initialize(TrayOptions options, IReadOnlyList<TrayMenuItem> menu);
    void ShowWindow();
    void HideWindow();
    void UpdateTooltip(string text);
    bool IsWindowVisible { get; }
    event EventHandler? IconActivated;
}
