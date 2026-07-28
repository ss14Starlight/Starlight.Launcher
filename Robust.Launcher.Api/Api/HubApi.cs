using Robust.Launcher.Api.Models;
using Robust.Launcher.Api.Utility;
using Microsoft.Extensions.Logging;
using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Robust.Launcher.Api.Api;

/// <summary>
/// Provides methods for interacting with the hub API.
/// </summary>
public sealed class HubApi
{
    private readonly HttpClient _http;
    private readonly ILogger<HubApi> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="HubApi"/> class.
    /// </summary>
    public HubApi(HttpClient http, ILogger<HubApi> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <summary>
    /// Retrieves the list of available servers from the hub.
    /// </summary>
    public async Task<ServerListEntry[]> GetServers(UrlFallbackSet hubUri, CancellationToken cancel)
    {
        HubApiException? lastError = null;
        foreach (var url in hubUri.Urls)
        {
            var baseUrl = url;
            if (!baseUrl.EndsWith('/'))
            {
                _logger.LogWarning(
                    "Hub URL {Url} is missing a trailing slash; appending one", baseUrl);
                baseUrl += '/';
            }

            var finalUrl = baseUrl + "api/servers";
            try
            {
                _logger.LogDebug("Fetching server list from {Url}", finalUrl);
                var result = await RequestJsonAsync<ServerListEntry[]>(finalUrl, cancel)
                    .ConfigureAwait(false);
                _logger.LogDebug("Got {Count} servers from {Url}", result.Length, finalUrl);
                return result;
            }
            catch (HubApiException ex) when (!ex.IsRateLimited)
            {
                _logger.LogWarning(
                    "Failed to fetch from {Url}: {Status}. Trying next fallback if any.",
                    finalUrl, ex.StatusCode);
                lastError = ex;
            }
        }
        throw lastError ?? new HubApiException("No URLs in fallback set");
    }

    /// <summary>
    /// Retrieves information about a server from the hub.
    /// </summary>
    public async Task<ServerInfo> GetServerInfo(
        string serverAddress,
        string hubAddress,
        CancellationToken cancel)
    {
        var url = $"{hubAddress}api/servers/info?url={Uri.EscapeDataString(serverAddress)}";

#if DEBUG
        _logger.LogDebug("Requesting info for {ServerAddress} via {Url}", serverAddress, url);
#endif

        return await RequestJsonAsync<ServerInfo>(url, cancel).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a request and deserializes the JSON response.
    /// </summary>
    private async Task<T> RequestJsonAsync<T>(string url, CancellationToken cancel)
    {
        HttpResponseMessage response;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            throw new HubApiException($"Request to {url} timed out",
                requestUrl: url, isTimeout: true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            throw new HubApiException(
                $"HTTP error requesting {url}: {ex.Message}",
                statusCode: ex.StatusCode,
                requestUrl: url,
                inner: ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                TimeSpan? retryAfter = null;
                if (response.Headers.RetryAfter is { } ra)
                {
                    if (ra.Delta.HasValue)
                        retryAfter = ra.Delta.Value;
                    else if (ra.Date.HasValue)
                        retryAfter = ra.Date.Value - DateTimeOffset.UtcNow;
                }

                throw new HubApiException(
                    $"Hub returned {(int)response.StatusCode} {response.ReasonPhrase} for {url}",
                    statusCode: response.StatusCode,
                    retryAfter: retryAfter,
                    requestUrl: url);
            }

            try
            {
                return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancel).ConfigureAwait(false)
                    ?? throw new HubApiException($"Response body was null for {url}", requestUrl: url);
            }
            catch (JsonException ex)
            {
                throw new HubApiException(
                    $"Failed to parse JSON from {url}: {ex.Message}",
                    requestUrl: url, inner: ex);
            }
            catch (NotSupportedException ex)
            {
                throw new HubApiException(
                    $"Hub at {url} returned non-JSON content ({response.Content.Headers.ContentType}): {ex.Message}",
                    requestUrl: url, inner: ex);
            }
        }
    }

    /// <summary>
    /// Represents a server entry returned by the hub.
    /// </summary>
    public sealed record ServerListEntry(string Address, ServerApi.ServerStatus StatusData);
}
