using System.Text.Json;
using Serilog;

namespace Starlight.Launcher.Services.Journal;

public sealed class JsonFileStore<T> where T : class, new()
{
    private static readonly TimeSpan _saveDelay = TimeSpan.FromSeconds(2);
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = false };

    private readonly string _path;
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly T _value;
    private CancellationTokenSource? _saveCts;
    private bool _dirty;

    private readonly bool _readOnly;

    public JsonFileStore(string path)
    {
        _path = path;
        (_value, _readOnly) = Load(path);
    }

    public TResult Read<TResult>(Func<T, TResult> read)
    {
        lock (_gate)
            return read(_value);
    }

    public void Update(Action<T> update)
    {
        lock (_gate)
        {
            update(_value);
            _dirty = true;
        }

        ScheduleSave();
    }

    public async Task FlushAsync()
    {
        Interlocked.Exchange(ref _saveCts, null)?.Cancel();
        await SaveAsync();
    }

    private void ScheduleSave()
    {
        var cts = new CancellationTokenSource();
        var token = cts.Token;
        var old = Interlocked.Exchange(ref _saveCts, cts);
        old?.Cancel();
        old?.Dispose();

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_saveDelay, token);
                await SaveAsync();
            }
            catch (OperationCanceledException) { }
        }, token);
    }

    private async Task SaveAsync()
    {
        if (_readOnly)
            return;

        await _writeLock.WaitAsync();
        try
        {
            string json;
            lock (_gate)
            {
                if (!_dirty)
                    return;

                json = JsonSerializer.Serialize(_value, _jsonOptions);
                _dirty = false;
            }

            _ = Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            await File.WriteAllTextAsync(temp, json);
            File.Move(temp, _path, true);
        }
        catch (Exception e)
        {
            Log.Error(e, "Failed to save {Path}", _path);
        }
        finally
        {
            _ = _writeLock.Release();
        }
    }

    private static (T, bool ReadOnly) Load(string path)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return (new T(), false);
        }
        catch (Exception e)
        {
            Log.Error(e, "Failed to read {Path}; changes won't be saved this session", path);
            return (new T(), true);
        }

        try
        {
            return (JsonSerializer.Deserialize<T>(json) ?? new T(), false);
        }
        catch (JsonException e)
        {
            var backup = path + $".corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}.bak";
            try { File.Move(path, backup, true); } catch { /* best effort */ }

            Log.Error(e, "Failed to parse {Path}; backed up to {Backup}", path, backup);
            return (new T(), false);
        }
    }
}
