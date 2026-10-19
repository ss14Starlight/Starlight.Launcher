using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32;
using Serilog;
using Starlight.Launcher.Services.Logging;
using Starlight.Launcher.Services.Settings;
using Starlight.Launcher.WebUI.Models.Diagnostics;
using Starlight.Launcher.WebUI.Models.Settings;

namespace Starlight.Launcher.Services.Diagnostics;

/// <summary>
/// Packs everything needed to look into a client problem into one zip a player can send.
/// </summary>
/// <remarks>
/// Dumps are only listed, not packed: they run into gigabytes. Logs carry the username and server addresses,
/// which the settings page says before the player shares the file.
/// </remarks>
public sealed class DiagnosticsBundle(SettingsService settings, ClientLogManager clientLogs, ClientDiagnostics clientDiagnostics)
{
    private const int ClientLogFiles = 4;
    private const int LauncherLogFiles = 2;
    private const int CrashReports = 3;
    private const int BundlesToKeep = 5;

    private static string s_launcherLogsDir => Path.Combine(AppPaths.AppDataDirectory, "launcher-logs");

    public async Task<DiagnosticsBundleResult> CreateAsync(CancellationToken cancel = default)
    {
        var s = settings.GetSettings();
        var dir = Path.Combine(s.DirLauncherData, "diagnostics");
        var path = Path.Combine(dir, $"starlight-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip");

        try
        {
            await Task.Run(() =>
            {
                _ = Directory.CreateDirectory(dir);
                Prune(dir);

                using var zip = ZipFile.Open(path, ZipArchiveMode.Create);

                AddText(zip, "system.txt", DescribeSystem(s));

                if (clientDiagnostics.LastSnapshot is { } snapshot)
                    AddText(zip, "counters.txt", DescribeCounters(snapshot));

                AddText(zip, "crash-dumps.txt", DescribeDumps(s.DirCrashDumps));

                foreach (var file in Newest(clientLogs.ResolveDirectory(), "*.log", ClientLogFiles))
                    AddFile(zip, file, "client-logs");

                foreach (var file in Newest(s_launcherLogsDir, "*.log", LauncherLogFiles))
                    AddFile(zip, file, "launcher-logs");

                foreach (var file in Newest(s.DirCrashDumps, $"*{CrashDumps.CrashReportSuffix}", CrashReports))
                    AddFile(zip, file, "crash-reports");

                var startupCrash = Path.Combine(AppPaths.AppDataDirectory, "startup-crash.txt");
                if (File.Exists(startupCrash))
                    AddFile(zip, startupCrash, "");
            }, cancel);

            Log.Information("Wrote diagnostics bundle {Path}", path);
            return new DiagnosticsBundleResult(true, path);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            Log.Error(e, "Failed to write diagnostics bundle");
            try
            {
                File.Delete(path);
            }
            catch (Exception)
            {
                // Best effort, a broken zip is only clutter.
                Log.Warning("Failed to delete broken zip!");
            }

            return new DiagnosticsBundleResult(false);
        }
    }

    private static string DescribeSystem(AppSettings s)
    {
        var sb = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;

        _ = sb.AppendLine(inv, $"Created: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        _ = sb.AppendLine(inv, $"Launcher: {LauncherUpdater.GetVersion()} ({InstallKindDetector.Current})");
        _ = sb.AppendLine(inv, $"OS: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        _ = sb.AppendLine(inv, $"Runtime: {RuntimeInformation.FrameworkDescription} ({RuntimeInformation.RuntimeIdentifier})");
        _ = sb.AppendLine(inv, $"CPU: {CpuName() ?? "unknown"}, {Environment.ProcessorCount} logical cores");
        _ = sb.AppendLine(inv, $"RAM: {GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024)} MiB");

        foreach (var gpu in Gpus())
            _ = sb.AppendLine(inv, $"GPU: {gpu}");

        _ = sb.AppendLine(inv, $"Language: {s.SelectedLanguage ?? CultureInfo.CurrentUICulture.Name}");
        _ = sb.AppendLine();
        _ = sb.AppendLine("Launch settings:");
        _ = sb.AppendLine(inv, $"  CompatMode: {s.CompatMode}");
        _ = sb.AppendLine(inv, $"  DisableSigning: {s.DisableSigning}");
        _ = sb.AppendLine(inv, $"  EngineOverride: {s.EngineOverrideEnabled} {s.EngineOverridePath}");
        _ = sb.AppendLine(inv, $"  Tracy: {s.TracyEnabled} (localhost only: {s.TracyOnlyLocalhost})");
        _ = sb.AppendLine(inv, $"  CrashDumps: {s.CrashDumpsEnabled}");
        _ = sb.AppendLine(inv, $"  AudioOutputMode: {s.AudioOutputMode ?? "auto"}, FollowDefaultDevice: {s.AudioFollowDefaultDevice}");
        _ = sb.AppendLine(inv, $"  CVar overrides: {s.ClientCVarOverrides.ReplaceLineEndings("; ")}");

        return sb.ToString();
    }

    private static string DescribeCounters(ClientDiagnosticsSnapshot snapshot)
    {
        var sb = new StringBuilder();
        _ = sb.AppendLine(CultureInfo.InvariantCulture, $"Client {snapshot.Pid}, last update {snapshot.UpdatedAt:yyyy-MM-dd HH:mm:ss zzz}");
        foreach (var c in snapshot.Counters.OrderBy(c => c.Name, StringComparer.Ordinal))
            _ = sb.AppendLine(CultureInfo.InvariantCulture, $"{c.DisplayName}: {c.Value:0.###} {c.Units}");

        return sb.ToString();
    }

    private static string DescribeDumps(string dir)
    {
        if (!Directory.Exists(dir))
            return "No crash dumps folder.\n";

        var sb = new StringBuilder();
        var dumps = new DirectoryInfo(dir).EnumerateFiles($"*{CrashDumps.Extension}").OrderByDescending(f => f.LastWriteTime).ToList();
        if (dumps.Count == 0)
            return "No dumps.\n";

        foreach (var f in dumps)
            _ = sb.AppendLine(CultureInfo.InvariantCulture, $"{f.LastWriteTime:yyyy-MM-dd HH:mm:ss}  {f.Length / (1024 * 1024),6} MiB  {f.FullName}");

        return sb.ToString();
    }

    private static string? CpuName()
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", null) as string;

            if (OperatingSystem.IsLinux())
                return File.ReadLines("/proc/cpuinfo").FirstOrDefault(l => l.StartsWith("model name", StringComparison.Ordinal))?.Split(':', 2)[1].Trim();
        }
        catch (Exception e)
        {
            Log.Debug(e, "Can't read the CPU name");
        }

        return null;
    }

    private static List<string> Gpus()
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return WindowsGpus();

            if (OperatingSystem.IsLinux())
                return LinuxGpus();
        }
        catch (Exception e)
        {
            Log.Debug(e, "Can't list GPUs");
        }

        return [];
    }

    [SupportedOSPlatform("windows")]
    private static List<string> WindowsGpus()
    {
        // Display adapter device class.
        const string ClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

        var result = new List<string>();
        using var cls = Registry.LocalMachine.OpenSubKey(ClassKey);
        if (cls == null)
            return result;

        foreach (var name in cls.GetSubKeyNames())
        {
            try
            {
                using var key = cls.OpenSubKey(name);
                if (key?.GetValue("DriverDesc") is string desc)
                    result.Add($"{desc}, driver {key.GetValue("DriverVersion")} ({key.GetValue("DriverDate")})");
            }
            catch (System.Security.SecurityException)
            {
                // "Properties" and friends are locked down, skip them.
            }
        }

        return result;
    }

    private static List<string> LinuxGpus()
    {
        var result = new List<string>();
        foreach (var card in Directory.EnumerateDirectories("/sys/class/drm", "card*"))
        {
            var device = Path.Combine(card, "device");
            var vendorFile = Path.Combine(device, "vendor");
            if (Path.GetFileName(card).Contains('-') || !File.Exists(vendorFile))
                continue;

            var vendor = File.ReadAllText(vendorFile).Trim();
            var id = File.ReadAllText(Path.Combine(device, "device")).Trim();
            var driver = new DirectoryInfo(Path.Combine(device, "driver")).ResolveLinkTarget(true)?.Name ?? "unknown";
            var vendorName = vendor switch
            {
                "0x10de" => "NVIDIA",
                "0x1002" => "AMD",
                "0x8086" => "Intel",
                _ => vendor,
            };

            result.Add($"{vendorName} {id}, driver {driver}");
        }

        return result;
    }

    private static IEnumerable<string> Newest(string dir, string pattern, int count)
        => Directory.Exists(dir)
            ? new DirectoryInfo(dir).EnumerateFiles(pattern).OrderByDescending(f => f.LastWriteTimeUtc).Take(count).Select(f => f.FullName)
            : [];

    private static void AddText(ZipArchive zip, string name, string text)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(text);
    }

    private static void AddFile(ZipArchive zip, string path, string folder)
    {
        try
        {
            // Logs of a running client or of the launcher itself are still open for writing.
            using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var entry = zip.CreateEntry(folder.Length == 0 ? Path.GetFileName(path) : $"{folder}/{Path.GetFileName(path)}");
            entry.LastWriteTime = File.GetLastWriteTime(path);
            using var target = entry.Open();
            source.CopyTo(target);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warning(e, "Can't add {Path} to the diagnostics bundle", path);
        }
    }

    private static void Prune(string dir)
    {
        foreach (var old in new DirectoryInfo(dir).EnumerateFiles("starlight-diagnostics-*.zip")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Skip(BundlesToKeep - 1))
        {
            try
            {
                old.Delete();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Debug(e, "Can't delete old diagnostics bundle {Path}", old.FullName);
            }
        }
    }
}
