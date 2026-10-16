namespace Starlight.Launcher.WebUI.Models.LocalServer;

public sealed class LocalServerRuntimeUnavailableException(string runtimeVersion, Exception? inner = null)
    : Exception($"The server needs .NET {runtimeVersion}, which is not installed and could not be downloaded. Check your internet connection.", inner)
{
    public string RuntimeVersion { get; } = runtimeVersion;
}
