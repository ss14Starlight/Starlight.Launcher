using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Serilog;
using Starlight.Launcher.Services.Settings;
using Starlight.Launcher.WebUI.Models.Diagnostics;

namespace Starlight.Launcher.Services.Diagnostics;

public sealed class RenderDocInstaller(HttpClient http, SettingsService settings)
{
    public const string Version = "1.46";

    private const string WindowsUrl = $"https://renderdoc.org/stable/{Version}/RenderDoc_{Version}_64.zip";
    private const string WindowsSha256 = "9CA4D09ECABA2CC791168660D6FC2A7E3D70FE67146E87EC05C5F8DEA70772F7";
    private const string LinuxUrl = $"https://renderdoc.org/stable/{Version}/renderdoc_{Version}.tar.gz";
    private const string LinuxSha256 = "6FEDF15EAB1288D9A81889BA8A4452DCA53F5C10DE8162BE32A0F8972F62DC09";

    private readonly SemaphoreSlim _installLock = new(1, 1);

    public static bool Supported
        => RuntimeInformation.OSArchitecture == Architecture.X64 && (OperatingSystem.IsWindows() || OperatingSystem.IsLinux());

    private string _installDir
        => Path.Combine(settings.GetSettings().DirTools, $"renderdoc-{Version}");

    public string LibraryPath
        => OperatingSystem.IsWindows()
        ? Path.Combine(_installDir, "renderdoc.dll")
        : Path.Combine(_installDir, "lib", "librenderdoc.so");

    private string _uiPath
        => OperatingSystem.IsWindows()
        ? Path.Combine(_installDir, "qrenderdoc.exe")
        : Path.Combine(_installDir, "bin", "qrenderdoc");

    public string CapturesDir => Path.Combine(settings.GetSettings().DirLauncherData, "diagnostics", "renderdoc");

    public RenderDocInfo GetInfo() => new(Supported, File.Exists(LibraryPath), Version);

    public async Task EnsureInstalledAsync(CancellationToken cancel)
    {
        if (!Supported)
            throw new PlatformNotSupportedException("RenderDoc has no build for this platform.");

        await _installLock.WaitAsync(cancel);
        try
        {
            if (File.Exists(LibraryPath))
                return;

            var (url, sha) = OperatingSystem.IsWindows() ? (WindowsUrl, WindowsSha256) : (LinuxUrl, LinuxSha256);
            Log.Information("Downloading RenderDoc {Version} from {Url}", Version, url);

            var dir = _installDir;
            var tools = Path.GetDirectoryName(dir)!;
            _ = Directory.CreateDirectory(tools);

            var tmpFile = Path.Combine(tools, $".renderdoc-{Guid.NewGuid():N}");
            var tmpDir = tmpFile + ".d";
            try
            {
                using (var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel))
                {
                    _ = response.EnsureSuccessStatusCode();
                    await using var file = File.Create(tmpFile);
                    await response.Content.CopyToAsync(file, cancel);
                }

                string hash;
                await using (var file = File.OpenRead(tmpFile))
                    hash = Convert.ToHexString(await SHA256.HashDataAsync(file, cancel));

                if (!hash.Equals(sha, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"RenderDoc download hash mismatch: got {hash}");

                _ = Directory.CreateDirectory(tmpDir);
                if (OperatingSystem.IsWindows())
                {
                    ZipFile.ExtractToDirectory(tmpFile, tmpDir);
                }
                else
                {
                    await using var file = File.OpenRead(tmpFile);
                    await using var gz = new GZipStream(file, CompressionMode.Decompress);
                    await TarFile.ExtractToDirectoryAsync(gz, tmpDir, overwriteFiles: true, cancel);
                }

                var root = Directory.GetDirectories(tmpDir) is [var only] ? only : tmpDir;

                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);

                Directory.Move(root, dir);
            }
            finally
            {
                TryDelete(() => File.Delete(tmpFile));
                if (Directory.Exists(tmpDir))
                    TryDelete(() => Directory.Delete(tmpDir, recursive: true));
            }

            Log.Information("Installed RenderDoc to {Dir}", dir);
        }
        finally
        {
            _ = _installLock.Release();
        }
    }

    public IEnumerable<(string Name, string Value)> GetClientEnvironment()
    {
        _ = Directory.CreateDirectory(CapturesDir);
        yield return ("ROBUST_RENDERDOC_PATH", LibraryPath);
        yield return ("ROBUST_RENDERDOC_CAPTURES", Path.Combine(CapturesDir, "ss14"));
    }

    public async Task OpenUiAsync(CancellationToken cancel)
    {
        await EnsureInstalledAsync(cancel);
        _ = Process.Start(new ProcessStartInfo(_uiPath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(_uiPath),
        });
    }

    private static void TryDelete(Action delete)
    {
        try
        {
            delete();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Debug(e, "Failed to clean up RenderDoc download leftovers");
        }
    }
}
