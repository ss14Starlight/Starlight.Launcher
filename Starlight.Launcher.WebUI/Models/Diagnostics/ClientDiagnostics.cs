namespace Starlight.Launcher.WebUI.Models.Diagnostics;

/// <summary>
/// One runtime counter of the running client, as the .NET runtime reports it (same data as <c>dotnet-counters</c>).
/// </summary>
public sealed record RuntimeCounter(string Name, string DisplayName, double Value, string Units);

/// <summary>
/// Live view of the newest running client. <see cref="Counters"/> stays empty until the first interval arrives.
/// </summary>
public sealed record ClientDiagnosticsSnapshot(int Pid, DateTimeOffset UpdatedAt, IReadOnlyList<RuntimeCounter> Counters);

public enum ClientDumpStatus
{
    Success,
    NoClient,
    Failed,
}

public sealed record ClientDumpResult(ClientDumpStatus Status, string? Path = null);

public sealed record DiagnosticsBundleResult(bool Success, string? Path = null);
