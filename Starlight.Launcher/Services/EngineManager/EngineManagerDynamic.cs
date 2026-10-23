using System.IO.MemoryMappedFiles;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.Sqlite;
using NSec.Cryptography;
using Robust.Launcher.Api.Models;
using Robust.Launcher.Api.Models.Data;
using Robust.Launcher.Api.Utility;
using Serilog;
using Starlight.Launcher.Models.EngineManager;
using Starlight.Launcher.Services.Settings;
using Starlight.Launcher.WebUI.Models.Data;
using static Robust.Launcher.Api.Utility.Helpers;

namespace Starlight.Launcher.Services.EngineManager;

/// <summary>
///     Downloads engine versions from the website.
/// </summary>
public sealed partial class EngineManagerDynamic : IEngineManager
{
    public const string OverrideVersionName = "_OVERRIDE_";

    private readonly SettingsService _settings;
    private readonly HttpClient _http;
    private readonly ICdnRegistry _cdns;
    private readonly EngineSourceGuard _sourceGuard;
    private volatile bool _manifestCachesDirty;

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, VerifiedEngineKey> _verifiedEngineKeys = new();

    private sealed record VerifiedEngineKey(string Signature, long Length, DateTime LastWriteUtc, string PublicKey);

    public EngineManagerDynamic(HttpClient http, SettingsService settings, ICdnRegistry cdns, EngineSourceGuard sourceGuard)
    {
        _settings = settings;
        _http = http;
        _cdns = cdns;
        _sourceGuard = sourceGuard;
        _cdns.Changed += () => _manifestCachesDirty = true;
    }

    public string GetEnginePath(string engineVersion)
    {
        var settings = _settings.GetSettings();
#if DEVELOPMENT
        if (settings.EngineOverrideEnabled)
        {
            return FindOverrideZip("Robust.Client", settings.EngineOverridePath);
        }
#endif

        return !_settings.GetEngines().TryGetValue(engineVersion, out _)
            ? throw new ArgumentException("We do not have that engine version!")
            : Path.Combine(settings.DirEngineInstallations, $"{engineVersion}.zip");
    }

    public string GetEngineModule(string moduleName, string moduleVersion)
    {
        var settings = _settings.GetSettings();
#if DEVELOPMENT
        if (settings.EngineOverrideEnabled)
            moduleVersion = OverrideVersionName;
#endif

        return Path.Combine(settings.DirModuleInstallations, moduleName, moduleVersion);
    }

    public string GetEngineSignature(string engineVersion)
    {
#if DEVELOPMENT
        var settings = _settings.GetSettings();
        if (settings.EngineOverrideEnabled)
            return "DEADBEEF";
#endif

        return !_settings.GetEngines().TryGetValue(engineVersion, out var installedEngine)
            ? throw new ArgumentException("Invalid engine version in GET!")
            : installedEngine.Signature;
    }

    private async Task ConfirmEngineSourceAsync(FoundVersionInfo found, CancellationToken cancel)
    {
        if (found.Cdn.Important)
            return;

        // Checked against the enabled CDNs only: a player who switched the Starlight CDN off chose this.
        if (_cdns.Cdns.FirstOrDefault(c => c.Important) is not { } preferred)
            return;

        var cdnName = string.IsNullOrWhiteSpace(found.Cdn.Name) ? found.Cdn.BaseUrl.Urls[0] : found.Cdn.Name;
        var preferredName = string.IsNullOrWhiteSpace(preferred.Name) ? preferred.BaseUrl.Urls[0] : preferred.Name;

        Log.Warning(
            "Engine {Version} is not on the {Preferred} CDN, it will come from {Cdn} and may lack Starlight features",
            found.Version, preferredName, cdnName);

        if (!await _sourceGuard.ConfirmAsync(new ForeignEngineWarning(found.Version, cdnName, preferredName), cancel))
            throw new OperationCanceledException($"Player declined engine {found.Version} from {cdnName}");
    }

    public async Task<EngineInstallationResult> DownloadEngineIfNecessary(
        string engineVersion,
        DownloadProgressCallback? progress = null,
        CancellationToken cancel = default)
    {
        var settings = _settings.GetSettings();
#if DEVELOPMENT
        if (settings.EngineOverrideEnabled)
        {
            // Engine override means we don't need to download anything, we have it locally!
            // At least, if we don't, we'll just blame the developer that enabled it.
            return new EngineInstallationResult(engineVersion, false);
        }
#endif

        var foundVersion = await GetVersionInfo(engineVersion, cancel: cancel) ?? throw new UpdateException("Unable to find engine version in manifest!");
        if (foundVersion.Info.Insecure)
            throw new UpdateException("Specified engine version is insecure!");

        await ConfirmEngineSourceAsync(foundVersion, cancel);

        Log.Debug(
            "Requested engine version was {RequestedEngien}, redirected to {FoundVersion}",
            engineVersion,
            foundVersion.Version);

        var bestRid = RidUtility.FindBestRid(foundVersion.Info.Platforms.Keys) ?? throw new NoEngineForPlatformException("No engine version available for our platform!");
        Log.Debug("Selecting RID {rid}", bestRid);

        var buildInfo = foundVersion.Info.Platforms[bestRid];

        if (_settings.GetEngines().TryGetValue(foundVersion.Version, out var installed))
        {
            if (!string.IsNullOrEmpty(installed.Sha256)
                && string.Equals(installed.Sha256, buildInfo.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return new EngineInstallationResult(foundVersion.Version, false);
            }

            Log.Information(
                "Engine {Version} is installed but its hash ({Have}) does not match the requested build ({Want}); re-downloading.",
                foundVersion.Version,
                installed.Sha256 ?? "<none>",
                buildInfo.Sha256);

            _settings.RemoveInstalledEngine(foundVersion.Version);

            var stalePath = Path.Combine(settings.DirEngineInstallations, $"{foundVersion.Version}.zip");
            if (File.Exists(stalePath))
                File.Delete(stalePath);
        }

        Log.Information("Installing engine version {version}...", foundVersion.Version);

        EnsureDirectoryExists(settings.DirEngineInstallations);

        var downloadTarget = Path.Combine(settings.DirEngineInstallations, $"{foundVersion.Version}.zip");
        var file = File.Create(downloadTarget, 4096, FileOptions.Asynchronous);

        try
        {
            var urls = ExpandMirrors(buildInfo.Url, [foundVersion.Cdn]);

            await DownloadFromMirrors(urls, file, VerifySha, progress, cancel, async () =>
            {
                var more = await GetSameEngineBuildOnOtherCdns(foundVersion, bestRid, buildInfo.Sha256, cancel);
                return [.. more.SelectMany(x => ExpandMirrors(x.Url, [x.Cdn])).Except(urls, StringComparer.OrdinalIgnoreCase)];
            });
        }
        catch
        {
            // Don't leave behind garbage.
            await file.DisposeAsync();
            File.Delete(downloadTarget);

            throw;
        }

        await file.DisposeAsync();

        _settings.AddInstalledEngine(new InstalledEngineVersion(foundVersion.Version, buildInfo.Signature, buildInfo.Sha256));
        return new EngineInstallationResult(foundVersion.Version, true);

        async Task VerifySha(FileStream stream)
        {
            stream.Seek(0, SeekOrigin.Begin);
            var actualSha = await ComputeSha256Hex(stream, cancel);

            if (!string.Equals(actualSha, buildInfo.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new UpdateException(
                    $"Downloaded engine {foundVersion.Version} hash mismatch: expected {buildInfo.Sha256}, got {actualSha}.");
            }
        }
    }

    private async Task DownloadFromMirrors(
        IReadOnlyList<string> urls,
        FileStream file,
        Func<FileStream, Task> verify,
        DownloadProgressCallback? progress,
        CancellationToken cancel,
        Func<Task<IReadOnlyList<string>>>? moreUrls = null)
    {
        Exception? lastError = null;
        var queue = new List<string>(urls);
        var sink = new LocalWriteStream(file);

        for (var i = 0; ; i++)
        {
            if (i == queue.Count)
            {
                if (moreUrls == null)
                    break;

                try
                {
                    queue.AddRange(await moreUrls());
                }
                catch (Exception e) when (!cancel.IsCancellationRequested)
                {
                    Log.Warning(e, "Failed to look up fallback download URLs");
                }

                moreUrls = null;
                if (i == queue.Count)
                    break;
            }

            var url = queue[i];
            Log.Debug("Downloading {DownloadUrl}", url);

            try
            {
                sink.Reset();
                await _http.DownloadToStream(url, sink, progress, cancel);
                await sink.FlushAsync(cancel);
                await verify(file);
                return;
            }
            catch (LocalWriteException e)
            {
                Log.Error(e.InnerException, "Writing download from {DownloadUrl} to disk failed", url);
                ExceptionDispatchInfo.Throw(e.InnerException!);
            }
            catch (Exception e) when (!cancel.IsCancellationRequested)
            {
                lastError = e;
                Log.Warning(e, "Download from {DownloadUrl} failed, trying the next mirror", url);
            }
        }

        throw lastError ?? new UpdateException("No download URLs available.");
    }

    internal static List<string> ExpandMirrors(string url, IEnumerable<RobustCdn> cdns)
    {
        var result = new List<string> { url };

        foreach (var cdn in cdns)
        {
            var mirrors = cdn.BaseUrl.Urls;
            var own = mirrors.FirstOrDefault(m => url.StartsWith(m, StringComparison.OrdinalIgnoreCase));
            if (own == null)
                continue;

            var path = url[own.Length..];
            foreach (var mirror in mirrors)
            {
                var candidate = mirror + path;
                if (!result.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                    result.Add(candidate);
            }
        }

        return result;
    }

    private static async Task<string> ComputeSha256Hex(Stream stream, CancellationToken cancel)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancel);
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// Verify a downloaded file against a signature using the public key of any
    /// configured CDN. Content is trusted if it is validly signed by ANY trusted
    /// CDN key - every CDN in the config is equally trusted, and a manifest's
    /// absolute download URL doesn't tell us which CDN it resolved to.
    /// </summary>
    private bool VerifyAgainstAnyCdn(FileStream stream, string signature)
    {
        foreach (var cdn in _cdns.Cdns)
        {
            _ = stream.Seek(0, SeekOrigin.Begin);
            if (VerifySignature(stream, signature, cdn.PublicKey))
                return true;
        }

        return false;
    }

    private static unsafe bool VerifySignature(FileStream stream, string signature, string publicKeyPem)
    {
        if (stream.Length > int.MaxValue)
            throw new InvalidOperationException("Unable to handle files larger than 2 GiB");

        // Use memory-mapped file here so we don't have to read the whole thing in at once.
        using var memoryMapped = MemoryMappedFile.CreateFromFile(
            stream,
            null,
            0,
            MemoryMappedFileAccess.Read,
            HandleInheritability.None,
            leaveOpen: true);

        using var accessor = memoryMapped.CreateViewAccessor(0, stream.Length, MemoryMappedFileAccess.Read);
        byte* ptr = null;
        accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);

        try
        {
            var span = new ReadOnlySpan<byte>(ptr, (int)stream.Length);

            var pubKey = PublicKey.Import(
                SignatureAlgorithm.Ed25519,
                Encoding.UTF8.GetBytes(publicKeyPem),
                KeyBlobFormat.PkixPublicKeyText);

            var sigBytes = Convert.FromHexString(signature);

            return SignatureAlgorithm.Ed25519.Verify(pubKey, span, sigBytes);
        }
        finally
        {
            accessor.SafeMemoryMappedViewHandle.ReleasePointer();
        }
    }

    public async Task<bool> DownloadModuleIfNecessary(
        string moduleName,
        string moduleVersion,
        EngineModuleManifest manifest,
        DownloadProgressCallback? progress = null,
        CancellationToken cancel = default)
    {
        var settings = _settings.GetSettings();
#if DEVELOPMENT
        if (settings.EngineOverrideEnabled)
        {
            // For modules we have to extract them from the zip to disk first.
            // So it's a little more involved than just giving a different zip path to the launch code.
            await CopyOverrideModule(moduleName);
            return true;
        }
#endif

        // Currently the module handling code assumes all modules need straight extract to disk.
        // This works for CEF, but who knows what the future might hold?

        Log.Debug("Checking to download {ModuleName} {ModuleVersion}", moduleName, moduleVersion);

        var versionData = manifest.Modules[moduleName].Versions[moduleVersion];

        if (versionData.Insecure)
            throw new UpdateException("Selected module version is insecure!");

        Log.Debug("Selected module {ModuleName} {ModuleVersion}", moduleName, moduleVersion);

        var alreadyInstalled = _settings.GetModules().Contains(new InstalledEngineModule(moduleName, moduleVersion));

        if (alreadyInstalled)
        {
            Log.Debug("Already have module installed!");
            return false;
        }

        Log.Information("Installing {ModuleName} {ModuleVersion}", moduleName, moduleVersion);

        var bestRid = RidUtility.FindBestRid(versionData.Platforms.Keys) ?? throw new NoModuleForPlatformException("No module version available for our platform!");
        Log.Debug("Selecting RID {Rid}", bestRid);

        var platformData = versionData.Platforms[bestRid];

        GetModulePaths(
            moduleName,
            moduleVersion,
            out var moduleDiskPath,
            out var moduleVersionDiskPath);

        await ClearModuleDir(moduleDiskPath, moduleVersionDiskPath);

        {
            await using var tempFile = TempFile.CreateTempFile();
            Log.Debug("Downloading into temp file: {TempFilePath}", tempFile.Name);

            await DownloadFromMirrors(
                ExpandMirrors(platformData.Url, _cdns.Cdns),
                tempFile,
                VerifyModuleSignature,
                progress,
                cancel);

            // Done downloading, extract...
            Log.Debug("Download complete, extracting into: {TempFilePath}", moduleVersionDiskPath);

            tempFile.Seek(0, SeekOrigin.Begin);

            // CEF is so horrifically huge I'm enabling disk compression on it.
            MarkDirectoryCompress(moduleVersionDiskPath);

            ExtractModule(moduleName, moduleVersionDiskPath, tempFile);
        }

        _settings.AddInstalledModule(new InstalledEngineModule(moduleName, moduleVersion));

        Log.Debug("Done installing module!");

        return true;

        Task VerifyModuleSignature(FileStream stream)
        {
            if (!VerifyAgainstAnyCdn(stream, platformData.Sig))
            {
#if DEBUG
                if (settings.DisableSigning)
                {
                    Log.Debug("Signature check failed for module, ignoring because signing disabled");
                }
                else
#endif
                {
                    throw new UpdateException("Failed to verify module signature!");
                }
            }

            return Task.CompletedTask;
        }
    }

    private async Task CopyOverrideModule(string name)
    {
        GetModulePaths(
            name,
            OverrideVersionName,
            out var modPath,
            out var modVersionPath);

        await ClearModuleDir(modPath, modVersionPath);

        var zipPath = FindOverrideZip(name, _settings.GetSettings().EngineOverridePath);
        await using var zip = File.OpenRead(zipPath);

        // Note: not marking directory as compressed since it would take a while to start.
        ExtractModule(name, modVersionPath, zip);
    }

    private void GetModulePaths(
        string module,
        string version,
        out string moduleDiskPath,
        out string moduleVersionDiskPath)
    {
        moduleDiskPath = "";
        moduleVersionDiskPath = "";
        moduleDiskPath = Path.Combine(_settings.GetSettings().DirModuleInstallations, module);
        moduleVersionDiskPath = Path.Combine(moduleDiskPath, version);
    }

    private static async Task ClearModuleDir(string modDiskPath, string modVersionDiskPath)
        => await Task.Run(() =>
        {
            // Avoid disk IO hang.
            EnsureDirectoryExists(modDiskPath);
            EnsureDirectoryExists(modVersionDiskPath);
            ClearDirectory(modVersionDiskPath);
        }, CancellationToken.None);

    private static void ExtractModule(string moduleName, string moduleVersionDiskPath, FileStream tempFile)
    {
        ExtractZipToDirectory(moduleVersionDiskPath, tempFile);

        // Chmod required files.
        if (OperatingSystem.IsLinux())
        {
            switch (moduleName)
            {
                case "Robust.Client.WebView":
                    ChmodPlusX(Path.Combine(moduleVersionDiskPath, "Robust.Client.WebView"));
                    break;
            }
        }
    }

    /// <summary>
    /// Resolve the public key the loader should use to verify the given engine
    /// version. Picks the CDN key that actually validates the installed engine zip,
    /// writes it out, and returns the path for the loader to read.
    /// </summary>
    public string GetEnginePublicKeyPath(string engineVersion)
    {
        var cdns = _cdns.Cdns;
        var settings = _settings.GetSettings();

#if DEVELOPMENT
        if (settings.EngineOverrideEnabled)
        {
            // Override builds aren't signed; loader runs with signing disabled and ignores the key.
            return WriteLoaderKey(cdns[0].PublicKey);
        }
#endif

        var signature = GetEngineSignature(engineVersion);
        var enginePath = GetEnginePath(engineVersion);

        var fileInfo = new FileInfo(enginePath);
        var stamp = new VerifiedEngineKey(signature, fileInfo.Length, fileInfo.LastWriteTimeUtc, "");

        if (_verifiedEngineKeys.TryGetValue(engineVersion, out var cached)
            && cached with { PublicKey = "" } == stamp
            && cdns.Any(c => c.PublicKey == cached.PublicKey))
        {
            return WriteLoaderKey(cached.PublicKey);
        }

        using var stream = File.OpenRead(enginePath);

        foreach (var cdn in cdns)
        {
            stream.Seek(0, SeekOrigin.Begin);
            if (VerifySignature(stream, signature, cdn.PublicKey))
            {
                _verifiedEngineKeys[engineVersion] = stamp with { PublicKey = cdn.PublicKey };
                return WriteLoaderKey(cdn.PublicKey);
            }
        }

#if DEBUG
        if (settings.DisableSigning)
        {
            Log.Warning(
                "No configured CDN key validated engine {Version}, but signing is disabled; passing first CDN key.",
                engineVersion);
            return WriteLoaderKey(cdns[0].PublicKey);
        }
#endif

        throw new UpdateException($"No configured CDN public key validates installed engine {engineVersion}!");
    }

    private string WriteLoaderKey(string publicKeyPem)
    {
        var settings = _settings.GetSettings();
        var bytes = Encoding.UTF8.GetBytes(publicKeyPem);
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))[..16].ToLowerInvariant();
        var path = $"{settings.PathLoaderSigningKey}_{hash}";

        if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
            return path;

        EnsureDirectoryExists(settings.DirLauncherData);

        var tmp = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllBytes(tmp, bytes);
        try
        {
            File.Move(tmp, path, overwrite: true);
        }
        catch (IOException) when (File.Exists(path))
        {
            File.Delete(tmp);
        }

        return path;
    }

    public async Task<EngineModuleManifest> GetEngineModuleManifest(CancellationToken cancel = default)
    {
        Exception? lastError = null;

        foreach (var cdn in _cdns.Cdns)
        {
            try
            {
                var manifest = await cdn.ModulesManifest
                    .GetFromJsonAsync<EngineModuleManifest>(_http, cancel);

                if (manifest != null)
                    return manifest;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                lastError = e;
                Log.Warning(e, "Failed to load module manifest from a CDN, trying next one...");
            }
        }

        throw new InvalidDataException("Unable to load module manifest from any CDN.", lastError);
    }

    public async Task DoEngineCullMaybeAsync(SqliteConnection contenCon)
    {
        Log.Debug("Checking to cull engine dependencies");

        // Cull main engine installations.

        var origModulesUsed = contenCon
            .Query<(string, string)>("SELECT DISTINCT ModuleName, ModuleVersion FROM ContentEngineDependency")
            .ToList();

        // GOD DAMNIT more bodging everything together.
        // The code sucks.
        // My shitty hacks to do engine version redirection fall apart here as well.
        var modulesUsed = new HashSet<(string, string)>();
        foreach (var (name, version) in origModulesUsed)
        {
            if (name == "Robust")
            {
                FoundVersionInfo? redirect;
                try
                {
                    redirect = await GetVersionInfo(version);
                }
                catch (Exception e)
                {
                    Log.Warning(e, "Failed to resolve engine version {EngineVersion}, skipping engine cull", version);
                    return;
                }

                _ = modulesUsed.Add(("Robust", redirect?.Version ?? version));
            }
            else
            {
                _ = modulesUsed.Add((name, version));
            }
        }

        foreach (var installation in _settings.GetEngines().Values.Where(i => !modulesUsed.Contains(("Robust", i.Version))).ToArray())
        {
            Log.Debug("Culling unused version {engineVersion}", installation.Version);

            var path = GetEnginePath(installation.Version);

            try
            {
                await Task.Run(() => File.Delete(path));
            }
            catch (Exception e)
            {
                Log.Warning(e, "Failed to delete engine {EngineVersion}", installation.Version);
                continue;
            }

            _settings.RemoveInstalledEngine(installation.Version);
        }

        // Cull modules
        foreach (var module in _settings.GetModules().Where(m => !modulesUsed.Contains((m.Name, m.Version))).ToArray())
        {
            Log.Debug("Culling unused module {EngineModule}", module);

            var path = GetEngineModule(module.Name, module.Version);

            try
            {
                await Task.Run(() =>
                {
                    if (Directory.Exists(path))
                        Directory.Delete(path, true);
                });
            }
            catch (Exception e)
            {
                Log.Warning(e, "Failed to delete module {EngineModule}", module);
                continue;
            }

            _settings.RemoveInstalledModule(module);
        }
    }

    public void ClearAllEngines()
    {
        _settings.WriteEngines([]);
        _settings.WriteModules([]);

        var settings = _settings.GetSettings();

        if (Directory.Exists(settings.DirEngineInstallations))
        {
            foreach (var file in Directory.EnumerateFiles(settings.DirEngineInstallations))
                File.Delete(file);
        }

        if (Directory.Exists(settings.DirModuleInstallations))
        {
            foreach (var dir in Directory.EnumerateDirectories(settings.DirModuleInstallations))
                Directory.Delete(dir, recursive: true);
        }
    }

    private static string FindOverrideZip(string name, string dir)
    {
        var foundRids = new List<string>();

        var regex = new Regex(@$"^{Regex.Escape(name)}_([a-z\-\d]+)\.zip$");
        foreach (var item in Directory.EnumerateFiles(dir))
        {
            var fileName = Path.GetFileName(item);
            var match = regex.Match(fileName);
            if (!match.Success)
                continue;

            foundRids.Add(match.Groups[1].Value);
        }

        var rid = RidUtility.FindBestRid(foundRids) ?? throw new UpdateException($"Unable to find overriden {name} for current platform");
        var path = Path.Combine(dir, $"{name}_{rid}.zip");
        Log.Warning("Using override for {Name}: {Path}", name, path);
        return path;
    }
}
