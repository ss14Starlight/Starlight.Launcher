using Microsoft.AspNetCore.Components;
using MudBlazor;
using Starlight.Launcher.WebUI.Bridge;
using Starlight.Launcher.WebUI.Localization;
using Starlight.Launcher.WebUI.Models.Diagnostics;

namespace Starlight.Launcher.WebUI.Components.Atoms.Development;

/// <summary>
/// Buttons for dotnet-trace / counters / gcdump / stack / monitor against the running game, and their runs.
/// </summary>
public partial class DotnetToolsPanel : LocalizedComponentBase
{
    private static readonly (string Value, string Key)[] _traceProfiles =
    [
        ("dotnet-sampled-thread-time,dotnet-common", "development-tool-trace-profile-cpu"),
        ("gc-verbose", "development-tool-trace-profile-gc-verbose"),
        ("gc-collect", "development-tool-trace-profile-gc-collect"),
        ("dotnet-common", "development-tool-trace-profile-common"),
    ];

    [Inject] private IBridge _bridge { get; set; } = default!;
    [Inject] private ISnackbar _snackbar { get; set; } = default!;

    [Parameter] public Func<Task>? OpenCaptures { get; set; }

    private IReadOnlyList<ToolRunInfo> _runs = [];
    private readonly HashSet<Guid> _expanded = [];
    private int _refreshScheduled;

    private string _traceProfile = _traceProfiles[0].Value;
    private string _traceFormat = "Speedscope";
    private int _traceSeconds = 30;
    private int _countersSeconds = 60;

    protected override void OnInitialized()
    {
        base.OnInitialized();
        _runs = _bridge.GetToolRuns();
        _bridge.ToolRunsChanged += OnRunsChanged;
    }

    private bool Supported(DiagnosticTool tool) => _bridge.IsDiagnosticToolSupported(tool);

    private ToolRunInfo? ActiveMonitor()
        => _runs.FirstOrDefault(r => r.Tool == DiagnosticTool.Monitor && r.Status is ToolRunStatus.Installing or ToolRunStatus.Running);

    private void Start(ToolRequest request)
    {
        var result = _bridge.StartTool(request);
        switch (result.Status)
        {
            case ToolStartStatus.Started when result.RunId is { } id:
                _ = _expanded.Add(id);
                break;
            case ToolStartStatus.NoClient:
                _ = _snackbar.Add(L["client-diagnostics-not-running"], Severity.Warning);
                break;
            case ToolStartStatus.Unsupported:
                _ = _snackbar.Add(L["development-tool-unsupported"], Severity.Warning);
                break;
            case ToolStartStatus.AlreadyRunning:
                _ = _snackbar.Add(L["development-tool-already-running"], Severity.Info);
                break;
        }

        _runs = _bridge.GetToolRuns();
    }

    private void ToggleExpanded(Guid id)
    {
        if (!_expanded.Remove(id))
            _ = _expanded.Add(id);
    }

    private async Task OpenCapturesFolder()
    {
        if (OpenCaptures != null)
            await OpenCaptures();
    }

    private void OnRunsChanged()
    {
        if (Interlocked.Exchange(ref _refreshScheduled, 1) == 1)
            return;

        _ = InvokeAsync(async () =>
        {
            await Task.Delay(250);
            _ = Interlocked.Exchange(ref _refreshScheduled, 0);
            _runs = _bridge.GetToolRuns();
            StateHasChanged();
        });
    }

    private static string ToolName(DiagnosticTool tool) => tool switch
    {
        DiagnosticTool.Trace => "dotnet-trace",
        DiagnosticTool.Counters => "dotnet-counters",
        DiagnosticTool.GcDump => "dotnet-gcdump",
        DiagnosticTool.Stack => "dotnet-stack",
        DiagnosticTool.Monitor => "dotnet-monitor",
        _ => tool.ToString(),
    };

    private static Color StatusColor(ToolRunStatus status) => status switch
    {
        ToolRunStatus.Running or ToolRunStatus.Installing => Color.Info,
        ToolRunStatus.Completed => Color.Success,
        ToolRunStatus.Failed => Color.Error,
        _ => Color.Default,
    };

    public override void Dispose()
    {
        base.Dispose();
        _bridge.ToolRunsChanged -= OnRunsChanged;
        GC.SuppressFinalize(this);
    }
}
