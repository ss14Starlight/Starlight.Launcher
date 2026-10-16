using Starlight.Launcher.Services.EngineManager;

namespace Starlight.Launcher.Tests;

public sealed class LocalWriteStreamTests : IDisposable
{
    private readonly string _path = Path.GetTempFileName();

    public void Dispose() => File.Delete(_path);

    [Fact]
    public async Task WritesPassThroughToFile()
    {
        await using (var file = new FileStream(_path, FileMode.Create, FileAccess.ReadWrite))
        {
            var sink = new LocalWriteStream(file);
            await sink.WriteAsync(new byte[] { 1, 2, 3 }, TestContext.Current.CancellationToken);
            sink.Reset();
            await sink.WriteAsync(new byte[] { 4, 5 }, TestContext.Current.CancellationToken);
            await sink.FlushAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal([4, 5], await File.ReadAllBytesAsync(_path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LocalWriteFailure_IsTaggedAsLocal()
    {
        await using var file = new FileStream(_path, FileMode.Open, FileAccess.Read);
        var sink = new LocalWriteStream(file);

        var e = await Assert.ThrowsAsync<LocalWriteException>(
            () => sink.WriteAsync(new byte[] { 1 }, TestContext.Current.CancellationToken).AsTask());
        Assert.NotNull(e.InnerException);
        Assert.Throws<LocalWriteException>(sink.Reset);
    }

    [Fact]
    public async Task Cancellation_IsNotTaggedAsLocal()
    {
        await using var file = new FileStream(_path, FileMode.Create, FileAccess.ReadWrite);
        var sink = new LocalWriteStream(file);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sink.WriteAsync(new byte[] { 1 }, new CancellationToken(true)).AsTask());
    }
}
