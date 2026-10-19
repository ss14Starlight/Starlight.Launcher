using Microsoft.AspNetCore.Components;
using MudBlazor;
using Starlight.Launcher.WebUI.Bridge;
using Starlight.Launcher.WebUI.Localization;
using Starlight.Launcher.WebUI.Models.Journal;

namespace Starlight.Launcher.WebUI.Components.Atoms.Dialogs;

public partial class ServerWatchDialog : LocalizedComponentBase
{
    [CascadingParameter] private IMudDialogInstance _mudDialog { get; set; } = null!;
    [Inject] private IBridge _bridge { get; set; } = default!;

    [Parameter, EditorRequired] public string Address { get; set; } = "";
    [Parameter] public string? ServerName { get; set; }
    [Parameter] public string? HubAddress { get; set; }

    [Parameter] public int SoftMaxPlayers { get; set; }

    private ServerWatch? _existing { get; set; }

    private bool _onLobby;
    private bool _onNewRound;
    private bool _onSlotFree;
    private bool _playersEnabled;
    private int _playersThreshold = 30;
    private bool _autoConnect;

    protected override void OnInitialized()
    {
        base.OnInitialized();

        _existing = _bridge.GetServerWatch(Address);
        if (_existing is { } w)
        {
            _onLobby = w.OnLobby;
            _onNewRound = w.OnNewRound;
            _onSlotFree = w.OnSlotFree;
            _playersEnabled = w.PlayersAtLeast is > 0;
            _playersThreshold = w.PlayersAtLeast is > 0 and var t ? t : _playersThreshold;
            _autoConnect = w.AutoConnect;
        }
        else if (SoftMaxPlayers > 0)
        {
            _playersThreshold = Math.Max(1, SoftMaxPlayers / 2);
        }
    }

    private void Save()
    {
        _bridge.SetServerWatch(new ServerWatch(Address)
        {
            Name = ServerName,
            HubAddress = HubAddress ?? _existing?.HubAddress,
            OnLobby = _onLobby,
            OnNewRound = _onNewRound,
            OnSlotFree = _onSlotFree,
            PlayersAtLeast = _playersEnabled ? Math.Max(1, _playersThreshold) : null,
            AutoConnect = _autoConnect,
        });
        _mudDialog.Close(DialogResult.Ok(true));
    }

    private void Clear()
    {
        _bridge.SetServerWatch(new ServerWatch(Address));
        _mudDialog.Close(DialogResult.Ok(false));
    }

    private void Cancel() => _mudDialog.Cancel();
}
