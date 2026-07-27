namespace Starlight.Launcher.WebUI.Services;

public class AppState
{
    public event Action? OnChange;

    public void CallUpdate() => OnChange?.Invoke();
}
