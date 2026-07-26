using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Starlight.Launcher.WebUI.Components.Atoms.Dialogs;

public partial class ChangelogDialog
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = default!;

    [Parameter] public string Version { get; set; } = "";
    [Parameter] public string Notes { get; set; } = "";

    private void Close() => MudDialog.Close();
}
