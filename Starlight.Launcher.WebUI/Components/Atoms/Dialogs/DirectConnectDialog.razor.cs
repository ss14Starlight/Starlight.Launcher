using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;
using Starlight.Launcher.WebUI.Localization;

namespace Starlight.Launcher.WebUI.Components.Atoms.Dialogs;

public partial class DirectConnectDialog : LocalizedComponentBase
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = null!;

    private string Address { get; set; } = "";
    private bool AddToFavorites { get; set; }

    private void Submit()
        => MudDialog.Close(DialogResult.Ok(
            new DirectConnectResult(Address.Trim(), AddToFavorites)));

    private void Cancel() => MudDialog.Cancel();

    private void HandleKeyDown(KeyboardEventArgs e)
    {
        if (e.Key is "Enter" or "NumpadEnter" && !string.IsNullOrWhiteSpace(Address))
            Submit();
    }
}

public sealed record DirectConnectResult(string Address, bool AddToFavorites);
