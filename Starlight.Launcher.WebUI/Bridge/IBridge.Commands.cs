namespace Starlight.Launcher.WebUI.Bridge;

public partial interface IBridge
{
    event Func<string, Task>? ConnectRequested;
}
