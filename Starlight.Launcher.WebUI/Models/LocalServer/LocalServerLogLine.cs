namespace Starlight.Launcher.WebUI.Models.LocalServer;

public sealed record LocalServerLogLine(
    DateTimeOffset Timestamp,
    string Text,
    bool IsError
);
