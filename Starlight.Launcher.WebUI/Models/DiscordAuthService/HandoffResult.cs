namespace Starlight.Launcher.WebUI.Models.DiscordAuthService;

public sealed record HandoffResult(string Token, string? RefreshToken, string? SessionId);
