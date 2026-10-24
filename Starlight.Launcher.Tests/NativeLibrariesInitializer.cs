using System.Runtime.CompilerServices;

namespace Starlight.Launcher.Tests;

internal static class NativeLibrariesInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
        => Program.PinBundledLibsodium();
}
