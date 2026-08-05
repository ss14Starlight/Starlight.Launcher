using System.Net.Http.Headers;
using Robust.Launcher.Api.Utility;
using static Robust.Launcher.Api.Utility.HttpUtility;

namespace Starlight.Launcher.Utility;

/// <summary>
/// Provides helper methods for sending and receiving Zstandard-compressed HTTP content.
/// </summary>
public static class HttpUtility
{
    private static readonly StringWithQualityHeaderValue _zStdHeader = new("zstd", 1);

    /// <summary>
    /// Sends an HTTP request and automatically wraps Zstandard-compressed responses.
    /// </summary>
    public static async Task<HttpResponseMessage> SendZStdAsync(
        this HttpClient client,
        HttpRequestMessage message,
        HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead,
        CancellationToken cancel = default)
    {
        message.Headers.AcceptEncoding.Add(_zStdHeader);

        var response = await client.SendAsync(message, completionOption, cancel);

        if (response.Content.Headers.ContentEncoding.LastOrDefault() == "zstd")
        {
            response.Content = new ZStdHttpContent(response.Content);
        }

        return response;
    }

    /// <summary>
    /// Represents HTTP content that is transparently decompressed using Zstandard.
    /// </summary>
    public sealed class ZStdHttpContent : DecompressedContent
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ZStdHttpContent"/> class.
        /// </summary>
        public ZStdHttpContent(HttpContent originalContent) : base(originalContent)
        {
        }

        /// <summary>
        /// Creates a stream that decompresses Zstandard-compressed content.
        /// </summary>
        protected override Stream GetDecompressedStream(Stream originalStream) => new ZStdDecompressStream(originalStream);
    }
}
