using System.Runtime.InteropServices;
using Starlight.Launcher.Services.LocalServer.Dotnet;

namespace Starlight.Launcher.Tests;

public sealed class ExecutableArchitectureTests
{
    [Fact]
    public void CurrentHostMatchesProcessArchitecture()
    {
        var host = Environment.ProcessPath;
        Assert.NotNull(host);
        Assert.True(ExecutableArchitecture.Matches(host, RuntimeInformation.ProcessArchitecture));
    }

    [Theory]
    // Thin Mach-O 64: magic CFFAEDFE, cputype little endian.
    [InlineData(new byte[] { 0xCF, 0xFA, 0xED, 0xFE, 0x07, 0x00, 0x00, 0x01 }, new[] { Architecture.X64 })]
    [InlineData(new byte[] { 0xCF, 0xFA, 0xED, 0xFE, 0x0C, 0x00, 0x00, 0x01 }, new[] { Architecture.Arm64 })]
    // Universal binary with x86_64 + arm64 slices.
    [InlineData(new byte[]
    {
        0xCA, 0xFE, 0xBA, 0xBE, 0x00, 0x00, 0x00, 0x02,
        0x01, 0x00, 0x00, 0x07, 0, 0, 0, 3, 0, 0, 0x10, 0, 0, 0, 0x10, 0, 0, 0, 0, 12,
        0x01, 0x00, 0x00, 0x0C, 0, 0, 0, 0, 0, 0, 0x20, 0, 0, 0, 0x10, 0, 0, 0, 0, 14
    }, new[] { Architecture.X64, Architecture.Arm64 })]
    // ELF x86-64 and aarch64.
    [InlineData(new byte[] { 0x7F, 0x45, 0x4C, 0x46, 2, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 0x3E, 0x00 }, new[] { Architecture.X64 })]
    [InlineData(new byte[] { 0x7F, 0x45, 0x4C, 0x46, 2, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 0xB7, 0x00 }, new[] { Architecture.Arm64 })]
    // Shell script / garbage.
    [InlineData(new byte[] { 0x23, 0x21, 0x2F, 0x62, 0x69, 0x6E, 0x2F, 0x73, 0x68, 0x0A }, new Architecture[0])]
    public void ReadsHeaders(byte[] header, Architecture[] expected)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, header);
            Assert.Equal(expected, ExecutableArchitecture.Read(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MissingFileDoesNotMatch()
        => Assert.False(ExecutableArchitecture.Matches(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), Architecture.X64));
}
