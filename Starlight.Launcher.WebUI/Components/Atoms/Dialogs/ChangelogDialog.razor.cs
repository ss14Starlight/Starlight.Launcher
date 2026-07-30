using Microsoft.AspNetCore.Components;
using MudBlazor;
using Starlight.Launcher.WebUI.Models.LauncherUpdater;

namespace Starlight.Launcher.WebUI.Components.Atoms.Dialogs;

public partial class ChangelogDialog
{
    [CascadingParameter] private IMudDialogInstance _mudDialog { get; set; } = default!;

    [Parameter] public IReadOnlyList<ChangelogEntry> Entries { get; set; } = Array.Empty<ChangelogEntry>();

    private void Close() => _mudDialog.Close();
}
