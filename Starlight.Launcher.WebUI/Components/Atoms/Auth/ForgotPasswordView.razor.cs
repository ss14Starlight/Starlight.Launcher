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

    private bool _busy;
    private string _email = "";
    private string? _error;
    private bool _success;

    private async Task DoForgotPassword()
    {
        _error = null;

        if ((await _bridge.GetSettingsAsync()).SelectedAuthServer is not { } authServer)
        {
            _error = L["auth-menu-no-server-error"];
            return;
        }

        if (string.IsNullOrWhiteSpace(_email) || !_email.Contains('@'))
        {
            _error = L["auth-menu-forgot-notvalid-email-error"];
            return;
        }

        _busy = true;
        await InvokeAsync(StateHasChanged);
        try
        {
            var errors = await _authApi.ForgotPasswordAsync(_email, new UrlFallbackSet(authServer));
            if (errors == null)
                _success = true;
            else
                _error = string.Join("\n", errors);
        }
        finally
        {
            _busy = false;
            await InvokeAsync(StateHasChanged);
        }
    }
}
