using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Robust.Launcher.Api.Models;

namespace Robust.Launcher.Api.Utility;

/// <summary>
/// Read-only stream wrapper that fails a read if no data arrives within <see cref="Timeout"/>.
/// </summary>
public sealed class StallTimeoutStream(Stream baseStream, TimeSpan timeout) : Stream
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    public StallTimeoutStream(Stream baseStream) : this(baseStream, DefaultTimeout)
    {
    }

    public TimeSpan Timeout { get; } = timeout;

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(Timeout);

        try
        {
            return await baseStream.ReadAsync(buffer, cts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DownloadStalledException(Timeout);
        }
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override int Read(byte[] buffer, int offset, int count)
        => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

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
