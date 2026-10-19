using Starlight.Launcher.Services.DeepLink;

namespace Starlight.Launcher.Tests;

public sealed class RedactPayloadTests
{
    [Theory]
    [InlineData(LauncherActivationKind.DiscordAuth)]
    [InlineData(LauncherActivationKind.SteamAuth)]
    public void AuthPayload_QueryIsRedacted_PathIsKept(LauncherActivationKind kind)
    {
        var redacted = LauncherActivationMessage.RedactPayload(kind, "starlight://auth/discord?code=SECRET&state=SECRET2");

        Assert.Equal("starlight://auth/discord?<redacted>", redacted);
    }

    [Fact]
    public void AuthPayload_FragmentIsDropped()
    {
        var redacted = LauncherActivationMessage.RedactPayload(LauncherActivationKind.DiscordAuth, "starlight://auth/discord#token=SECRET");

        Assert.Equal("starlight://auth/discord", redacted);
    }

    [Fact]
    public void AuthPayload_WithoutQuery_IsUnchanged()
    {
        var redacted = LauncherActivationMessage.RedactPayload(LauncherActivationKind.SteamAuth, "starlight://auth/steam");

        Assert.Equal("starlight://auth/steam", redacted);
    }

    [Theory]
    [InlineData("not a uri ?code=SECRET")]
    [InlineData("auth/steam?code=SECRET")]
    public void AuthPayload_Unparseable_IsFullyRedacted(string payload)
        => Assert.Equal("<redacted>", LauncherActivationMessage.RedactPayload(LauncherActivationKind.SteamAuth, payload));

    [Theory]
    [InlineData(LauncherActivationKind.DiscordAuth)]
    [InlineData(LauncherActivationKind.Connect)]
    public void NullPayload_StaysNull(LauncherActivationKind kind)
        => Assert.Null(LauncherActivationMessage.RedactPayload(kind, null));

    [Theory]
    [InlineData(LauncherActivationKind.Connect)]
    [InlineData(LauncherActivationKind.Ping)]
    [InlineData(LauncherActivationKind.RedialWait)]
    public void NonAuthPayload_IsPassedThrough(LauncherActivationKind kind)
    {
        const string Payload = "starlight://play.example.com:1212/?password=x";

        Assert.Equal(Payload, LauncherActivationMessage.RedactPayload(kind, Payload));
    }
}
