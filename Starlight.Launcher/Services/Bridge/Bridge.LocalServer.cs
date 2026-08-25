using Starlight.Launcher.WebUI.Bridge;
using Starlight.Launcher.WebUI.Models.LocalServer;

namespace Starlight.Launcher.Services.Bridge;

public sealed partial class Bridge : IBridge
{
    public event Action<LocalServerLogLine>? LocalServerOutput
    {
        add => _localServer.OutputReceived += value;
        remove => _localServer.OutputReceived -= value;
    }

    public event Action<LocalServerState>? LocalServerStateChanged
    {
        add => _localServer.StateChanged += value;
        remove => _localServer.StateChanged -= value;
    }

    public Task<LocalServerLatestBuildResult> FetchLocalServerLatestBuildAsync(string manifestUrl, CancellationToken cancel = default)
        => _localServer.GetLatestBuildAsync(manifestUrl, cancel);

    public Task StartLocalServerAsync(string sourceName, string manifestUrl, IReadOnlyList<ServerCVarValue> cvarOverrides, CancellationToken cancel = default)
        => _localServer.StartAsync(sourceName, manifestUrl, cvarOverrides, cancel);

    public void StopLocalServer() => _localServer.Stop();

    public LocalServerState GetLocalServerState() => _localServer.CurrentState;

    public IReadOnlyList<LocalServerLogLine> GetLocalServerConsoleBuffer() => _localServer.GetConsoleBuffer();

    public void OpenLocalServerFolder()
    {
        if (_localServer.LastInstallDirectory is { } dir)
            OpenPath(dir);
    }

    public bool SendLocalServerCommand(string text) => _localServer.SendCommand(text);

    public Task ClearLocalServerInstallsAsync() => _localServer.ClearInstalledServersAsync();
}
