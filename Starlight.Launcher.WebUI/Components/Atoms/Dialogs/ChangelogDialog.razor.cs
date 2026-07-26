using Microsoft.AspNetCore.Components;
using MudBlazor;
using Starlight.Launcher.Services.Localization;

namespace Starlight.Launcher.Components.Atoms.Dialogs;

public partial class ChangelogDialog
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = default!;
    [Inject] private LocalizationManager _localization { get; set; } = default!;

    [Parameter] public string Version { get; set; } = "";
    [Parameter] public string Notes { get; set; } = "";

    private void Close() => MudDialog.Close();
}
