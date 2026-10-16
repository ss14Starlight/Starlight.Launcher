using Starlight.Launcher.Services.LocalServer.Dotnet;

namespace Starlight.Launcher.Tests;

public sealed class DotnetRuntimeTests
{
    private static RuntimeVersion V(string text) =>
        RuntimeVersion.TryParse(text, out var v) ? v : throw new FormatException(text);

    [Theory]
    [InlineData("10.0.13", 10, 0, 13, null)]
    [InlineData("10.0", 10, 0, 0, null)]
    [InlineData("11.0.0-rc.2.26504.105", 11, 0, 0, "rc.2.26504.105")]
    [InlineData("10.0.1+abcdef", 10, 0, 1, null)]
    public void ParsesVersions(string text, int major, int minor, int patch, string? prerelease) =>
        Assert.Equal(new RuntimeVersion(major, minor, patch, prerelease), V(text));

    [Theory]
    [InlineData("")]
    [InlineData("10")]
    [InlineData("a.b.c")]
    [InlineData("10.0.0-")]
    public void RejectsInvalidVersions(string text) =>
        Assert.False(RuntimeVersion.TryParse(text, out _));

    [Theory]
    [InlineData("10.0.1", "10.0.2")]
    [InlineData("10.0.0-rc.1", "10.0.0")]
    [InlineData("10.0.0-rc.2", "10.0.0-rc.10")]
    [InlineData("10.0.0-preview.7", "10.0.0-rc.1")]
    [InlineData("9.0.9", "10.0.0")]
    public void OrdersVersions(string lower, string higher) =>
        Assert.True(V(lower) < V(higher));

    [Theory]
    [InlineData(RollForwardPolicy.Minor, "10.0.13", true)]
    [InlineData(RollForwardPolicy.Minor, "10.1.0", true)]
    [InlineData(RollForwardPolicy.Minor, "11.0.0", false)]
    [InlineData(RollForwardPolicy.Minor, "9.0.9", false)]
    [InlineData(RollForwardPolicy.Minor, "10.0.0", false)]
    [InlineData(RollForwardPolicy.Minor, "10.0.5-rc.1", false)]
    [InlineData(RollForwardPolicy.LatestPatch, "10.1.0", false)]
    [InlineData(RollForwardPolicy.Major, "11.0.0", true)]
    [InlineData(RollForwardPolicy.Disable, "10.0.2", true)]
    [InlineData(RollForwardPolicy.Disable, "10.0.3", false)]
    public void RollForward(RollForwardPolicy policy, string candidate, bool expected) =>
        Assert.Equal(expected, new FrameworkRequirement("Microsoft.NETCore.App", V("10.0.2"), policy).IsSatisfiedBy(V(candidate)));

    [Fact]
    public void ParsesFrameworkDependentConfig()
    {
        var req = DotnetRuntimeRequirements.Parse("""
            {
              "runtimeOptions": {
                "tfm": "net10.0",
                "rollForward": "LatestPatch",
                "framework": { "name": "Microsoft.NETCore.App", "version": "10.0.0" }
              }
            }
            """);

        Assert.NotNull(req);
        var fx = Assert.Single(req.Frameworks);
        Assert.Equal(DotnetRuntimeRequirements.NetCoreApp, fx.Name);
        Assert.Equal(RollForwardPolicy.LatestPatch, fx.RollForward);
        Assert.False(req.NeedsAspNetCore);
        Assert.Equal("10.0", req.DisplayVersion);
    }

    [Fact]
    public void AspNetCoreImpliesBaseRuntime()
    {
        var req = DotnetRuntimeRequirements.Parse("""
            {
              "runtimeOptions": {
                "frameworks": [ { "name": "Microsoft.AspNetCore.App", "version": "10.0.0", "rollForward": "Major" } ]
              }
            }
            """);

        Assert.NotNull(req);
        Assert.True(req.NeedsAspNetCore);
        Assert.Equal(RollForwardPolicy.Major, req.NetCore.RollForward);
        Assert.Equal(V("10.0.0"), req.NetCore.Version);
    }

    [Fact]
    public void SelfContainedConfigNeedsNoRuntime() =>
        Assert.Null(DotnetRuntimeRequirements.Parse("""
            {
              "runtimeOptions": {
                "includedFrameworks": [ { "name": "Microsoft.NETCore.App", "version": "10.0.13" } ]
              }
            }
            """));

    [Fact]
    public void RequirementsCheckEveryFramework()
    {
        var req = new DotnetRuntimeRequirements([
            new FrameworkRequirement(DotnetRuntimeRequirements.NetCoreApp, V("10.0.0"), RollForwardPolicy.Minor),
            new FrameworkRequirement(DotnetRuntimeRequirements.AspNetCoreApp, V("10.0.0"), RollForwardPolicy.Minor)
        ]);

        Assert.False(req.IsSatisfiedBy(new Dictionary<string, List<RuntimeVersion>>
        {
            [DotnetRuntimeRequirements.NetCoreApp] = [V("10.0.13")]
        }));
        Assert.True(req.IsSatisfiedBy(new Dictionary<string, List<RuntimeVersion>>
        {
            [DotnetRuntimeRequirements.NetCoreApp] = [V("9.0.1"), V("10.0.13")],
            [DotnetRuntimeRequirements.AspNetCoreApp] = [V("10.0.13")]
        }));
    }

    [Fact]
    public async Task DownloadsAndReusesRuntime()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("STARLIGHT_NETWORK_TESTS") == "1", "Network tests disabled.");

        var cache = Path.Combine(Path.GetTempPath(), "starlight-dotnet-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var http = new HttpClient();
            var manager = new DotnetRuntimeManager(http, () => cache);
            var req = new DotnetRuntimeRequirements([
                new FrameworkRequirement(DotnetRuntimeRequirements.NetCoreApp, V("8.0.1"), RollForwardPolicy.Disable)
            ]);

            var progressSeen = false;
            var install = await manager.EnsureRuntimeAsync(req, _ => progressSeen = true, TestContext.Current.CancellationToken);

            Assert.True(progressSeen);
            Assert.True(install.IsLauncherManaged);
            Assert.True(File.Exists(install.Muxer));
            Assert.Equal(V("8.0.1"), install.NetCoreVersion);

            var again = await manager.EnsureRuntimeAsync(req, _ => Assert.Fail("Should reuse the cache."), TestContext.Current.CancellationToken);
            Assert.Equal(install.Root, again.Root);

            await manager.PruneCacheAsync([]);
            Assert.Empty(Directory.EnumerateDirectories(cache));
        }
        finally
        {
            if (Directory.Exists(cache))
                Directory.Delete(cache, recursive: true);
        }
    }
}
