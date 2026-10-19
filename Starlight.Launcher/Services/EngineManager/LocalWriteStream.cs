namespace Starlight.Launcher.Services.EngineManager;

internal sealed class LocalWriteException(Exception inner) : Exception(inner.Message, inner);

internal sealed class LocalWriteStream(FileStream file) : Stream
{
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public void Reset() => Guard(() =>
    {
        file.SetLength(0);
        _ = file.Seek(0, SeekOrigin.Begin);
    });

    public override void Write(byte[] buffer, int offset, int count) => Guard(() => file.Write(buffer, offset, count));

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        try
        {
            await file.WriteAsync(buffer, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new LocalWriteException(e);
        }
    }

    public override void Flush() => Guard(file.Flush);

    public override async Task FlushAsync(CancellationToken cancellationToken)
    {
        try
        {
            await file.FlushAsync(cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new LocalWriteException(e);
        }
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    private static void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new LocalWriteException(e);
        }
    }
}
