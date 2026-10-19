using Serilog;
using Starlight.Launcher.Services.Diagnostics;
using Starlight.Launcher.WebUI.Bridge;
using Starlight.Launcher.WebUI.Models.Diagnostics;

namespace Starlight.Launcher.Services.Bridge;

public sealed partial class Bridge : IBridge
{
    public ClientDiagnosticsSnapshot? GetClientDiagnostics() => _clientDiagnostics.GetSnapshot();

    public Task<ClientDumpResult> WriteClientDumpAsync() => _clientDiagnostics.WriteDumpAsync();

    public Task<DiagnosticsBundleResult> CreateDiagnosticsBundleAsync() => _diagnosticsBundle.CreateAsync();

    public IReadOnlyList<ConsoleLine> GetClientConsoleBuffer() => _clientConsole.Snapshot();

    public void ClearClientConsole() => _clientConsole.Clear();

    public event Action<ConsoleLine>? ClientConsoleOutput
    {
        add => _clientConsole.Output += value;
        remove => _clientConsole.Output -= value;
    }

    public bool IsDiagnosticToolSupported(DiagnosticTool tool) => DotnetTools.IsSupported(tool);

    public IReadOnlyList<ToolRunInfo> GetToolRuns() => _dotnetTools.GetRuns();

    public ToolStartResult StartTool(ToolRequest request) => _dotnetTools.Start(request);

    public void StopTool(Guid id) => _dotnetTools.Stop(id);

    public void RemoveToolRun(Guid id) => _dotnetTools.Remove(id);

    public event Action? ToolRunsChanged
    {
        add => _dotnetTools.RunsChanged += value;
        remove => _dotnetTools.RunsChanged -= value;
    }

    public RenderDocInfo GetRenderDocInfo() => _renderDoc.GetInfo();

    public async Task<bool> OpenRenderDocAsync()
    {
        try
        {
            await _renderDoc.OpenUiAsync(CancellationToken.None);
            return true;
        }
        catch (Exception e)
        {
            Log.Error(e, "Failed to open RenderDoc");
            return false;
        }
    }
}
