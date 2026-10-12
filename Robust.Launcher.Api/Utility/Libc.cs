using System;
using System.Runtime.InteropServices;

namespace Robust.Launcher.Api.Utility;

internal static partial class Libc
{
    public const int RTLD_LAZY = 0x00001;
    public const int RTLD_NOW = 0x00002;
    public const int RTLD_BINDING_MASK = 0x3;
    public const int RTLD_NOLOAD = 0x00004;
    public const int RTLD_DEEPBIND = 0x00008;
    public const int RTLD_GLOBAL = 0x00100;
    public const int RTLD_LOCAL = 0;
    public const int RTLD_NODELETE = 0x01000;

    [LibraryImport("libdl.so.2")]
    public static partial IntPtr dlopen([MarshalAs(UnmanagedType.LPUTF8Str)] string name, int flags);

    public const int O_RDONLY = 0;
    public const int O_CLOEXEC = 0x80000;

    // _IOR('f', 1, long) / _IOW('f', 2, long); same values on x86_64 and arm64.
    public const nuint FS_IOC_GETFLAGS = 0x80086601;
    public const nuint FS_IOC_SETFLAGS = 0x40086602;
    public const int FS_COMPR_FL = 0x00000004;
    public const int FS_NOCOMP_FL = 0x00000400;

    [LibraryImport("libc", SetLastError = true)]
    public static partial int open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    [LibraryImport("libc", SetLastError = true)]
    public static unsafe partial int ioctl(int fd, nuint request, void* arg);

    [LibraryImport("libc", SetLastError = true)]
    public static partial int close(int fd);
}
