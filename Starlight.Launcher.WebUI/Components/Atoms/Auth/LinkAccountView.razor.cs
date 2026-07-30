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

    private bool Busy;
    private string Password = "";
    private string TfaCode = "";
    private bool TfaRequired;

    private string? Error;

    private async Task OnLinkKey(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" && !Busy)
            await DoLinkAccount();
    }

    private async Task DoLinkAccount()
    {
        Error = null;

        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrEmpty(Password))
        {
            Error = L["auth-menu-enter-info-error"];
            return;
        }

        if ((await _bridge.GetSettingsAsync()).SelectedAuthServer is not { } authServer)
        {
            Error = L["auth-menu-no-server-error"];
            return;
        }

        Busy = true;
        await InvokeAsync(StateHasChanged);
        try
        {
            var request = new AuthApi.AuthenticateRequest(
                Username, null, Password,
                TfaRequired ? TfaCode : null);

            var result = await _authApi.AuthenticateAsync(request, new UrlFallbackSet(authServer));

            if (result.IsSuccess && UserId != null)
            {
                _bridge.LinkAuthToken(UserId.Value, result.LoginInfo.UserId, result.LoginInfo);

                _ = _snackbar.Add(L.GetString("auth-menu-account-linked", ("account", result.LoginInfo.Username)), Severity.Success);

                OnModeSwitch?.Invoke(Mode.AccountList);
                return;
            }

            switch (result.Code)
            {
                case AuthApi.AuthenticateDenyResponseCode.InvalidCredentials:
                    Error = L["auth-menu-incorrect-info-error"];
                    break;
                case AuthApi.AuthenticateDenyResponseCode.TfaRequired:
                    TfaRequired = true;
                    Error = L["auth-menu-tfa-required-error"];
                    break;
                case AuthApi.AuthenticateDenyResponseCode.TfaInvalid:
                    TfaRequired = true;
                    Error = L["auth-menu-tfa-invalid-error"];
                    break;
                case AuthApi.AuthenticateDenyResponseCode.AccountLocked:
                    Error = L["auth-menu-account-blocked-error"];
                    break;
                default:
                    Error = string.Join("\n", result.Errors);
                    break;
            }
        }
        finally
        {
            Busy = false;
            await InvokeAsync(StateHasChanged);
        }
    }
}
