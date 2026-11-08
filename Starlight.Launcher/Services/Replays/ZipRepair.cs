using System.Buffers.Binary;
using System.Text;

namespace Starlight.Launcher.Services.Replays;

/// <summary>
///     Rebuilds the central directory of a zip whose tail is missing, e.g. a replay whose recording was cut off.
///     Entries are recovered from their local headers; the first incomplete one and everything after it is dropped.
/// </summary>
public static class ZipRepair
{
    private const uint LocalHeaderSig = 0x04034b50;
    private const uint DescriptorSig = 0x08074b50;
    private const uint CentralHeaderSig = 0x02014b50;
    private const uint EndOfCentralDirSig = 0x06054b50;

    private const int LocalHeaderSize = 30;
    private const int DescriptorSize = 16;

    private sealed record Entry(
        uint Offset,
        ushort Version,
        ushort Flags,
        ushort Method,
        ushort Time,
        ushort Date,
        uint Crc,
        uint CompressedSize,
        uint Size,
        byte[] Name);

    /// <summary>
    ///     Writes a repaired copy of <paramref name="source"/> to <paramref name="destination"/>.
    /// </summary>
    /// <returns>The number of recovered entries, 0 if nothing could be recovered (no file is written then).</returns>
    public static int TryRepair(string source, string destination)
    {
        if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Can't repair a zip in place", nameof(destination));

        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);

        var entries = ScanEntries(input, out var dataEnd);
        if (entries.Count == 0)
            return 0;

        var directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(directory))
            _ = Directory.CreateDirectory(directory);

        var temp = destination + ".tmp";
        try
        {
            using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
            {
                input.Position = 0;
                CopyBytes(input, output, dataEnd);
                WriteCentralDirectory(output, entries, (uint)dataEnd);
            }

            File.Move(temp, destination, true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }

        return entries.Count;
    }

    private static List<Entry> ScanEntries(Stream input, out long dataEnd)
    {
        var entries = new List<Entry>();
        var length = input.Length;
        var header = new byte[LocalHeaderSize];
        dataEnd = 0;

        while (entries.Count < ushort.MaxValue)
        {
            var offset = input.Position;
            if (offset + LocalHeaderSize > length || offset > uint.MaxValue)
                break;

            input.ReadExactly(header);
            var span = header.AsSpan();
            if (BinaryPrimitives.ReadUInt32LittleEndian(span) != LocalHeaderSig)
                break;

            var version = BinaryPrimitives.ReadUInt16LittleEndian(span[4..]);
            var flags = BinaryPrimitives.ReadUInt16LittleEndian(span[6..]);
            var method = BinaryPrimitives.ReadUInt16LittleEndian(span[8..]);
            var time = BinaryPrimitives.ReadUInt16LittleEndian(span[10..]);
            var date = BinaryPrimitives.ReadUInt16LittleEndian(span[12..]);
            var crc = BinaryPrimitives.ReadUInt32LittleEndian(span[14..]);
            var compressedSize = BinaryPrimitives.ReadUInt32LittleEndian(span[18..]);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(span[22..]);
            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(span[26..]);
            var extraLength = BinaryPrimitives.ReadUInt16LittleEndian(span[28..]);

            var dataStart = offset + LocalHeaderSize + nameLength + extraLength;
            if (dataStart > length)
                break;

            var name = new byte[nameLength];
            input.ReadExactly(name);
            input.Position = dataStart;

            long entryEnd;
            if ((flags & 0x08) != 0)
            {
                // Sizes follow the data in a descriptor.
                if (FindDescriptor(input, dataStart) is not { } descriptor)
                    break;

                (crc, compressedSize, size, entryEnd) = descriptor;
            }
            else
            {
                // Zip64 sizes would need a zip64 central directory too, recordings never get that big.
                if (compressedSize == uint.MaxValue || size == uint.MaxValue)
                    break;

                entryEnd = dataStart + compressedSize;
            }

            if (entryEnd > length || entryEnd > uint.MaxValue)
                break;

            entries.Add(new Entry((uint)offset, version, flags, method, time, date, crc, compressedSize, size, name));
            dataEnd = entryEnd;
            input.Position = entryEnd;
        }

        return entries;
    }

    private static (uint Crc, uint CompressedSize, uint Size, long End)? FindDescriptor(Stream input, long dataStart)
    {
        var length = input.Length;
        var buffer = new byte[1 << 16];
        var descriptor = new byte[DescriptorSize];
        var position = dataStart;

        while (position + DescriptorSize <= length)
        {
            input.Position = position;
            var read = input.ReadAtLeast(buffer, Math.Min(buffer.Length, (int)Math.Min(int.MaxValue, length - position)), throwOnEndOfStream: false);
            if (read < 4)
                return null;

            for (var i = 0; i + 4 <= read; i++)
            {
                if (BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(i)) != DescriptorSig)
                    continue;

                var candidate = position + i;
                if (candidate + DescriptorSize > length)
                    return null;

                input.Position = candidate;
                input.ReadExactly(descriptor);
                var compressedSize = BinaryPrimitives.ReadUInt32LittleEndian(descriptor.AsSpan(8));

                // The signature bytes can occur inside compressed data, the size has to point back at the data start.
                if (compressedSize == candidate - dataStart)
                {
                    return (BinaryPrimitives.ReadUInt32LittleEndian(descriptor.AsSpan(4)),
                        compressedSize,
                        BinaryPrimitives.ReadUInt32LittleEndian(descriptor.AsSpan(12)),
                        candidate + DescriptorSize);
                }
            }

            // Keep 3 bytes of overlap so a signature split between reads is still found.
            position += read - 3;
        }

        return null;
    }

    private static void CopyBytes(Stream input, Stream output, long count)
    {
        var buffer = new byte[1 << 16];
        while (count > 0)
        {
            var read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, count));
            if (read == 0)
                throw new EndOfStreamException();

            output.Write(buffer, 0, read);
            count -= read;
        }
    }

    private static void WriteCentralDirectory(Stream output, List<Entry> entries, uint directoryOffset)
    {
        using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
        var directoryStart = output.Position;

        foreach (var entry in entries)
        {
            writer.Write(CentralHeaderSig);
            writer.Write(entry.Version); // Version made by.
            writer.Write(entry.Version); // Version needed to extract.
            writer.Write(entry.Flags);
            writer.Write(entry.Method);
            writer.Write(entry.Time);
            writer.Write(entry.Date);
            writer.Write(entry.Crc);
            writer.Write(entry.CompressedSize);
            writer.Write(entry.Size);
            writer.Write((ushort)entry.Name.Length);
            writer.Write((ushort)0); // Extra field length.
            writer.Write((ushort)0); // Comment length.
            writer.Write((ushort)0); // Disk number.
            writer.Write((ushort)0); // Internal attributes.
            writer.Write(0u); // External attributes.
            writer.Write(entry.Offset);
            writer.Write(entry.Name);
        }

        var directorySize = (uint)(output.Position - directoryStart);

        writer.Write(EndOfCentralDirSig);
        writer.Write((ushort)0); // This disk.
        writer.Write((ushort)0); // Disk with the central directory.
        writer.Write((ushort)entries.Count);
        writer.Write((ushort)entries.Count);
        writer.Write(directorySize);
        writer.Write(directoryOffset);
        writer.Write((ushort)0); // Comment length.
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
