namespace Starlight.Launcher.WebUI.Models.Journal;

public sealed record ServerWatch(string Address)
{
    public string? Name { get; init; }

    public string? HubAddress { get; init; }

    public bool OnLobby { get; init; }

    public bool OnNewRound { get; init; }

    public int? PlayersAtLeast { get; init; }

    public bool OnSlotFree { get; init; }

    public bool AutoConnect { get; init; }

    public bool IsEmpty => !OnLobby && !OnNewRound && PlayersAtLeast is not > 0 && !OnSlotFree && !AutoConnect;
}
