using Starlight.Launcher.Services.EngineManager;
using Starlight.Launcher.WebUI.Models.Data;

namespace Starlight.Launcher.Tests;

public sealed class ExpandMirrorsTests
{
    private static RobustCdn Cdn(params string[] mirrors) => new(mirrors) { PublicKey = "" };

    [Fact]
    public void NoCdns_ReturnsOriginalOnly() =>
        Assert.Equal(["https://a.example/x.zip"], EngineManagerDynamic.ExpandMirrors("https://a.example/x.zip", []));

    [Fact]
    public void UrlOnMirror_ExpandsToAllMirrors_OriginalFirst()
    {
        var cdn = Cdn("https://a.example/cdn/", "https://b.example/", "https://c.example/mirror/");

        var urls = EngineManagerDynamic.ExpandMirrors("https://b.example/builds/1.zip", [cdn]);

        Assert.Equal(
            ["https://b.example/builds/1.zip", "https://a.example/cdn/builds/1.zip", "https://c.example/mirror/builds/1.zip"],
            urls);
    }

    [Fact]
    public void UrlOutsideCdn_IsNotExpanded()
    {
        var cdn = Cdn("https://a.example/", "https://b.example/");

        Assert.Equal(["https://other.example/1.zip"], EngineManagerDynamic.ExpandMirrors("https://other.example/1.zip", [cdn]));
    }

    [Fact]
    public void PrefixMatch_IsCaseInsensitive_AndDeduplicates()
    {
        var cdn = Cdn("https://A.example/", "https://b.example/");

        var urls = EngineManagerDynamic.ExpandMirrors("https://a.example/1.zip", [cdn]);

        Assert.Equal(["https://a.example/1.zip", "https://b.example/1.zip"], urls);
    }

    [Fact]
    public void OnlyMatchingCdnContributes()
    {
        var unrelated = Cdn("https://x.example/", "https://y.example/");
        var own = Cdn("https://a.example/", "https://b.example/");

        var urls = EngineManagerDynamic.ExpandMirrors("https://a.example/1.zip", [unrelated, own]);

        Assert.Equal(["https://a.example/1.zip", "https://b.example/1.zip"], urls);
    }

    [Fact]
    public void OverlappingCdns_DoNotProduceDuplicates()
    {
        var first = Cdn("https://a.example/", "https://b.example/");
        var second = Cdn("https://b.example/", "https://a.example/");

        var urls = EngineManagerDynamic.ExpandMirrors("https://a.example/1.zip", [first, second]);

        Assert.Equal(["https://a.example/1.zip", "https://b.example/1.zip"], urls);
    }
}
