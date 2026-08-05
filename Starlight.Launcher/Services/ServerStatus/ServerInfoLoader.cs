using System.Collections.Concurrent;
using System.Threading.Channels;
using Robust.Launcher.Api.Models.ServerStatus;

namespace Starlight.Launcher.Services.ServerStatus;

public sealed class ServerInfoLoader : IDisposable
{
    private const int MaxConcurrent = 8;

    private readonly Func<ServerStatusData, Task> _fetch;
    private readonly Channel<ServerStatusData> _queue =
        Channel.CreateUnbounded<ServerStatusData>(new UnboundedChannelOptions { SingleReader = true });
    private readonly ConcurrentDictionary<string, byte> _inFlight = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(MaxConcurrent, MaxConcurrent);
    private readonly CancellationTokenSource _cts = new();

    public ServerInfoLoader(Func<ServerStatusData, Task> fetch)
    {
        _fetch = fetch;
        _ = Task.Run(() => PumpAsync(_cts.Token));
    }
    public void Request(ServerStatusData? data)
    {
        if (data is null || data.StatusInfo != ServerStatusInfoCode.NotFetched)
            return;
        if (string.IsNullOrEmpty(data.Address))
            return;
        if (!_inFlight.TryAdd(data.Address, 0))
            return;

        if (!_queue.Writer.TryWrite(data))
            _ = _inFlight.TryRemove(data.Address, out _);
    }

    private async Task PumpAsync(CancellationToken token)
    {
        try
        {
            await foreach (var data in _queue.Reader.ReadAllAsync(token).ConfigureAwait(false))
            {
                await _gate.WaitAsync(token).ConfigureAwait(false);
                _ = ProcessAsync(data);
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task ProcessAsync(ServerStatusData data)
    {
        try
        {
            if (data.StatusInfo == ServerStatusInfoCode.NotFetched)
                await _fetch(data).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch { }
        finally
        {
            _ = _gate.Release();
            _ = _inFlight.TryRemove(data.Address, out _);
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _ = _queue.Writer.TryComplete();
        try { _cts.Dispose(); } catch { }
        try { _gate.Dispose(); } catch { }
    }
}
