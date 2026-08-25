using Starlight.Launcher.WebUI.Models.LocalServer;

namespace Starlight.Launcher.WebUI.Bridge;

public partial interface IBridge
{
    event Action<LocalServerLogLine>? LocalServerOutput;

    event Action<LocalServerState>? LocalServerStateChanged;

    Task<LocalServerLatestBuildResult> FetchLocalServerLatestBuildAsync(string manifestUrl, CancellationToken cancel = default);

    Task StartLocalServerAsync(string sourceName, string manifestUrl, IReadOnlyList<ServerCVarValue> cvarOverrides, CancellationToken cancel = default);

    void StopLocalServer();

    LocalServerState GetLocalServerState();

    IReadOnlyList<LocalServerLogLine> GetLocalServerConsoleBuffer();

    void OpenLocalServerFolder();

    bool SendLocalServerCommand(string text);

    Task ClearLocalServerInstallsAsync();
}
