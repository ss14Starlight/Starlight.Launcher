
using Avalonia.Threading;
using Starlight.Launcher.WebUI.Bridge;

namespace Starlight.Launcher.Services.Bridge;

public sealed partial class Bridge : IBridge
{
    public void HideWindow() => Dispatcher.UIThread.Post(_tray.HideWindow);
}
