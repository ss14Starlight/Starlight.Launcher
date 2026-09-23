using Starlight.Launcher.Models.Tray;

namespace Starlight.Launcher.Services;

public interface INativeTray : IDisposable
{
    void Initialize(TrayOptions options, IReadOnlyList<TrayMenuItem> menu, TrayPalette palette);
    void SetMenu(IReadOnlyList<TrayMenuItem> menu);
    void ApplyPalette(TrayPalette palette);
    void ShowWindow();
    void HideWindow();
    void UpdateTooltip(string text);
    bool IsWindowVisible { get; }
    event EventHandler? IconActivated;
}
