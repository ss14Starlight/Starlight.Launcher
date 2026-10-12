using System;

namespace Robust.Launcher.Api.Models;

public class UpdateException : Exception
{
    public UpdateException(string message) : base(message)
    {
    }
}

public sealed class NoEngineForPlatformException(string message) : UpdateException(message);
public sealed class NoModuleForPlatformException(string message) : UpdateException(message);

/// <summary>
/// The download server stopped sending data mid-transfer.
/// </summary>
public sealed class DownloadStalledException(TimeSpan timeout)
    : UpdateException($"Download stalled: no data received for {timeout.TotalSeconds:0} seconds.");