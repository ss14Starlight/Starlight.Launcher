using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Robust.Launcher.Api.Models.Data;
using Starlight.Launcher.WebUI.Models.Data;
using Starlight.Launcher.WebUI.Models.ServerStatus;
using Starlight.Launcher.WebUI.Models.Settings;

namespace Starlight.Launcher.Services.Settings;

public sealed partial class SettingsService : IAsyncDisposable
{
    #region Variables

    private CancellationTokenSource? _settingsSaveCts;
    private CancellationTokenSource? _favoritesSaveCts;
    private CancellationTokenSource? _loginsSaveCts;
    private CancellationTokenSource? _enginesSaveCts;
    private CancellationTokenSource? _modulesSaveCts;

    private AppSettings _settings;
    private readonly SemaphoreSlim _settingsLock = new(1, 1);
    private readonly string _filePath;

    private List<FavoriteServer> _favorites;
    private readonly SemaphoreSlim _favoritesLock = new(1, 1);
    private readonly string _favoritesPath;
    private volatile HashSet<string> _favoriteAddresses = new(StringComparer.OrdinalIgnoreCase);

    private Dictionary<Guid, LoginInfo> _logins = new();
    private readonly SemaphoreSlim _loginsLock = new(1, 1);
    private readonly string _loginsPath;
    private readonly string _loginKeyPath;

    // Version to engine version info(signature)
    private Dictionary<string, InstalledEngineVersion> _engineInstallations;
    private readonly SemaphoreSlim _enginesLock = new(1, 1);
    private readonly string _enginesPath;

    private HashSet<(string Version, string Name)> _engineModules;
    private readonly SemaphoreSlim _modulesLock = new(1, 1);
    private readonly string _modulesPath;

    private readonly ILogger<SettingsService> _logger;
    private readonly ILoginKeyProvider _keyProvider;

    public event Action? FavoritesChanged;

    public event Action? LoginsChanged;

    public event Action? EnginesChanged;

    public event Action? ModulesChanged;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly ConcurrentDictionary<string, Task> _pendingSaves = new();

    #endregion

    public SettingsService(ILogger<SettingsService> logger, ILoginKeyProvider keyProvider)
    {
        _logger = logger;
        _keyProvider = keyProvider;
        _filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Starlight.Launcher", "settings.json");
        _settings = LoadJson(_filePath, new AppSettings());
        _loginsPath = Path.Combine(_settings.DirLauncherData, "logins.json");
        _loginKeyPath = Path.Combine(_settings.DirLauncherData, "logins.key");
        _favoritesPath = Path.Combine(_settings.DirLauncherData, "favorites.json");
        _enginesPath = Path.Combine(_settings.DirLauncherData, "engines.json");
        _modulesPath = Path.Combine(_settings.DirLauncherData, "modules.json");
        _favorites = LoadJson(_favoritesPath, new List<FavoriteServer>());
        _engineInstallations = LoadJson(_enginesPath, new List<InstalledEngineVersion>()).ToDictionary(x => x.Version);
        _engineModules = LoadJson(_modulesPath, new HashSet<(string Version, string Name)>());
        RebuildFavoritesIndex(); // Rebuild addresses after load.
    }

    public IReadOnlySet<string> GetFavoriteAddressesSnapshot() => _favoriteAddresses;

    private void ScheduleSaveInternal(
        ref CancellationTokenSource? ctsField,
        Func<Task> saveAction,
        string what)
    {
        var cts = new CancellationTokenSource();
        var old = Interlocked.Exchange(ref ctsField, cts);
        old?.Cancel();
        old?.Dispose();

        var delay = _settings.SaveIntervalMs;

        var task = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, cts.Token);
                await saveAction();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Auto-save failed for {what}", what);
            }
        });

        _pendingSaves[what] = task;
    }

    private async Task SaveJsonAsync<T>(string path, SemaphoreSlim slim, T obj)
    {
        await slim.WaitAsync();
        try
        {
            var json = JsonSerializer.Serialize(obj, _jsonOptions);
            await WriteFileSafeAsync(json, Path.GetDirectoryName(path)!, path);
#if DEBUG
            _logger.LogDebug("{0} saved", path);
#endif
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save {0}", path);
        }
        finally
        {
            slim.Release();
        }
    }

    public async Task SaveAllAsync()
    {
        var tasks = new List<Task>
        {
            SaveJsonAsync(_filePath, _settingsLock, _settings),
            SaveJsonAsync(_favoritesPath, _favoritesLock, _favorites),
            SaveLoginsEncryptedAsync(),
            SaveJsonAsync(_enginesPath, _enginesLock, _engineInstallations.Values),
            SaveJsonAsync(_modulesPath, _modulesLock, _engineModules)
        };
        await Task.WhenAll(tasks);
    }

    private static async Task WriteFileSafeAsync(string content, string dir, string filePath)
    {
        _ = Directory.CreateDirectory(dir);

        var tempFile = filePath + ".tmp";
        await File.WriteAllTextAsync(tempFile, content);
        File.Move(tempFile, filePath, true);
    }

    #region Sync Methods

    /// <summary>
    /// Gets the current AppSettings instance under a lock to ensure thread-safe access.
    /// </summary>
    /// <remarks>Acquires _settingsLock before reading and releases it in a finally block so the lock is
    /// always released.</remarks>
    public AppSettings GetSettings()
    {
        _settingsLock.Wait();
        try
        {
            return _settings;
        }
        finally
        {
            _ = _settingsLock.Release();
        }
    }

    /// <summary>
    /// Updates the in-memory application settings under a lock and schedules an asynchronous save.
    /// </summary>
    /// <remarks>Acquires an internal lock to ensure thread-safe replacement of the settings. The save is
    /// scheduled for asynchronous persistence and may not occur immediately.</remarks>
    public void WriteSettings(AppSettings settings)
    {
        AppSettings old;
        _settingsLock.Wait();
        try
        {
            old = _settings;
            _settings = settings;
        }
        finally
        {
            _ = _settingsLock.Release();
        }

        NotifySettingsChanged(old, settings);
        ScheduleSaveInternal(ref _settingsSaveCts, () => SaveJsonAsync(_filePath, _settingsLock, _settings), "settings");
    }

    #endregion

    #region Async Methods

    /// <summary>
    /// Prefer this to use in async methods to avoid races.
    /// </summary>
    public async Task<AppSettings> GetSettingsAsync()
    {
        await _settingsLock.WaitAsync();
        try
        {
            return _settings;
        }
        finally
        {
            _ = _settingsLock.Release();
        }
    }

    /// <summary>
    /// Prefer this to use in async methods to avoid races.
    /// </summary>
    public async Task WriteSettingsAsync(AppSettings settings)
    {
        AppSettings old;
        await _settingsLock.WaitAsync();
        try
        {
            old = settings;
            _settings = settings;
        }
        finally
        {
            _ = _settingsLock.Release();
        }

        NotifySettingsChanged(old, settings);
        ScheduleSaveInternal(ref _settingsSaveCts, () => SaveJsonAsync(_filePath, _settingsLock, _settings), "settings");
    }

    #endregion

    public async ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _settingsSaveCts, null)?.Cancel();
        Interlocked.Exchange(ref _favoritesSaveCts, null)?.Cancel();
        Interlocked.Exchange(ref _loginsSaveCts, null)?.Cancel();

        await SaveAllAsync();

        _settingsLock.Dispose();
        _favoritesLock.Dispose();
        _loginsLock.Dispose();
        _enginesLock.Dispose();
        _modulesLock.Dispose();
        _keyLock.Dispose();

        GC.SuppressFinalize(this);
    }

    public async Task FlushPendingSavesAsync()
    {
        await SaveAllAsync();
        var pending = _pendingSaves.Values.ToArray();
        try { await Task.WhenAll(pending); }
        catch { }
    }

    public async Task CacheFilters(ServerListFilters filters)
    {
        AppSettings old, updated;
        await _settingsLock.WaitAsync();
        try
        {
            old = _settings;
            updated = _settings with { CachedFilters = filters };
            _settings = updated;
        }
        finally { _ = _settingsLock.Release(); }

        NotifySettingsChanged(old, updated);
        ScheduleSaveInternal(ref _settingsSaveCts, () => SaveJsonAsync(_filePath, _settingsLock, _settings), "settings");
    }

    private T LoadJson<T>(string path, T fallback)
    {
        try
        {
            if (!File.Exists(path))
            {
                _logger.LogInformation("Can't find {0} file, fallback to empty.", path);
                return fallback;
            }

            var json = File.ReadAllText(path);
            var result = JsonSerializer.Deserialize<T>(json) ?? fallback;

            _logger.LogInformation("Successfully loaded {0}", path);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load {0}, using empty list", path);
            return fallback;
        }
    }
}
