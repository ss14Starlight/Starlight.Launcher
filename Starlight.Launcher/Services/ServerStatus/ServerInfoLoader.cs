using System.Collections.Concurrent;
using System.Threading.Channels;
using Robust.Launcher.Api.Models.ServerStatus;

namespace Starlight.Launcher.Services.ServerStatus;

public sealed class ServerInfoLoader : IDisposable
{
    private const int MaxConcurrent = 8;

    private readonly Channel<ServerStatusData> _queue =
        Channel.CreateUnbounded<ServerStatusData>(new UnboundedChannelOptions { SingleReader = true });
    private readonly ConcurrentDictionary<string, byte> _inFlight = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(MaxConcurrent, MaxConcurrent);
    private readonly CancellationTokenSource _cts = new();

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

    public void Dispose()
    {
        _cts.Cancel();
        _ = _queue.Writer.TryComplete();
        try { _cts.Dispose(); } catch { }
        try { _gate.Dispose(); } catch { }
    }
}
