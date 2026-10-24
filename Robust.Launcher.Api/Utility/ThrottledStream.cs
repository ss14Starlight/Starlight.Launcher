using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Robust.Launcher.Api.Utility;

public static class DownloadThrottle
{
    public static Func<long>? LimitProvider { get; set; }

    private static readonly Lock _lock = new();
    private static double s_budget;
    private static long s_lastTimestamp;

    public static long CurrentLimit => LimitProvider?.Invoke() ?? 0;

    internal static TimeSpan Consume(int bytes, long limit)
    {
        lock (_lock)
        {
            var now = Stopwatch.GetTimestamp();
            var elapsed = Stopwatch.GetElapsedTime(s_lastTimestamp, now).TotalSeconds;
            s_lastTimestamp = now;

            var burst = limit / 4d;
            s_budget = Math.Min(burst, s_budget + (elapsed * limit)) - bytes;

            return s_budget >= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(-s_budget / limit);
        }
    }

    internal static int ChunkSize(int requested, long limit)
        => (int)Math.Min(requested, Math.Max(4096, limit / 10));
}

public sealed class ThrottledStream(Stream baseStream) : Stream
{
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var limit = DownloadThrottle.CurrentLimit;
        if (limit <= 0 || buffer.Length == 0)
            return await baseStream.ReadAsync(buffer, cancellationToken);

        var read = await baseStream.ReadAsync(buffer[..DownloadThrottle.ChunkSize(buffer.Length, limit)], cancellationToken);
        if (read > 0)
        {
            var delay = DownloadThrottle.Consume(read, limit);
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken);
        }

        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override int Read(byte[] buffer, int offset, int count)
        => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var limit = DownloadThrottle.CurrentLimit;
        if (limit <= 0 || buffer.Length == 0)
            return baseStream.Read(buffer);

        var read = baseStream.Read(buffer[..DownloadThrottle.ChunkSize(buffer.Length, limit)]);
        if (read > 0)
        {
            var delay = DownloadThrottle.Consume(read, limit);
            if (delay > TimeSpan.Zero)
                Thread.Sleep(delay);
        }

        return read;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            baseStream.Dispose();

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await baseStream.DisposeAsync();
        await base.DisposeAsync();
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
