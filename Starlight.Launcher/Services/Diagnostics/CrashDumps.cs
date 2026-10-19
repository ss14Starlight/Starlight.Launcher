using Serilog;
using Starlight.Launcher.WebUI.Models.Settings;

namespace Starlight.Launcher.Services.Diagnostics;

/// <summary>
/// Crash dumps written by the .NET runtime itself (createdump ships with the self-contained loader).
/// </summary>
public static class CrashDumps
{
    public const string Extension = ".dmp";

    /// <summary>
    /// Appended to the dump name by the runtime when <c>DOTNET_EnableCrashReport</c> is set.
    /// </summary>
    public const string CrashReportSuffix = ".crashreport.json";

    /// <summary>
    /// Prefix of dumps taken on request from a live client, so they aren't mistaken for crashes.
    /// </summary>
    public const string SnapshotPrefix = "snapshot-";

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

        // Stacks of every thread as JSON next to the dump, readable without a debugger. Not implemented on Windows.
        if (!OperatingSystem.IsWindows())
            yield return ("DOTNET_EnableCrashReport", "1");
    }

    /// <summary>
    /// Dumps and crash reports written by the client with the given pid when it crashed, if any.
    /// </summary>
    public static IEnumerable<string> FindForProcess(AppSettings settings, int pid)
    {
        var dir = settings.DirCrashDumps;
        return !Directory.Exists(dir)
            ? []
            : Directory.EnumerateFiles(dir, $"*-{pid}-*")
                .Where(f => !Path.GetFileName(f).StartsWith(SnapshotPrefix, StringComparison.Ordinal)
                    && (f.EndsWith(Extension, StringComparison.Ordinal) || f.EndsWith(CrashReportSuffix, StringComparison.Ordinal)));
    }

    /// <summary>
    /// Deletes all but the newest <paramref name="keep"/> dumps, together with their crash reports.
    /// </summary>
    public static void Prune(string dir, int keep)
    {
        var old = new DirectoryInfo(dir).EnumerateFiles($"*{Extension}")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Skip(keep);

        foreach (var file in old)
        {
            try
            {
                file.Delete();
                File.Delete(file.FullName + CrashReportSuffix);
                Log.Debug("Deleted old crash dump {File}", file.FullName);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Warning(e, "Failed to delete old crash dump {File}", file.FullName);
            }
        }
    }
}
