using System.Buffers;
using System.Runtime.InteropServices;
using SharpZstd.Interop;

namespace Starlight.Launcher.Utility;

/// <summary>
/// Provides helper methods for Zstandard compression.
/// </summary>
public static class ZStd
{
    /// <summary>
    /// Returns the maximum compressed size for the specified input length.
    /// </summary>
    public static int CompressBound(int length) => (int)Zstd.ZSTD_compressBound((nuint)length);
}

/// <summary>
/// Represents a reusable Zstandard compression context.
/// </summary>
public sealed unsafe partial class ZStdCCtx : IDisposable
{
    /// <summary>
    /// Current compression context for this class.
    /// </summary>
    public ZSTD_CCtx* Context { get; private set; }

    private bool _disposed => Context == null;

    /// <summary>
    /// Initializes a new compression context.
    /// </summary>
    public ZStdCCtx() => Context = Zstd.ZSTD_createCCtx();

    /// <summary>
    /// Sets a compression parameter for this context.
    /// </summary>
    public void SetParameter(ZSTD_cParameter parameter, int value)
    {
        CheckDisposed();

        _ = Zstd.ZSTD_CCtx_setParameter(Context, parameter, value);
    }

    /// <summary>
    /// Compresses the source data into the destination buffer.
    /// </summary>
    public int Compress(Span<byte> destination, Span<byte> source, int compressionLevel = 3)
    {
        CheckDisposed();

        fixed (byte* dst = destination)
        fixed (byte* src = source)
        {
            var ret = Zstd.ZSTD_compressCCtx(
                Context,
                dst, (nuint)destination.Length,
                src, (nuint)source.Length,
                compressionLevel);

            ZStdException.ThrowIfError(ret);
            return (int)ret;
        }
    }

    /// <summary>
    /// Releases the unmanaged compression context.
    /// </summary>
    ~ZStdCCtx()
    {
        Dispose();
    }

    /// <summary>
    /// Releases the unmanaged compression context.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _ = Zstd.ZSTD_freeCCtx(Context);
        Context = null;
        GC.SuppressFinalize(this);
    }

    private void CheckDisposed() => ObjectDisposedException.ThrowIf(_disposed, nameof(ZStdCCtx));
}

/// <summary>
/// Represents a reusable Zstandard decompression context.
/// </summary>
public sealed unsafe partial class ZStdDCtx : IDisposable
{
    /// <summary>
    /// Current decompression context for this class
    /// </summary>
    public ZSTD_DCtx* Context { get; private set; }

    private bool _disposed => Context == null;

    /// <summary>
    /// Initializes a new decompression context.
    /// </summary>
    public ZStdDCtx() => Context = Zstd.ZSTD_createDCtx();

    /// <summary>
    /// Sets a decompression parameter for this context.
    /// </summary>
    public void SetParameter(ZSTD_dParameter parameter, int value)
    {
        CheckDisposed();

        _ = Zstd.ZSTD_DCtx_setParameter(Context, parameter, value);
    }

    /// <summary>
    /// Decompresses the source data into the destination buffer.
    /// </summary>
    public int Decompress(Span<byte> destination, Span<byte> source)
    {
        CheckDisposed();

        fixed (byte* dst = destination)
        fixed (byte* src = source)
        {
            var ret = Zstd.ZSTD_decompressDCtx(Context, dst, (nuint)destination.Length, src, (nuint)source.Length);

            ZStdException.ThrowIfError(ret);
            return (int)ret;
        }
    }

    /// <summary>
    /// Releases the unmanaged decompression context.
    /// </summary>
    ~ZStdDCtx()
    {
        Dispose();
    }

    /// <summary>
    /// Releases the unmanaged decompression context.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _ = Zstd.ZSTD_freeDCtx(Context);
        Context = null;
        GC.SuppressFinalize(this);
    }

    private void CheckDisposed() => ObjectDisposedException.ThrowIf(_disposed, nameof(ZStdDCtx));
}

/// <summary>
/// Represents an exception thrown by the Zstandard library.
/// </summary>
[Serializable]
public class ZStdException : Exception
{
    /// <summary>
    /// Initializes a new ZStd exception.
    /// </summary>
    public ZStdException()
    {
    }

    /// <summary>
    /// Initializes a new ZStd exception.
    /// </summary>
    public ZStdException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new ZStd exception.
    /// </summary>
    public ZStdException(string message, Exception inner) : base(message, inner)
    {
    }

    /// <summary>
    /// Creates an exception from a Zstandard error code.
    /// </summary>
    public static unsafe ZStdException FromCode(nuint code) => new(Marshal.PtrToStringUTF8((IntPtr)Zstd.ZSTD_getErrorName(code))!);

    /// <summary>
    /// Throws a <see cref="ZStdException"/> if the specified result represents an error.
    /// </summary>
    public static void ThrowIfError(nuint code)
    {
        if (Zstd.ZSTD_isError(code) != 0)
            throw FromCode(code);
    }
}

/// <summary>
/// Provides a stream that decompresses Zstandard-compressed data while reading.
/// </summary>
public sealed class ZStdDecompressStream : Stream
{
    private readonly Stream _baseStream;
    private readonly bool _ownStream;
    private readonly unsafe ZSTD_DCtx* _ctx;
    private readonly byte[] _buffer;
    private int _bufferPos;
    private int _bufferSize;
    private bool _disposed;

    /// <summary>
    /// Initializes a new decompression stream.
    /// </summary>
    public unsafe ZStdDecompressStream(Stream baseStream, bool ownStream = true)
    {
        _baseStream = baseStream;
        _ownStream = ownStream;
        _ctx = Zstd.ZSTD_createDCtx();
        _buffer = ArrayPool<byte>.Shared.Rent((int)Zstd.ZSTD_DStreamInSize());
    }

    /// <summary>
    /// Releases the resources used by the stream.
    /// </summary>
    protected override unsafe void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        _disposed = true;
        _ = Zstd.ZSTD_freeDCtx(_ctx);

        if (disposing)
        {
            if (_ownStream)
                _baseStream.Dispose();

            ArrayPool<byte>.Shared.Return(_buffer);
        }
    }

    /// <summary>
    /// Flushes the underlying stream.
    /// </summary>
    public override void Flush()
    {
        ThrowIfDisposed();
        _baseStream.Flush();
    }

    /// <summary>
    /// Reads and decompresses data into the specified buffer.
    /// </summary>
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    /// <summary>
    /// Reads and decompresses a single byte.
    /// </summary>
    public override int ReadByte()
    {
        Span<byte> buf = stackalloc byte[1];
        return Read(buf) == 0 ? -1 : buf[0];
    }

    /// <summary>
    /// Reads and decompresses data into the specified buffer.
    /// </summary>
    public override unsafe int Read(Span<byte> buffer)
    {
        ThrowIfDisposed();
        do
        {
            if (_bufferSize == 0 || _bufferPos == _bufferSize)
            {
                _bufferPos = 0;
                _bufferSize = _baseStream.Read(_buffer);

                if (_bufferSize == 0)
                    return 0;
            }

            fixed (byte* inputPtr = _buffer)
            fixed (byte* outputPtr = buffer)
            {
                var outputBuf = new ZSTD_outBuffer { dst = outputPtr, pos = 0, size = (nuint)buffer.Length };
                var inputBuf = new ZSTD_inBuffer { src = inputPtr, pos = (nuint)_bufferPos, size = (nuint)_bufferSize };
                var ret = Zstd.ZSTD_decompressStream(_ctx, &outputBuf, &inputBuf);

                _bufferPos = (int)inputBuf.pos;
                ZStdException.ThrowIfError(ret);

                if (outputBuf.pos > 0)
                    return (int)outputBuf.pos;
            }
        } while (true);
    }

    /// <summary>
    /// Asynchronously reads and decompresses data into the specified buffer.
    /// </summary>
    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        do
        {
            if (_bufferSize == 0 || _bufferPos == _bufferSize)
            {
                _bufferPos = 0;
                _bufferSize = await _baseStream.ReadAsync(_buffer, cancellationToken);

                if (_bufferSize == 0)
                    return 0;
            }

            var ret = DecompressChunk(this, buffer.Span);
            if (ret > 0)
                return (int)ret;

        } while (true);

        static unsafe nuint DecompressChunk(ZStdDecompressStream stream, Span<byte> buffer)
        {
            fixed (byte* inputPtr = stream._buffer)
            fixed (byte* outputPtr = buffer)
            {
                ZSTD_outBuffer outputBuf = default;
                outputBuf.dst = outputPtr;
                outputBuf.pos = 0;
                outputBuf.size = (nuint)buffer.Length;
                ZSTD_inBuffer inputBuf = default;
                inputBuf.src = inputPtr;
                inputBuf.pos = (nuint)stream._bufferPos;
                inputBuf.size = (nuint)stream._bufferSize;

                var ret = Zstd.ZSTD_decompressStream(stream._ctx, &outputBuf, &inputBuf);

                stream._bufferPos = (int)inputBuf.pos;
                ZStdException.ThrowIfError(ret);

                return outputBuf.pos;
            }
        }
    }

    /// <summary>
    /// Currently not supported.
    /// </summary>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <summary>
    /// Currently not supported.
    /// </summary>
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <summary>
    /// Currently not supported.
    /// </summary>
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <summary>
    /// Stream access parameter which determines read access.
    /// </summary>
    public override bool CanRead => true;

    /// <summary>
    /// Stream access parameter which determines seek access.
    /// </summary>
    public override bool CanSeek => false;

    /// <summary>
    /// Stream access parameter which determines write access.
    /// </summary>
    public override bool CanWrite => false;

    /// <summary>
    /// Currently not supported.
    /// </summary>
    public override long Length => throw new NotSupportedException();

    /// <summary>
    /// Currently not supported.
    /// </summary>
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ZStdDecompressStream));
    }
}

/// <summary>
/// Provides a stream that compresses data using Zstandard while writing.
/// </summary>
public sealed class ZStdCompressStream : Stream
{
    private readonly Stream _baseStream;
    private readonly bool _ownStream;
    private readonly unsafe ZSTD_CCtx* _ctx;
    private readonly byte[] _buffer;
    private int _bufferPos;
    private bool _disposed;

    /// <summary>
    /// Initializes a new compression stream.
    /// </summary>
    public unsafe ZStdCompressStream(Stream baseStream, bool ownStream = true)
    {
        _ctx = Zstd.ZSTD_createCCtx();
        _baseStream = baseStream;
        _ownStream = ownStream;
        _buffer = ArrayPool<byte>.Shared.Rent((int)Zstd.ZSTD_CStreamOutSize());
    }

    /// <summary>
    /// Flushes all pending compressed data to the underlying stream.
    /// </summary>
    public override void Flush() => FlushInternal(ZSTD_EndDirective.ZSTD_e_flush);

    /// <summary>
    /// Finalizes the compression stream and writes all remaining compressed data.
    /// </summary>
    public void FlushEnd() => FlushInternal(ZSTD_EndDirective.ZSTD_e_end);

    /// <summary>
    /// Flushes pending compressed data according to the specified Zstandard directive.
    /// </summary>
    private unsafe void FlushInternal(ZSTD_EndDirective directive)
    {
        fixed (byte* outPtr = _buffer)
        {
            ZSTD_outBuffer outBuf = default;
            outBuf.size = (nuint)_buffer.Length;
            outBuf.pos = (nuint)_bufferPos;
            outBuf.dst = outPtr;

            ZSTD_inBuffer inBuf = default;

            while (true)
            {
                var err = Zstd.ZSTD_compressStream2(_ctx, &outBuf, &inBuf, directive);
                ZStdException.ThrowIfError(err);
                _bufferPos = (int)outBuf.pos;

                _baseStream.Write(_buffer.AsSpan(0, (int)outBuf.pos));
                _bufferPos = 0;
                outBuf.pos = 0;

                if (err == 0)
                    break;
            }
        }

        _baseStream.Flush();
    }

    /// <summary>
    /// Currently not supported
    /// </summary>
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <summary>
    /// Currently not supported
    /// </summary>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <summary>
    /// Currently not supported
    /// </summary>
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <summary>
    /// Compresses the specified data and appends it to the compression stream.
    /// </summary>
    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    /// <summary>
    /// Compresses the specified data and appends it to the compression stream.
    /// </summary>
    public override unsafe void Write(ReadOnlySpan<byte> buffer)
    {
        ThrowIfDisposed();

        fixed (byte* outPtr = _buffer)
        fixed (byte* inPtr = buffer)
        {
            ZSTD_outBuffer outBuf = default;
            outBuf.size = (nuint)_buffer.Length;
            outBuf.pos = (nuint)_bufferPos;
            outBuf.dst = outPtr;

            ZSTD_inBuffer inBuf = default;
            inBuf.pos = 0;
            inBuf.size = (nuint)buffer.Length;
            inBuf.src = inPtr;

            while (true)
            {
                var err = Zstd.ZSTD_compressStream2(_ctx, &outBuf, &inBuf, ZSTD_EndDirective.ZSTD_e_continue);
                ZStdException.ThrowIfError(err);
                _bufferPos = (int)outBuf.pos;

                if (inBuf.pos >= inBuf.size)
                    break;

                // Not all input data consumed. Flush output buffer and continue.
                _baseStream.Write(_buffer.AsSpan(0, (int)outBuf.pos));
                _bufferPos = 0;
                outBuf.pos = 0;
            }
        }
    }

    /// <summary>
    /// Stream access parameter which determines read access.
    /// </summary>
    public override bool CanRead => false;

    /// <summary>
    /// Stream access parameter which determines seek access.
    /// </summary>
    public override bool CanSeek => false;

    /// <summary>
    /// Stream access parameter which determines write access.
    /// </summary>
    public override bool CanWrite => true;

    /// <summary>
    /// Currently not supported
    /// </summary>
    public override long Length => throw new NotSupportedException();

    /// <summary>
    /// Currently not supported
    /// </summary>
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <summary>
    /// Releases the resources used by the stream.
    /// </summary>
    protected override unsafe void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (_disposed)
            return;

        _disposed = true;
        _ = Zstd.ZSTD_freeCCtx(_ctx);

        if (disposing)
        {
            if (_ownStream)
                _baseStream.Dispose();

            ArrayPool<byte>.Shared.Return(_buffer);
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, nameof(ZStdCompressStream));
}
