using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;
using Starlight.Launcher.WebUI.Localization;

namespace Starlight.Launcher.WebUI.Components.Atoms.Dialogs;

public partial class DirectConnectDialog : LocalizedComponentBase
{
    [CascadingParameter] private IMudDialogInstance _mudDialog { get; set; } = null!;

    private string _address { get; set; } = "";
    private bool _addToFavorites { get; set; }

    private void Submit()
        => _mudDialog.Close(DialogResult.Ok(
            new DirectConnectResult(_address.Trim(), _addToFavorites)));

    private void Cancel() => _mudDialog.Cancel();

    private void HandleKeyDown(KeyboardEventArgs e)
    {
        if (e.Key is "Enter" or "NumpadEnter" && !string.IsNullOrWhiteSpace(_address))
            Submit();
    }
}

public sealed record DirectConnectResult(string Address, bool AddToFavorites);
