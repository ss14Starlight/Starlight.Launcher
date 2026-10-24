using System.Diagnostics;
using Robust.Launcher.Api.Utility;

namespace Starlight.Launcher.Tests;

[Collection(nameof(ThrottledStreamTests))]
public sealed class ThrottledStreamTests : IDisposable
{
    public void Dispose() => DownloadThrottle.LimitProvider = null;

    [Fact]
    public async Task Unlimited_PassesDataThrough()
    {
        DownloadThrottle.LimitProvider = () => 0;
        var data = Enumerable.Range(0, 100_000).Select(i => (byte)i).ToArray();

        var copy = new MemoryStream();
        await new ThrottledStream(new MemoryStream(data)).CopyToAsync(copy, TestContext.Current.CancellationToken);

        Assert.Equal(data, copy.ToArray());
    }

    [Fact]
    public async Task Limit_SlowsReadsDown()
    {
        const long Limit = 200 * 1024;
        DownloadThrottle.LimitProvider = () => Limit;
        var data = new byte[100 * 1024];

        var sw = Stopwatch.StartNew();
        var copy = new MemoryStream();
        await new ThrottledStream(new MemoryStream(data)).CopyToAsync(copy, TestContext.Current.CancellationToken);
        sw.Stop();

        Assert.Equal(data.Length, copy.Length);
        Assert.True(sw.Elapsed >= TimeSpan.FromSeconds(0.2), $"took {sw.Elapsed}");
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3), $"took {sw.Elapsed}");
    }
}
