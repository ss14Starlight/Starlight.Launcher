using System.Runtime.InteropServices;

namespace Starlight.Launcher.Helpers;

public static partial class ConsoleHelper
{
    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AllocConsole();

    public static void CreateConsole() => AllocConsole();
}
