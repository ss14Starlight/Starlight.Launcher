namespace Starlight.Launcher.WebUI.Models.Diagnostics;

/// <summary>
/// One line of game output shown on the Development page.
/// </summary>
public sealed record ConsoleLine(DateTimeOffset Timestamp, string Text, bool IsError);

/// <summary>
/// The .NET diagnostics tools the launcher downloads and runs against the client.
/// </summary>
public enum DiagnosticTool
{
    Trace,
    Counters,
    GcDump,
    Stack,
    Monitor,
}

public enum ToolRunStatus
{
    Installing,
    Running,
    Completed,
    Failed,
    Stopped,
}

/// <summary>
/// What to run. <see cref="Profile"/> is a dotnet-trace profile list (e.g. <c>dotnet-sampled-thread-time,dotnet-common</c>),
/// <see cref="DurationSeconds"/> how long dotnet-trace / dotnet-counters record, <see cref="Format"/> the extra
/// dotnet-trace output format (NetTrace, Speedscope or Chromium).
/// </summary>
public sealed record ToolRequest(DiagnosticTool Tool, string? Profile = null, int DurationSeconds = 30, string? Format = null);

/// <summary>
/// State of one tool run. <see cref="OutputPath"/> is the file it writes, <see cref="Url"/> the address of the server
/// it started (dotnet-monitor).
/// </summary>
public sealed record ToolRunInfo(
    Guid Id,
    DiagnosticTool Tool,
    int? Pid,
    ToolRunStatus Status,
    DateTimeOffset StartedAt,
    string? OutputPath,
    string? Url,
    IReadOnlyList<string> Output);

public enum ToolStartStatus
{
    Started,
    NoClient,
    Unsupported,
    AlreadyRunning,
}

public sealed record ToolStartResult(ToolStartStatus Status, Guid? RunId = null);

public sealed record RenderDocInfo(bool Supported, bool Installed, string Version);
