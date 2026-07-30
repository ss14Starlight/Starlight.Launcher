using Microsoft.AspNetCore.Components;
using Robust.Launcher.Api.Api;
using Robust.Launcher.Api.Utility;
using Starlight.Launcher.WebUI.Bridge;
using Starlight.Launcher.WebUI.Components.Pages;
using Starlight.Launcher.WebUI.Localization;

namespace Starlight.Launcher.WebUI.Components.Atoms.Auth;

public partial class ForgotPasswordView : LocalizedComponentBase
{
    [Parameter, EditorRequired] public Action<Mode>? OnModeSwitch { get; set; }

    [Inject] private AuthApi _authApi { get; set; } = default!;
    [Inject] private IBridge _bridge { get; set; } = default!;

    private bool Busy;
    private string Email = "";
    private string? Error;
    private bool Success;

    private async Task DoForgotPassword()
    {
        Error = null;

        if ((await _bridge.GetSettingsAsync()).SelectedAuthServer is not { } authServer)
        {
            Error = L["auth-menu-no-server-error"];
            return;
        }

        if (string.IsNullOrWhiteSpace(Email) || !Email.Contains('@'))
        {
            Error = L["auth-menu-forgot-notvalid-email-error"];
            return;
        }

        Busy = true;
        await InvokeAsync(StateHasChanged);
        try
        {
            var errors = await _authApi.ForgotPasswordAsync(Email, new UrlFallbackSet(authServer));
            if (errors == null)
                Success = true;
            else
                Error = string.Join("\n", errors);
        }
        finally
        {
            Busy = false;
            await InvokeAsync(StateHasChanged);
        }
    }
}
