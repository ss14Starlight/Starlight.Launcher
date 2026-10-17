namespace Starlight.Launcher.WebUI.Models.Replays;

public sealed record ReplayEntry(string FileName, string? Server, DateTime Date, int Round, long? Size, string DownloadPath);

public sealed record ReplaysResult(bool Ok, IReadOnlyList<ReplayEntry> Replays);
