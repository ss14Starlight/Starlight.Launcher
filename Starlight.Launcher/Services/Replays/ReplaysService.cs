using System.IO.Compression;
using Serilog;
using Starlight.Launcher.Models.Helpers;
using Starlight.Launcher.Services.Auth;
using Starlight.Launcher.Services.Settings;
using Starlight.Launcher.WebUI.Models.Helpers;
using Starlight.Launcher.WebUI.Models.Replays;

namespace Starlight.Launcher.Services.Replays;

public sealed class ReplaysService(HttpClient http, StarlightAuthApi starlight, SettingsService settings)
{
    private const int KeepDownloaded = 10;

    private static readonly TimeSpan _listTimeout = TimeSpan.FromSeconds(15);

    private string _cacheDir => Path.Combine(settings.GetSettings().DirLauncherData, "replays");

    public async Task<ReplaysResult> GetAsync(CancellationToken cancel = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(_listTimeout);

            var list = await http.GetFromJsonAsync<List<ReplayEntry>>(new Uri(starlight.ApiUrl, "api/replays"), timeout.Token);
            return new ReplaysResult(true, list ?? []);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            Log.Warning(e, "Can't load the replay list");
            return new ReplaysResult(false, []);
        }
    }

    public bool IsDownloaded(ReplayEntry replay) => File.Exists(LocalPath(replay));

    public async Task<IFileResult> DownloadAsync(ReplayEntry replay, IProgress<double>? progress, CancellationToken cancel = default)
    {
        var path = LocalPath(replay);
        if (File.Exists(path))
        {
            if (IsValidZip(path))
            {
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
                return new FileResult(path);
            }

            Log.Warning("Cached replay {Path} is not a valid zip, downloading it again", path);
            File.Delete(path);
        }

        _ = Directory.CreateDirectory(_cacheDir);
        var temp = path + ".part";

        using (var response = await http.GetAsync(new Uri(starlight.ApiUrl, replay.DownloadPath.TrimStart('/')), HttpCompletionOption.ResponseHeadersRead, cancel))
        {
            _ = response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? replay.Size;

            await using var source = await response.Content.ReadAsStreamAsync(cancel);
            long read = 0;
            await using (var target = File.Create(temp))
            {
                var buffer = new byte[81920];
                int n;
                while ((n = await source.ReadAsync(buffer, cancel)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, n), cancel);
                    read += n;
                    if (total is > 0)
                        progress?.Report(Math.Min(1, (double)read / total.Value));
                }
            }

            if (response.Content.Headers.ContentLength is { } expected && read != expected)
            {
                File.Delete(temp);
                throw new IOException($"Replay download was cut short: got {read} of {expected} bytes");
            }
        }

        if (!IsValidZip(temp))
        {
            File.Delete(temp);
            throw new InvalidDataException("Downloaded replay is not a valid zip archive");
        }

        File.Move(temp, path, true);
        Prune();
        return new FileResult(path);
    }

    private static bool IsValidZip(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private string LocalPath(ReplayEntry replay)
    {
        var name = Path.GetFileName(replay.FileName);
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            name = $"replay-{replay.Round}.zip";

        return Path.Combine(_cacheDir, name);
    }

    private void Prune()
    {
        try
        {
            var stale = new DirectoryInfo(_cacheDir)
                .EnumerateFiles("*.zip")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Skip(KeepDownloaded);

            foreach (var file in stale)
                file.Delete();

            foreach (var part in Directory.EnumerateFiles(_cacheDir, "*.part"))
            {
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(part) > TimeSpan.FromDays(1))
                    File.Delete(part);
            }
        }
        catch (Exception e)
        {
            Log.Debug(e, "Can't prune the replay cache");
        }
    }
}
