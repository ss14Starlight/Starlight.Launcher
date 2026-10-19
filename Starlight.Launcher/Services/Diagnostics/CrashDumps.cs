using Serilog;
using Starlight.Launcher.WebUI.Models.Settings;

namespace Starlight.Launcher.Services.Diagnostics;

/// <summary>
/// Crash dumps written by the .NET runtime itself (createdump ships with the self-contained loader).
/// </summary>
public static class CrashDumps
{
    private const string Extension = ".dmp";

    private const string DumpType = "2";

    public static IEnumerable<(string Name, string Value)> GetEnvironment(AppSettings settings)
    {
        var dir = settings.DirCrashDumps;
        _ = Directory.CreateDirectory(dir);
        Prune(dir, Math.Max(settings.CrashDumpsRetain, 1) - 1);

        yield return ("DOTNET_DbgEnableMiniDump", "1");
        yield return ("DOTNET_DbgMiniDumpType", DumpType);
        yield return ("DOTNET_DbgMiniDumpName", Path.Combine(dir, $"%e-%p-%t{Extension}"));
        yield return ("DOTNET_CreateDumpDiagnostics", "1");
    }

    /// <summary>
    /// Dumps written by the client with the given pid, if any.
    /// </summary>
    public static IEnumerable<string> FindForProcess(AppSettings settings, int pid)
    {
        var dir = settings.DirCrashDumps;
        if (!Directory.Exists(dir))
            return [];

        return Directory.EnumerateFiles(dir, $"*-{pid}-*{Extension}");
    }

    private static void Prune(string dir, int keep)
    {
        var old = new DirectoryInfo(dir).EnumerateFiles($"*{Extension}")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Skip(keep);

        foreach (var file in old)
        {
            try
            {
                file.Delete();
                Log.Debug("Deleted old crash dump {File}", file.FullName);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Warning(e, "Failed to delete old crash dump {File}", file.FullName);
            }
        }
    }
}
