using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Serilog;
using Starlight.Launcher.Services.Settings;

namespace Starlight.Launcher.Services.Diagnostics;

public sealed class TracyProfiler(HttpClient http, SettingsService settings)
{
    public const string Version = "0.13.1";

    private const string WindowsZipUrl = $"https://github.com/wolfpld/tracy/releases/download/v{Version}/windows-{Version}.zip";
    private const string WindowsZipSha256 = "EE6DB1A7E71A12DEB5973A8DBFDF9F36D3635BEC0E0B31B1CC74F28DE7DAC4C9";
    private const string ProfilerExe = "tracy-profiler.exe";

    private const int ClientPort = 8086;

    private readonly SemaphoreSlim _installLock = new(1, 1);

    public static bool ClientSupported =>
        RuntimeInformation.ProcessArchitecture == Architecture.X64 && (OperatingSystem.IsWindows() || OperatingSystem.IsLinux());

    public static bool ProfilerDownloadSupported => OperatingSystem.IsWindows() && RuntimeInformation.OSArchitecture == Architecture.X64;

    private string InstallDir => Path.Combine(settings.GetSettings().DirTools, $"tracy-{Version}");

    public async Task LaunchForClientAsync(CancellationToken cancel)
    {
        if (!ProfilerDownloadSupported)
        {
            Log.Information("No prebuilt Tracy profiler for this platform, connect a Tracy {Version} profiler manually", Version);
            return;
        }

        try
        {
            if (Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ProfilerExe)).Length > 0)
            {
                Log.Debug("Tracy profiler already running, not starting another one");
                return;
            }

            var exe = await EnsureInstalledAsync(cancel);

            var startInfo = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(exe)!,
                ArgumentList = { "-a", "127.0.0.1", "-p", ClientPort.ToString() },
            };

            _ = Process.Start(startInfo);
            Log.Information("Started Tracy profiler {Version}", Version);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            Log.Error(e, "Failed to start the Tracy profiler");
        }
    }

    private async Task<string> EnsureInstalledAsync(CancellationToken cancel)
    {
        await _installLock.WaitAsync(cancel);
        try
        {
            var dir = InstallDir;
            var exe = Path.Combine(dir, ProfilerExe);
            if (File.Exists(exe))
                return exe;

            Log.Information("Downloading Tracy profiler {Version} from {Url}", Version, WindowsZipUrl);

            var tools = Path.GetDirectoryName(dir)!;
            _ = Directory.CreateDirectory(tools);

            var tmpZip = Path.Combine(tools, $".tracy-{Version}-{Guid.NewGuid():N}.zip");
            var tmpDir = Path.Combine(tools, $".tracy-{Version}-{Guid.NewGuid():N}");
            try
            {
                using (var response = await http.GetAsync(WindowsZipUrl, HttpCompletionOption.ResponseHeadersRead, cancel))
                {
                    _ = response.EnsureSuccessStatusCode();
                    await using var file = File.Create(tmpZip);
                    await response.Content.CopyToAsync(file, cancel);
                }

                string hash;
                await using (var file = File.OpenRead(tmpZip))
                    hash = Convert.ToHexString(await SHA256.HashDataAsync(file, cancel));

                if (!hash.Equals(WindowsZipSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Tracy download hash mismatch: got {hash}");

                ZipFile.ExtractToDirectory(tmpZip, tmpDir);

                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);

                Directory.Move(tmpDir, dir);
            }
            finally
            {
                TryDelete(() => File.Delete(tmpZip));
                if (Directory.Exists(tmpDir))
                    TryDelete(() => Directory.Delete(tmpDir, recursive: true));
            }

            Log.Information("Installed Tracy profiler to {Dir}", dir);
            return exe;
        }
        finally
        {
            _ = _installLock.Release();
        }
    }

    private static void TryDelete(Action delete)
    {
        try
        {
            delete();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Debug(e, "Failed to clean up Tracy download leftovers");
        }
    }
}
