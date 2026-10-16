using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using Robust.Launcher.Api.Models;
using Robust.Launcher.Api.Utility;

namespace Starlight.Launcher.Tests;

public sealed class TransientDownloadErrorTests
{
    private static DownloadErrorKind Classify(Exception e, bool networkAvailable = true) =>
        TransientDownloadError.Classify(e, networkAvailable);

    [Fact]
    public void TlsFailure_IsFatal()
    {
        var e = new HttpRequestException(
            HttpRequestError.SecureConnectionError,
            "The SSL connection could not be established",
            new AuthenticationException("Remote certificate is invalid"));

        Assert.Equal(DownloadErrorKind.Fatal, Classify(e));
    }

    [Fact]
    public void AuthenticationExceptionDeepInChain_IsFatal()
    {
        var e = new HttpRequestException("boom", new IOException("wrapped", new AuthenticationException()));

        Assert.Equal(DownloadErrorKind.Fatal, Classify(e));
    }

    [Fact]
    public void DnsFailure_IsOffline()
    {
        var e = new HttpRequestException(
            HttpRequestError.NameResolutionError,
            "No such host is known",
            new SocketException((int)SocketError.HostNotFound));

        Assert.Equal(DownloadErrorKind.Offline, Classify(e));
    }

    [Theory]
    [InlineData(SocketError.NetworkUnreachable)]
    [InlineData(SocketError.NetworkDown)]
    [InlineData(SocketError.HostNotFound)]
    public void OfflineSocketErrors_AreOffline(SocketError error)
    {
        var e = new HttpRequestException(HttpRequestError.ConnectionError, null, new SocketException((int)error));

        Assert.Equal(DownloadErrorKind.Offline, Classify(e));
    }

    [Fact]
    public void ConnectionReset_IsTransient()
    {
        var e = new HttpRequestException(
            HttpRequestError.ConnectionError,
            null,
            new IOException("reset", new SocketException((int)SocketError.ConnectionReset)));

        Assert.Equal(DownloadErrorKind.Transient, Classify(e));
    }

    [Fact]
    public void TransientError_WithoutNetwork_IsOffline()
    {
        var e = new HttpRequestException(HttpRequestError.ConnectionError, null, new SocketException((int)SocketError.ConnectionRefused));

        Assert.Equal(DownloadErrorKind.Offline, Classify(e, networkAvailable: false));
    }

    [Fact]
    public void FatalError_WithoutNetwork_StaysFatal()
    {
        var e = new HttpRequestException(null, null, HttpStatusCode.NotFound);

        Assert.Equal(DownloadErrorKind.Fatal, Classify(e, networkAvailable: false));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, DownloadErrorKind.Transient)]
    [InlineData(HttpStatusCode.ServiceUnavailable, DownloadErrorKind.Transient)]
    [InlineData(HttpStatusCode.TooManyRequests, DownloadErrorKind.Transient)]
    [InlineData(HttpStatusCode.RequestTimeout, DownloadErrorKind.Transient)]
    [InlineData(HttpStatusCode.NotFound, DownloadErrorKind.Fatal)]
    [InlineData(HttpStatusCode.Forbidden, DownloadErrorKind.Fatal)]
    public void StatusCodes(HttpStatusCode status, DownloadErrorKind expected)
    {
        Assert.Equal(expected, Classify(new HttpRequestException(null, null, status)));
    }

    [Fact]
    public void BareHttpRequestException_IsTransient() =>
        Assert.Equal(DownloadErrorKind.Transient, Classify(new HttpRequestException("connection closed")));

    [Fact]
    public void StallAndTimeout_AreTransient()
    {
        Assert.Equal(DownloadErrorKind.Transient, Classify(new DownloadStalledException(TimeSpan.FromSeconds(30))));
        Assert.Equal(DownloadErrorKind.Transient, Classify(new TaskCanceledException("timeout", new TimeoutException())));
    }

    [Fact]
    public void UnrelatedException_IsFatal() =>
        Assert.Equal(DownloadErrorKind.Fatal, Classify(new InvalidOperationException()));
}
