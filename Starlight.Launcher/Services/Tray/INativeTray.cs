using Starlight.Launcher.Models.Tray;

namespace Starlight.Launcher.Services;

public interface INativeTray : IDisposable
{
    void Initialize(TrayOptions options, IReadOnlyList<TrayMenuItem> menu);
    void ShowWindow();
    void HideWindow();
    void UpdateTooltip(string text);
    bool IsWindowVisible { get; }
    event EventHandler? IconActivated;
}
