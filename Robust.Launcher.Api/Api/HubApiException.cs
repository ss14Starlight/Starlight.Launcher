using System;
using System.Net;

namespace Robust.Launcher.Api.Api;

/// <summary>
/// Represents an error that occurs while communicating with the hub API.
/// </summary>
public sealed class HubApiException : Exception
{
    /// <summary>
    /// Gets the HTTP status code returned by the hub, if available.
    /// </summary>
    public HttpStatusCode? StatusCode { get; }

    /// <summary>
    /// Gets the suggested delay before retrying the request, if provided by the hub.
    /// </summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>
    /// Gets the URL of the request that caused the exception.
    /// </summary>
    public string? RequestUrl { get; }

    /// <summary>
    /// Gets a value indicating whether the request was rejected due to rate limiting.
    /// </summary>
    public bool IsRateLimited => StatusCode == HttpStatusCode.TooManyRequests;

    /// <summary>
    /// Gets a value indicating whether the request timed out.
    /// </summary>
    public bool IsTimeout { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="HubApiException"/> class.
    /// </summary>
    public HubApiException(
        string message,
        HttpStatusCode? statusCode = null,
        TimeSpan? retryAfter = null,
        string? requestUrl = null,
        bool isTimeout = false,
        Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        RetryAfter = retryAfter;
        RequestUrl = requestUrl;
        IsTimeout = isTimeout;
    }
}
