using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Authentication;
using Robust.Launcher.Api.Models;

namespace Robust.Launcher.Api.Utility;

public enum DownloadErrorKind
{
    Fatal,

    Transient,

    Offline,
}

public static class TransientDownloadError
{
    public static DownloadErrorKind Classify(Exception e) => Classify(e, IsNetworkAvailable());

    public static DownloadErrorKind Classify(Exception e, bool networkAvailable)
    {
        for (var cur = e; cur != null; cur = cur.InnerException)
        {
            if (cur is AuthenticationException)
                return DownloadErrorKind.Fatal;
        }

        var kind = ClassifyChain(e);
        return kind == DownloadErrorKind.Transient && !networkAvailable ? DownloadErrorKind.Offline : kind;
    }

    private static DownloadErrorKind ClassifyChain(Exception e)
    {
        for (var cur = e; cur != null; cur = cur.InnerException)
        {
            switch (cur)
            {
                case HttpRequestException { HttpRequestError: HttpRequestError.SecureConnectionError }:
                    return DownloadErrorKind.Fatal;
                case HttpRequestException { HttpRequestError: HttpRequestError.NameResolutionError }:
                    return DownloadErrorKind.Offline;
                case SocketException socket:
                    return IsOfflineSocketError(socket.SocketErrorCode)
                        ? DownloadErrorKind.Offline
                        : DownloadErrorKind.Transient;
                case DownloadStalledException:
                case HttpIOException:
                case TimeoutException:
                    return DownloadErrorKind.Transient;
                case HttpRequestException { StatusCode: { } status }:
                    return IsTransientStatus(status) ? DownloadErrorKind.Transient : DownloadErrorKind.Fatal;
                case HttpRequestException:
                    var inner = cur.InnerException is null ? DownloadErrorKind.Fatal : ClassifyChain(cur.InnerException);
                    return inner == DownloadErrorKind.Fatal ? DownloadErrorKind.Transient : inner;
                case IOException when cur.InnerException is SocketException:
                    continue;
            }
        }

        return DownloadErrorKind.Fatal;
    }

    private static bool IsOfflineSocketError(SocketError error)
        => error
        is SocketError.HostNotFound
        or SocketError.TryAgain
        or SocketError.NoData
        or SocketError.NetworkDown
        or SocketError.NetworkUnreachable;

    private static bool IsTransientStatus(HttpStatusCode status)
        => status
        is HttpStatusCode.RequestTimeout
        or HttpStatusCode.TooManyRequests
            || (int)status >= 500;

    private static bool IsNetworkAvailable()
    {
        try
        {
            return NetworkInterface.GetIsNetworkAvailable();
        }
        catch
        {
            return true;
        }
    }
}
