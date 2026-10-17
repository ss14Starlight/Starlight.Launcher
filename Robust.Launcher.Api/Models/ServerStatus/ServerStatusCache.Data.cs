using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Threading;

namespace Robust.Launcher.Api.Models.ServerStatus;

public sealed class ServerStatusData : ObservableObject, IServerStatusData
{
    public readonly object InfoLock = new();

    public event Action? Changed;
    public void NotifyChanged() => Changed?.Invoke();

    public ServerStatusData(string address) => Address = address;

    public ServerStatusData(string address, string hubAddress)
    {
        Address = address;
        HubAddress = hubAddress;
    }

    public string Address { get; }
    public string? HubAddress { get; }

    public string? Name
    {
        get;
        set => SetProperty(ref field, value);
    }

    public string? Description
    {
        get;
        set => SetProperty(ref field, value);
    }

    public TimeSpan? Ping
    {
        get;
        set => SetProperty(ref field, value);
    }

    public ServerStatusCode Status
    {
        get;
        set => SetProperty(ref field, value);
    } = ServerStatusCode.FetchingStatus;

    public ServerStatusInfoCode StatusInfo
    {
        get;
        set => SetProperty(ref field, value);
    } = ServerStatusInfoCode.NotFetched;

    public int PlayerCount
    {
        get;
        set => SetProperty(ref field, value);
    }

    /// <summary>
    /// 0 means there's no maximum.
    /// </summary>
    public int SoftMaxPlayerCount
    {
        get;
        set => SetProperty(ref field, value);
    }

    public DateTime? RoundStartTime
    {
        get;
        set => SetProperty(ref field, value);
    }

    public GameRoundStatus RoundStatus
    {
        get;
        set => SetProperty(ref field, value);
    }

    public string? Map
    {
        get;
        set => SetProperty(ref field, value);
    }

    public string? Preset
    {
        get;
        set => SetProperty(ref field, value);
    }

    public int? RoundId
    {
        get;
        set => SetProperty(ref field, value);
    }

    public bool PanicBunker
    {
        get;
        set => SetProperty(ref field, value);
    }

    public ServerInfoLink[]? Links
    {
        get;
        set => SetProperty(ref field, value);
    }

    public string[] Tags
    {
        get;
        set => SetProperty(ref field, value);
    } = [];

    public CancellationTokenSource? InfoCancel;
}
