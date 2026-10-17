using Microsoft.AspNetCore.Components;
using Starlight.Launcher.WebUI.Bridge;
using Starlight.Launcher.WebUI.Localization;
using Starlight.Launcher.WebUI.Models.Journal;

namespace Starlight.Launcher.WebUI.Components.Atoms.Journal;

public partial class ServerNoteEditor : LocalizedComponentBase
{
    [Inject] private IBridge _bridge { get; set; } = default!;

    [Parameter, EditorRequired] public string Address { get; set; } = "";
    [Parameter] public string? ServerName { get; set; }

    private string _note = "";
    private int _rating;
    private string? _loadedFor;

    protected override void OnParametersSet()
    {
        if (_loadedFor == Address)
            return;

        _loadedFor = Address;
        var note = _bridge.GetServerNote(Address);
        _note = note?.Note ?? "";
        _rating = note?.Rating ?? 0;
    }

    private void OnRatingChanged(int rating)
    {
        _rating = rating;
        Save();
    }

    private void OnNoteChanged(string? note)
    {
        _note = note ?? "";
        Save();
    }

    private void Save() => _bridge.SetServerNote(new ServerNote(Address)
    {
        Name = ServerName,
        Note = _note,
        Rating = _rating,
    });
}
