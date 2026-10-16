using Starlight.Launcher.Services.GameLaunch;

namespace Starlight.Launcher.Tests;

public sealed class CVarOverridesTests
{
    private static (string, string)[] Parse(string? raw) => [.. Connector.ParseCVarOverrides(raw)];

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \n\r\n ; ")]
    public void EmptyInput_YieldsNothing(string? raw) => Assert.Empty(Parse(raw));

    [Fact]
    public void SplitsOnSemicolonsAndAllNewlineKinds_AndTrims()
    {
        var result = Parse(" a.b = 1 ;c.d=2\r\ne.f=3\ng.h=4\r");

        Assert.Equal([("a.b", "1"), ("c.d", "2"), ("e.f", "3"), ("g.h", "4")], result);
    }

    [Fact]
    public void OnlyFirstEqualsSeparatesNameFromValue()
    {
        var result = Parse("net.url=https://example.com/?a=b");

        Assert.Equal([("net.url", "https://example.com/?a=b")], result);
    }

    [Fact]
    public void SkipsCommentsAndMalformedEntries_KeepsTheRest()
    {
        var result = Parse("""
            # comment=ignored
            noequals
            =value
            bad name=1
            good.one=1
            """);

        Assert.Equal([("good.one", "1")], result);
    }
}
