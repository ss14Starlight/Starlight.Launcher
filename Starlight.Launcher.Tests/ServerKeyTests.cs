using System.Text.Json;
using Starlight.Launcher.Services.DeepLink;

namespace Starlight.Launcher.Tests;

public sealed class ServerKeyTests
{
    [Theory]
    [InlineData("ss14://play.example.com")]
    [InlineData("ss14://play.example.com:1212/")]
    [InlineData("ss14://PLAY.example.com:1212/?password=x")]
    [InlineData("play.example.com")]
    public void EquivalentAddresses_ShareKey(string address)
        => Assert.Equal(LauncherCommands.GetServerKey("ss14://play.example.com:1212"), LauncherCommands.GetServerKey(address));

    [Theory]
    [InlineData("ss14://play.example.com:1213")]
    [InlineData("ss14s://play.example.com")]
    [InlineData("ss14://play.example.com/other")]
    [InlineData("ss14://evil.example.com")]
    public void DifferentServers_HaveDifferentKeys(string address)
        => Assert.NotEqual(LauncherCommands.GetServerKey("ss14://play.example.com"), LauncherCommands.GetServerKey(address));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://example.com")]
    public void InvalidAddress_HasNoKey(string? address)
        => Assert.Null(LauncherCommands.GetServerKey(address));

    [Fact]
    public void InternalFlag_DoesNotSurviveThePipe()
    {
        var message = new LauncherActivationMessage(LauncherActivationKind.Connect, "ss14://x") { Internal = true };
        var json = JsonSerializer.Serialize(message);

        Assert.DoesNotContain("Internal", json);
        Assert.False(JsonSerializer.Deserialize<LauncherActivationMessage>("""{"Kind":1,"Payload":"ss14://x","Internal":true}""")!.Internal);
    }
}
