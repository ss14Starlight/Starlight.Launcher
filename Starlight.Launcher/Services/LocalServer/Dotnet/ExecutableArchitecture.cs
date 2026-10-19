using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Starlight.Launcher.Services.LocalServer.Dotnet;

internal static class ExecutableArchitecture
{
    public static bool Matches(string path, Architecture architecture)
    {
        try
        {
            return Read(path).Contains(architecture);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    internal static IReadOnlyList<Architecture> Read(string path)
    {
        using var file = File.OpenRead(path);
        Span<byte> header = stackalloc byte[64];
        var read = file.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
        header = header[..read];

        if (read >= 4 && header[0] == 0x7F && header[1] == 'E' && header[2] == 'L' && header[3] == 'F')
            return ReadElf(header);

        if (read >= 2 && header[0] == 'M' && header[1] == 'Z')
            return ReadPe(file, header);

        if (read >= 8)
            return ReadMachO(file, header);

        return [];
    }

    private static IReadOnlyList<Architecture> ReadElf(ReadOnlySpan<byte> header)
    {
        if (header.Length < 20)
            return [];

        // EI_DATA: 1 = little endian, 2 = big endian.
        var machine = header[5] == 2
            ? BinaryPrimitives.ReadUInt16BigEndian(header[18..])
            : BinaryPrimitives.ReadUInt16LittleEndian(header[18..]);

        return machine switch
        {
            0x03 => [Architecture.X86],
            0x28 => [Architecture.Arm],
            0x3E => [Architecture.X64],
            0xB7 => [Architecture.Arm64],
            _ => []
        };
    }

    private static IReadOnlyList<Architecture> ReadPe(FileStream file, ReadOnlySpan<byte> header)
    {
        if (header.Length < 0x40)
            return [];

        var peOffset = BinaryPrimitives.ReadInt32LittleEndian(header[0x3C..]);
        if (peOffset <= 0 || peOffset > file.Length - 6)
            return [];

        Span<byte> pe = stackalloc byte[6];
        file.Position = peOffset;
        return file.ReadAtLeast(pe, pe.Length, throwOnEndOfStream: false) < pe.Length
            || pe[0] != 'P'
            || pe[1] != 'E'
            || pe[2] != 0
            || pe[3] != 0
            ? []
            : BinaryPrimitives.ReadUInt16LittleEndian(pe[4..]) switch
            {
                0x014C => [Architecture.X86],
                0x8664 => [Architecture.X64],
                0xAA64 => [Architecture.Arm64],
                _ => []
            };
    }

    private static List<Architecture> ReadMachO(FileStream file, ReadOnlySpan<byte> header)
    {
        var magic = BinaryPrimitives.ReadUInt32BigEndian(header);
        switch (magic)
        {
            // MH_MAGIC_64 / MH_MAGIC stored little endian.
            case 0xCFFAEDFE or 0xCEFAEDFE:
                return MachOCpu(BinaryPrimitives.ReadUInt32LittleEndian(header[4..])) is { } single ? [single] : [];

            // FAT_MAGIC / FAT_MAGIC_64 (universal binary), always big endian.
            case 0xCAFEBABE or 0xCAFEBABF:
                var count = BinaryPrimitives.ReadUInt32BigEndian(header[4..]);
                var entrySize = magic == 0xCAFEBABF ? 32 : 20;
                if (count is 0 or > 16)
                    return [];

                var entries = new byte[count * entrySize];
                file.Position = 8;
                if (file.ReadAtLeast(entries, entries.Length, throwOnEndOfStream: false) < entries.Length)
                    return [];

                var result = new List<Architecture>();
                for (var i = 0; i < count; i++)
                {
                    if (MachOCpu(BinaryPrimitives.ReadUInt32BigEndian(entries.AsSpan(i * entrySize))) is { } arch)
                        result.Add(arch);
                }

                return result;
            default:
                return [];
        }
    }

    private static Architecture? MachOCpu(uint cpuType) => cpuType switch
    {
        0x00000007 => Architecture.X86,
        0x01000007 => Architecture.X64,
        0x0100000C => Architecture.Arm64,
        _ => null
    };
}
