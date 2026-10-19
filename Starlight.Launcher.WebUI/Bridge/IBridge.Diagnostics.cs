using Starlight.Launcher.WebUI.Models.Diagnostics;

namespace Starlight.Launcher.WebUI.Bridge;

public partial interface IBridge
{
    /// <summary>
    /// Runtime counters of the newest running client, null when no client runs. Polling keeps the counter stream alive.
    /// </summary>
    ClientDiagnosticsSnapshot? GetClientDiagnostics();

    Task<ClientDumpResult> WriteClientDumpAsync();

    Task<DiagnosticsBundleResult> CreateDiagnosticsBundleAsync();

    IReadOnlyList<ConsoleLine> GetClientConsoleBuffer();

    void ClearClientConsole();

    /// <summary>
    /// Raised from background threads for every line the game prints.
    /// </summary>
    event Action<ConsoleLine>? ClientConsoleOutput;

    bool IsDiagnosticToolSupported(DiagnosticTool tool);

    IReadOnlyList<ToolRunInfo> GetToolRuns();

    ToolStartResult StartTool(ToolRequest request);

    void StopTool(Guid id);

    void RemoveToolRun(Guid id);

    /// <summary>
    /// Raised from background threads when a tool run changes or prints something.
    /// </summary>
    event Action? ToolRunsChanged;

    RenderDocInfo GetRenderDocInfo();

    Task<bool> OpenRenderDocAsync();
}
