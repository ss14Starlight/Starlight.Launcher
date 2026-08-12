using System;

namespace Robust.Launcher.Api.Models.Data;

public class LoginInfo
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = "";
    public LoginToken? Token { get; set; }
    public LoginToken? DiscordToken { get; set; }
    public LoginToken? SteamToken { get; set; }
    public string? DiscordRefreshToken { get; set; }
    public string? DiscordSessionId { get; set; }
    public string? AuthServerUrl { get; set; }

    public override string ToString()
        => $"{Username}/{UserId}";
}
