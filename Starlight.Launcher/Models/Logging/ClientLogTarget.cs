using System.Text;

namespace Starlight.Launcher.Models.Logging;

internal sealed class ClientLogTarget : IAsyncDisposable
{
    private readonly FileStream _file;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public string Path { get; }

    private ClientLogTarget(string path, FileStream file)
    {
        Path = path;
        _file = file;
    }

    public static ClientLogTarget OpenAppend(string path)
    {
        var fs = new FileStream(
            path,
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete,
            4096,
            FileOptions.Asynchronous);

        return new ClientLogTarget(path, fs);
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancel = default)
    {
        await _lock.WaitAsync(cancel);
        try
        {
            await _file.WriteAsync(data, cancel);
            await _file.FlushAsync(cancel);
        }
        finally
        {
            _ = _lock.Release();
        }
    }

    public ValueTask WriteTextAsync(string text, CancellationToken cancel = default)
        => WriteAsync(Encoding.UTF8.GetBytes(text), cancel);

    public async ValueTask DisposeAsync()
    {
        await _file.FlushAsync();
        await _file.DisposeAsync();
        _lock.Dispose();
    }
}

internal sealed class ClientLogStream(ClientLogTarget target) : Stream
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

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        => target.WriteAsync(buffer, cancellationToken);

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => target.WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Write(byte[] buffer, int offset, int count)
        => target.WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}

public sealed class ClientLogSession : IAsyncDisposable
{
    private readonly ClientLogTarget _stdout;
    private readonly ClientLogTarget? _stderr;

    public Stream Stdout { get; }
    public Stream Stderr { get; }
    public string StdoutPath => _stdout.Path;
    public string StderrPath => (_stderr ?? _stdout).Path;

    internal ClientLogSession(ClientLogTarget stdout, ClientLogTarget? stderr)
    {
        _stdout = stdout;
        _stderr = stderr;

        Stdout = new ClientLogStream(stdout);
        Stderr = new ClientLogStream(stderr ?? stdout);
    }

    public async ValueTask WriteProcessStartedAsync(int pid)
    {
        var text = $"=== pid: {pid}\n";
        await _stdout.WriteTextAsync(text);
        if (_stderr != null)
            await _stderr.WriteTextAsync(text);
    }

    public async ValueTask WriteExitAsync(int? exitCode)
    {
        var text = $"\n--- CLIENT EXITED (code: {exitCode?.ToString() ?? "unknown"}) " +
                   $"at {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} ---\n";

        await _stdout.WriteTextAsync(text);
        if (_stderr != null)
            await _stderr.WriteTextAsync(text);
    }

    public async ValueTask DisposeAsync()
    {
        await _stdout.DisposeAsync();
        if (_stderr != null)
            await _stderr.DisposeAsync();
    }
}
