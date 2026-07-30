namespace Starlight.Launcher.WebUI.Models.Data;

public record AcceptedPrivacyPolicy
{
    public required string Version { get; init; }
    public DateTimeOffset AcceptedTime { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastConnected { get; init; } = DateTimeOffset.UtcNow;
}
