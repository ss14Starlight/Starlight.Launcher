using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using Robust.Launcher.Api.Models;

namespace Robust.Launcher.Api.Utility;

public static class TransientDownloadError
{
    public static bool IsTransient(Exception e)
    {
        for (var cur = e; cur != null; cur = cur.InnerException)
        {
            switch (cur)
            {
                case DownloadStalledException:
                case SocketException:
                case HttpIOException:
                case TimeoutException:
                    return true;
                case HttpRequestException { StatusCode: { } status }:
                    return IsTransientStatus(status);
                case HttpRequestException:
                    return true;
                case IOException when cur.InnerException is SocketException:
                    return true;
            }
        }

        return false;
    }

    private static bool IsTransientStatus(HttpStatusCode status) =>
        status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)status >= 500;
}
