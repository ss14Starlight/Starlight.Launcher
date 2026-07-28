using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Robust.Launcher.Api.Api;
using Robust.Launcher.Api.Utility;

namespace Robust.Launcher.Api.Models.ServerStatus;

/// <summary>
///     Caches information pulled from servers and updates it asynchronously.
/// </summary>
public sealed class ServerStatusCache : IServerSource
{
    private readonly ConcurrentDictionary<string, CacheReg> _cachedData = new();
    private readonly HttpClient _http;
    private readonly ILogger<ServerStatusCache> _logger;

    public ServerStatusCache(HttpClient http, ILogger<ServerStatusCache> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <summary>
    ///     Gets an uninitialized status for a server address.
    ///     This does NOT start fetching the data.
    /// </summary>
    /// <param name="serverAddress">The address of the server to fetch data for.</param>
    public ServerStatusData GetStatusFor(string serverAddress, string? hubAddress = null)
        => _cachedData.GetOrAdd(
            serverAddress,
            static (addr, hub) => new CacheReg(hub != null ? new(addr, hub) : new(addr)),
            hubAddress).Data;

    /// <summary>
    ///     Do the initial status update for a server status. This only acts once.
    /// </summary>
    public bool TryInitialUpdateStatus(ServerStatusData data)
    {
        var reg = _cachedData[data.Address];
        if (reg.DidInitialStatusUpdate)
            return false;

        UpdateStatusFor(reg);
        return true;
    }

    private async void UpdateStatusFor(CacheReg reg)
    {
        reg.DidInitialStatusUpdate = true;
        reg.DidInitialPing = true;
        await reg.Semaphore.WaitAsync();
        var cancelSource = reg.Cancellation = new CancellationTokenSource();
        var cancel = cancelSource.Token;
        try
        {
            await UpdateStatusFor(reg.Data, _http, cancel);
        }
        finally
        {
            reg.Semaphore.Release();
        }
    }

    private static async Task MeasurePing(ServerStatusData data, string host, int port, CancellationToken cancel)
    {
        try
        {
            using var tcp = new TcpClient();
            using var pingCancel = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            pingCancel.CancelAfter(TimeSpan.FromSeconds(2));
            var sw = Stopwatch.StartNew();
            await tcp.ConnectAsync(host, port, pingCancel.Token);
            sw.Stop();
            data.Ping = sw.Elapsed;
        }
        catch { data.Ping = null; }
    }

    public bool TryInitialPing(ServerStatusData data)
    {
        if (!_cachedData.TryGetValue(data.Address, out var reg) || reg.DidInitialPing)
            return false;
        PingFor(reg);
        return true;
    }

    private async void PingFor(CacheReg reg)
    {
        reg.DidInitialPing = true;
        var data = reg.Data;
        if (!UriHelper.TryParseSs14Uri(data.Address, out var parsed))
            return;
        await MeasurePing(data, parsed.Host, parsed.Port, CancellationToken.None);
        data.NotifyChanged();
    }

    public async Task UpdateStatusFor(ServerStatusData data, HttpClient http, CancellationToken cancel)
    {
        try
        {
            if (!UriHelper.TryParseSs14Uri(data.Address, out var parsedAddress))
            {
                _logger.LogWarning("Server {Server} has invalid URI {Uri}", data.Name, data.Address);
                data.Status = ServerStatusCode.Offline;
                data.NotifyChanged();
                return;
            }

            await MeasurePing(data, parsedAddress.Host, parsedAddress.Port, cancel);

            var statusAddr = UriHelper.GetServerStatusAddress(parsedAddress);
            data.Status = ServerStatusCode.FetchingStatus;

            ServerApi.ServerStatus status;
            try
            {
                using (var linkedToken = CancellationTokenSource.CreateLinkedTokenSource(cancel))
                {
                    linkedToken.CancelAfter(TimeSpan.FromSeconds(5));

                    using var response = await http.GetAsync(
                        statusAddr,
                        HttpCompletionOption.ResponseHeadersRead,
                        linkedToken.Token);

                    response.EnsureSuccessStatusCode();

                    status = await response.Content
                                 .ReadFromJsonAsync<ServerApi.ServerStatus>(linkedToken.Token)
                             ?? throw new InvalidDataException();
                }

                cancel.ThrowIfCancellationRequested();
            }
            catch (Exception e) when (e is JsonException or HttpRequestException or InvalidDataException or IOException
                                          or SocketException)
            {
                data.Status = ServerStatusCode.Offline;
                data.NotifyChanged();
                return;
            }

            ApplyStatus(data, status);
        }
        catch (OperationCanceledException)
        {
            data.Ping = null;
            data.Status = ServerStatusCode.Offline;
            data.NotifyChanged();
        }
    }

    public static void ApplyStatus(ServerStatusData data, ServerApi.ServerStatus status)
    {
        data.Status = ServerStatusCode.Online;
        data.Name = status.Name;
        data.PlayerCount = Math.Max(0, status.PlayerCount);
        data.SoftMaxPlayerCount = Math.Max(0, status.SoftMaxPlayerCount);

        data.RoundStatus = status.RunLevel switch
        {
            ServerApi.GameRunLevel.InRound => GameRoundStatus.InRound,
            ServerApi.GameRunLevel.PostRound or ServerApi.GameRunLevel.PreRoundLobby => GameRoundStatus.InLobby,
            _ => GameRoundStatus.Unknown,
        };
        if (status.RoundStartTime != null)
        {
            data.RoundStartTime = DateTime.Parse(status.RoundStartTime, null, System.Globalization.DateTimeStyles.RoundtripKind);
        }

        var baseTags = status.Tags ?? Array.Empty<string>();
        var inferredTags = ServerTagInfer.InferTags(status);

        data.Tags = baseTags.Concat(inferredTags).ToArray();

        data.NotifyChanged();
    }

    public async Task UpdateInfoForCore(ServerStatusData data, Func<CancellationToken, Task<ServerInfo?>> fetch)
    {
        if (data.Status != ServerStatusCode.Online)
        {
            _logger.LogError("Refusing to fetch info for server {Server} before we know it's online", data.Address);
            return;
        }

        CancellationToken cancel;
        lock (data.InfoLock)
        {
            if (data.StatusInfo == ServerStatusInfoCode.Fetching)
                return;

            data.InfoCancel?.Cancel();
            data.InfoCancel?.Dispose();
            data.InfoCancel = new CancellationTokenSource();
            cancel = data.InfoCancel.Token;
            data.StatusInfo = ServerStatusInfoCode.Fetching;
        }

        ServerInfo info;
        try
        {
            using (var linkedToken = CancellationTokenSource.CreateLinkedTokenSource(cancel))
            {
                linkedToken.CancelAfter(TimeSpan.FromSeconds(5));
                info = await fetch(linkedToken.Token).ConfigureAwait(false) ?? throw new InvalidDataException("Hub returned null server info");
            }

            cancel.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
        {
            data.StatusInfo = ServerStatusInfoCode.NotFetched;
            data.NotifyChanged();
            return;
        }
        catch (Exception e) when (e is JsonException or HttpRequestException or InvalidDataException)
        {
            data.StatusInfo = ServerStatusInfoCode.Error;
            data.NotifyChanged();
            return;
        }
        catch (HubApiException apiException)
        {
#if DEBUG
            if (apiException.IsRateLimited)
                _logger.LogDebug("Hub: {Url} returned 429 exception(Too Many Requests)!", apiException.RequestUrl);
#endif
            data.StatusInfo = ServerStatusInfoCode.Error;
            data.NotifyChanged();
            return;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Unexpected error fetching info for {Address}", data.Address);
            data.StatusInfo = ServerStatusInfoCode.Error;
            data.NotifyChanged();
            return;
        }

        data.Description = info.Desc;
        data.Links = info.Links;
        data.StatusInfo = ServerStatusInfoCode.Fetched;
        data.NotifyChanged();
    }

    public void Refresh()
    {
        // TODO: This refreshes everything.
        // Which means if you're hitting refresh on your home page, it'll refresh the servers list too.
        // This is wasteful.

        foreach (var datum in _cachedData.Values)
        {
            if (!datum.DidInitialStatusUpdate)
                continue;

            datum.Cancellation?.Cancel();
            datum.Data.InfoCancel?.Cancel();

            datum.Data.StatusInfo = ServerStatusInfoCode.NotFetched;
            datum.Data.Links = null;
            datum.Data.Description = null;

            UpdateStatusFor(datum);
        }
    }

    public void Clear()
    {
        foreach (var value in _cachedData.Values)
        {
            value.Cancellation?.Cancel();
            value.Data.InfoCancel?.Cancel();
        }

        _cachedData.Clear();
    }

    void IServerSource.UpdateInfoFor(ServerStatusData statusData) =>
        _ = UpdateInfoForCore(statusData, async cancel =>
        {
            var uriBuilder = new UriBuilder(
                UriHelper.GetServerInfoAddress(statusData.Address))
            {
                Query = "can_skip_build=1"
            };

            var url = uriBuilder.ToString();

            try
            {
                _logger.LogDebug(
                    "Updating server info. Address: {Address}, Url: {Url}",
                    statusData.Address,
                    url);

                var result = await _http.GetFromJsonAsync<ServerInfo>(
                    url,
                    cancel);

                if (result is null)
                {
                    _logger.LogWarning(
                        "Server info response was null. Address: {Address}",
                        statusData.Address);
                }
                else
                {
                    _logger.LogDebug(
                        "Server info updated successfully. Address: {Address}",
                        statusData.Address);
                }

                return result;
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation(
                    "Server info update cancelled. Address: {Address}",
                    statusData.Address);

                throw;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(
                    ex,
                    "HTTP error while updating server info. Address: {Address}, Url: {Url}",
                    statusData.Address,
                    url);

                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Unexpected error while updating server info. Address: {Address}",
                    statusData.Address);

                throw;
            }
        });

    private sealed class CacheReg
    {
        public readonly ServerStatusData Data;
        public readonly SemaphoreSlim Semaphore = new(1);
        public CancellationTokenSource? Cancellation;
        public bool DidInitialStatusUpdate;
        public bool DidInitialPing;

        public CacheReg(ServerStatusData data) => Data = data;
    }
}
