using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using NSec.Cryptography;
using Robust.LoaderApi;

namespace Robust.Loader;

internal sealed class Program
{
    private readonly string[] _engineArgs;

    private const string RobustAssemblyName = "Robust.Client";

    private readonly ZipFileApi _fileApi;

    private Program(byte[] robustBytes, string[] engineArgs)
    {
        _engineArgs = engineArgs;
        var zipArchive = new ZipArchive(new MemoryStream(robustBytes, writable: false), ZipArchiveMode.Read);

        AssemblyLoadContext.Default.Resolving += LoadContextOnResolving;
        AssemblyLoadContext.Default.ResolvingUnmanagedDll += LoadContextOnResolvingUnmanaged;

        var prefix = "";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            prefix = "Space Station 14.app/Contents/Resources/";
        }

        _fileApi = new ZipFileApi(zipArchive, prefix);
    }

    private IntPtr LoadContextOnResolvingUnmanaged(Assembly assembly, string unmanaged)
    {
        var ourDir = Path.GetDirectoryName(typeof(Program).Assembly.Location)!;
        var os = GetOsName();

        var arch = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        string[] dirs =
        [
            ourDir,
            Path.Combine(ourDir, "runtimes", RuntimeInformation.RuntimeIdentifier, "native"),
            Path.Combine(ourDir, "runtimes", $"{os}-{arch}", "native"),
        ];

        var ext = GetNativeLibraryExtension();

        string[] names = Path.HasExtension(unmanaged) ? [unmanaged] : [unmanaged, unmanaged + ext, "lib" + unmanaged + ext];

        foreach (var dir in dirs)
        {
            foreach (var name in names)
            {
                var path = Path.Combine(dir, name);
                if (File.Exists(path) && NativeLibrary.TryLoad(path, out var handle))
                    return handle;
            }
        }

        return IntPtr.Zero;
    }

    private bool Run()
    {
        if (!TryOpenAssembly(RobustAssemblyName, out var clientAssembly))
        {
            Console.WriteLine("Unable to locate Robust.Client.dll in engine build!");
            return false;
        }

        if (!TryGetLoader(clientAssembly, out var loader))
            return false;

#if USE_SYSTEM_SQLITE
        SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_sqlite3());
#else
        SQLitePCL.Batteries_V2.Init();
#endif

        var launcher = Environment.GetEnvironmentVariable("SS14_LAUNCHER_PATH");
        var redialApi = launcher != null ? new RedialApi(launcher) : null;
        var contentDb = Environment.GetEnvironmentVariable("SS14_LOADER_CONTENT_DB");
        var contentVersion = Environment.GetEnvironmentVariable("SS14_LOADER_CONTENT_VERSION");
        var overlayZip = Environment.GetEnvironmentVariable("SS14_LOADER_OVERLAY_ZIP");
        ContentDbFileApi? contentApi = null;
        ZipFileApi? overlayApi = null;
        IEnumerable<ApiMount>? extraMounts = null;
        if (!string.IsNullOrEmpty(contentDb) && !string.IsNullOrEmpty(contentVersion))
        {
            contentApi = new ContentDbFileApi(contentDb, long.Parse(contentVersion));
            extraMounts = [new ApiMount(contentApi, "/")];
        }

        if (!string.IsNullOrEmpty(overlayZip))
        {
            var overlayArchive = new ZipArchive(
                File.OpenRead(overlayZip),
                ZipArchiveMode.Read);

            overlayApi = new ZipFileApi(overlayArchive, "");
            // Put this *before* the game's regular installation so it masks files.
            extraMounts = [new ApiMount(overlayApi, "/"), .. extraMounts ?? []];
        }

        var args = new MainArgs(_engineArgs, _fileApi, redialApi, extraMounts);

        try
        {
            loader.Main(args);
        }
        finally
        {
            contentApi?.Dispose();
            overlayApi?.Dispose();
        }

        return true;
    }

    private static bool TryGetLoader(Assembly clientAssembly, [NotNullWhen(true)] out ILoaderEntryPoint? loader)
    {
        loader = null;
        // Find ILoaderEntryPoint with the LoaderEntryPointAttribute
        var attrib = clientAssembly.GetCustomAttribute<LoaderEntryPointAttribute>();
        if (attrib == null)
        {
            Console.WriteLine("No LoaderEntryPointAttribute found on Robust.Client assembly!");
            return false;
        }

        var type = attrib.LoaderEntryPointType;
        if (!type.IsAssignableTo(typeof(ILoaderEntryPoint)))
        {
            Console.WriteLine("Loader type '{0}' does not implement ILoaderEntryPoint!", type);
            return false;
        }

        loader = (ILoaderEntryPoint)Activator.CreateInstance(type)!;
        return true;
    }

    private Assembly? LoadContextOnResolving(AssemblyLoadContext arg1, AssemblyName arg2) => TryOpenAssembly(arg2.Name!, out var assembly) ? assembly : null;

    private bool TryOpenAssembly(string name, [NotNullWhen(true)] out Assembly? assembly)
    {
        if (!TryOpenAssemblyStream(name, out var asm, out var pdb))
        {
            assembly = null;
            return false;
        }

        assembly = AssemblyLoadContext.Default.LoadFromStream(asm, pdb);
        return true;
    }

    private bool TryOpenAssemblyStream(string name, [NotNullWhen(true)] out Stream? asm, out Stream? pdb)
    {
        asm = null;
        pdb = null;

        if (!_fileApi.TryOpen($"{name}.dll", out asm))
            return false;

        _ = _fileApi.TryOpen($"{name}.pdb", out pdb);
        return true;
    }

    [STAThread]
    internal static int Main(string[] args)
    {
        if (args.Length < 3)
        {
            Console.WriteLine("Usage: SS14.Loader <robustPath> <signature> <public key> [engineArg [engineArg...]]");
            return 1;
        }

        PinBundledLibsodium();

        var robustPath = args[0];
        var sig = Convert.FromHexString(args[1]);
        var keyPath = args[2];

        var pubKey = PublicKey.Import(
            SignatureAlgorithm.Ed25519,
            File.ReadAllBytes(keyPath),
            KeyBlobFormat.PkixPublicKeyText);

        var robustBytes = File.ReadAllBytes(robustPath);

        if (!SignatureAlgorithm.Ed25519.Verify(pubKey, robustBytes, sig))
        {
            // ONLY allow disabling signing on debug mode.
#if !RELEASE
            var disableVar = Environment.GetEnvironmentVariable("SS14_DISABLE_SIGNING");
            if (!string.IsNullOrEmpty(disableVar) && bool.Parse(disableVar))
            {
                Console.WriteLine("Failed to verify engine signature, ignoring because signing is disabled.");
            }
            else
#endif
            {
                Console.WriteLine("Failed to verify engine signature!");
                return 2;
            }
        }

        var program = new Program(robustBytes, args[3..]);
        return program.Run() ? 0 : 3;
    }

    private static string GetOsName()
    {
        if (OperatingSystem.IsWindows())
            return "win";

        if (OperatingSystem.IsMacOS())
            return "osx";

        return "linux";
    }

    private static string GetNativeLibraryExtension()
    {
        if (OperatingSystem.IsWindows())
            return ".dll";

        if (OperatingSystem.IsMacOS())
            return ".dylib";

        return ".so";
    }

    private static void PinBundledLibsodium()
    {
        var fileName = "libsodium" + GetNativeLibraryExtension();

        var os = GetOsName();

        var arch = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();

        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, fileName),
            Path.Combine(baseDir, "runtimes", RuntimeInformation.RuntimeIdentifier, "native", fileName),
            Path.Combine(baseDir, "runtimes", $"{os}-{arch}", "native", fileName),
        };

        string? path = null;
        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                path = candidate;
                break;
            }
        }

        if (path == null || !NativeLibrary.TryLoad(path, out var handle))
            return;

        try
        {
            NativeLibrary.SetDllImportResolver(typeof(SignatureAlgorithm).Assembly, (name, _, _) =>
                name is "libsodium" or "libsodium.dll" or "libsodium.so" or "libsodium.dylib" ? handle : IntPtr.Zero);
        }
        catch (InvalidOperationException)
        {
            // Resolver already set.
        }
    }
}
