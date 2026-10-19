using System;
using System.Buffers;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Mono.Unix;
using TerraFX.Interop.Windows;
using Win = TerraFX.Interop.Windows.Windows;

namespace Robust.Launcher.Api.Utility;

public static class Helpers
{
    public static readonly JsonSerializerOptions JsonWebOptions = new(JsonSerializerDefaults.Web);

    public delegate void DownloadProgressCallback(long downloaded, long total);

    public static void ExtractZipToDirectory(string directory, Stream zipStream)
    {
        using var zipArchive = new ZipArchive(zipStream);
        zipArchive.ExtractToDirectory(directory);
    }

    public static void ClearDirectory(string directory)
    {
        var dirInfo = new DirectoryInfo(directory);
        foreach (var fileInfo in dirInfo.EnumerateFiles())
        {
            fileInfo.Delete();
        }

        foreach (var childDirInfo in dirInfo.EnumerateDirectories())
        {
            childDirInfo.Delete(true);
        }
    }

    public static async Task<T> AsJson<T>(this HttpContent content) where T : notnull
    {
        var str = await content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(str, JsonWebOptions)
            ?? throw new JsonException("AsJson: did not expect null response");
    }

    public static unsafe void MarkDirectoryCompress(string path)
    {
        if (OperatingSystem.IsLinux())
        {
            MarkDirectoryCompressLinux(path);
            return;
        }

        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1))
            return;

        fixed (char* pPath = path)
        {
            var handle = Win.CreateFileW(
                pPath,
                Win.GENERIC_ALL,
                FILE.FILE_SHARE_READ,
                null,
                OPEN.OPEN_EXISTING,
                FILE.FILE_FLAG_BACKUP_SEMANTICS,
                HANDLE.NULL);

            var lpBytesReturned = 0u;
            var lpInBuffer = (short)Win.COMPRESSION_FORMAT_DEFAULT;

            _ = Win.DeviceIoControl(
                handle,
                FSCTL.FSCTL_SET_COMPRESSION,
                &lpInBuffer,
                sizeof(short),
                null,
                0,
                &lpBytesReturned,
                null);

            _ = Win.CloseHandle(handle);
        }
    }

    // Equivalent of `chattr +c`: new files created in the directory inherit the flag.
    // Only btrfs (and f2fs with compression enabled) honor it; elsewhere the ioctl fails and we silently ignore it.
    private static unsafe void MarkDirectoryCompressLinux(string path)
    {
        var fd = Libc.open(path, Libc.O_RDONLY | Libc.O_CLOEXEC);
        if (fd < 0)
            return;

        try
        {
            var flags = 0;
            if (Libc.ioctl(fd, Libc.FS_IOC_GETFLAGS, &flags) < 0)
                return;

            flags = (flags | Libc.FS_COMPR_FL) & ~Libc.FS_NOCOMP_FL;
            _ = Libc.ioctl(fd, Libc.FS_IOC_SETFLAGS, &flags);
        }
        finally
        {
            _ = Libc.close(fd);
        }
    }

    public static void EnsureDirectoryExists(string dir)
    {
        if (!Directory.Exists(dir))
            _ = Directory.CreateDirectory(dir);
    }

    public static async Task DownloadToStream(this HttpClient client, string uri, Stream stream,
        DownloadProgressCallback? progress = null, CancellationToken cancel = default)
        => await Task.Run(async () =>
            {
                using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancel);
                _ = response.EnsureSuccessStatusCode();

                await using var contentStream = new StallTimeoutStream(await response.Content.ReadAsStreamAsync(cancel));
                // await using var fileStream = new FileStream(filePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite, 4096, useAsync: true);
                var totalLength = response.Content.Headers.ContentLength;
                if (totalLength.HasValue)
                {
                    progress?.Invoke(0, totalLength.Value);
                }

                var totalRead = 0L;
                var reads = 0L;
                const int BufferLength = 4096;
                var buffer = ArrayPool<byte>.Shared.Rent(BufferLength);
                var isMoreToRead = true;

                do
                {
                    var read = await contentStream.ReadAsync(buffer.AsMemory(0, BufferLength), cancel);
                    if (read == 0)
                    {
                        isMoreToRead = false;
                    }
                    else
                    {
                        await stream.WriteAsync(buffer.AsMemory(0, read), cancel);

                        reads++;
                        totalRead += read;
                        if (totalLength.HasValue && reads % 20 == 0)
                        {
                            progress?.Invoke(totalRead, totalLength.Value);
                        }
                    }
                } while (isMoreToRead);
            }, cancel);

    public static void ChmodPlusX(string path)
    {
        var f = new UnixFileInfo(path);
        f.FileAccessPermissions |=
            FileAccessPermissions.UserExecute
                | FileAccessPermissions.GroupExecute
                | FileAccessPermissions.OtherExecute;
    }
}
