using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;
using Robust.Launcher.Api.Api;
using Robust.Launcher.Api.Utility;
using Starlight.Launcher.WebUI.Bridge;
using Starlight.Launcher.WebUI.Components.Pages;
using Starlight.Launcher.WebUI.Localization;

namespace Starlight.Launcher.WebUI.Components.Atoms.Auth;

public partial class LinkAccountView : LocalizedComponentBase
{
    [Parameter, EditorRequired] public Action<Mode>? OnModeSwitch { get; set; }
    [Parameter, EditorRequired] public string Username { get; set; }
    [Parameter, EditorRequired] public Guid? UserId { get; set; }

    [Inject] private IBridge _bridge { get; set; } = default!;
    [Inject] private AuthApi _authApi { get; set; } = default!;
    [Inject] private ISnackbar _snackbar { get; set; } = default!;

    private bool _busy;
    private string _password = "";
    private string _tfaCode = "";
    private bool _tfaRequired;

    private string? _error;

    private async Task OnLinkKey(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" && !_busy)
            await DoLinkAccount();
    }

    private async Task DoLinkAccount()
    {
        _error = null;

        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrEmpty(_password))
        {
            _error = L["auth-menu-enter-info-error"];
            return;
        }

        if ((await _bridge.GetSettingsAsync()).SelectedAuthServer is not { } authServer)
        {
            _error = L["auth-menu-no-server-error"];
            return;
        }

        _busy = true;
        await InvokeAsync(StateHasChanged);
        try
        {
            var request = new AuthApi.AuthenticateRequest(
                Username, null, _password,
                _tfaRequired ? _tfaCode : null);

            var result = await _authApi.AuthenticateAsync(request, new UrlFallbackSet(authServer));

            if (result.IsSuccess && UserId != null)
            {
                if (await _bridge.LinkSs14AccountAsync(UserId.Value, result.LoginInfo) is { } linkError)
                {
                    _error = linkError;
                    return;
                }

                _ = _snackbar.Add(L.GetString("auth-menu-account-linked", ("account", result.LoginInfo.Username)), Severity.Success);

                OnModeSwitch?.Invoke(Mode.AccountList);
                return;
            }

            switch (result.Code)
            {
                case AuthApi.AuthenticateDenyResponseCode.InvalidCredentials:
                    _error = L["auth-menu-incorrect-info-error"];
                    break;
                case AuthApi.AuthenticateDenyResponseCode.TfaRequired:
                    _tfaRequired = true;
                    _error = L["auth-menu-tfa-required-error"];
                    break;
                case AuthApi.AuthenticateDenyResponseCode.TfaInvalid:
                    _tfaRequired = true;
                    _error = L["auth-menu-tfa-invalid-error"];
                    break;
                case AuthApi.AuthenticateDenyResponseCode.AccountLocked:
                    _error = L["auth-menu-account-blocked-error"];
                    break;
                default:
                    _error = string.Join("\n", result.Errors);
                    break;
            }
        }
        finally
        {
            _busy = false;
            await InvokeAsync(StateHasChanged);
        }
    }
}
