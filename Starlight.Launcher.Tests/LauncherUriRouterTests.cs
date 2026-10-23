using Starlight.Launcher.Services.DeepLink;

namespace Starlight.Launcher.Tests;

public sealed class LauncherUriRouterTests
{
    [Theory]
    [InlineData("starlight://auth/steam?code=x", LauncherActivationKind.SteamAuth)]
    [InlineData("starlight://AUTH/Steam/?code=x", LauncherActivationKind.SteamAuth)]
    [InlineData("starlight://auth/discord?code=x", LauncherActivationKind.DiscordAuth)]
    [InlineData("starlight://auth?code=x", LauncherActivationKind.DiscordAuth)] // legacy callback without a provider
    [InlineData("starlight://auth/unknown?code=x", LauncherActivationKind.Connect)]
    [InlineData("starlight://auth.example.com/steam", LauncherActivationKind.Connect)]
    [InlineData("starlight://play.example.com:1212", LauncherActivationKind.Connect)]
    [InlineData("ss14://play.example.com:1212", LauncherActivationKind.Connect)]
    [InlineData("ss14s://play.example.com", LauncherActivationKind.Connect)]
    [InlineData("ss14://auth/steam", LauncherActivationKind.Connect)] // server literally named "auth"
    public void Classify_RoutesByHostAndProvider(string uri, LauncherActivationKind expected)
        => Assert.Equal(expected, LauncherUriRouter.Classify(new Uri(uri)).Kind);

    [Fact]
    public void Classify_Connect_KeepsFullAddressAsPayload()
    {
        var uri = new Uri("starlight://play.example.com:1212/some/path");

        Assert.Equal(uri.ToString(), LauncherUriRouter.Classify(uri).Payload);
    }

    [Theory]
    [InlineData("starlight://auth/discord?code=SECRET&state=SECRET2")]
    [InlineData("starlight://auth/steam?code=SECRET&state=SECRET2")]
    public void AuthCallbacks_NeverLeakQueryIntoLogs(string uri)
    {
        var log = LauncherUriRouter.Classify(new Uri(uri)).ToLogString();

        Assert.DoesNotContain("SECRET", log);
        Assert.Contains("<redacted>", log);
    }
}
