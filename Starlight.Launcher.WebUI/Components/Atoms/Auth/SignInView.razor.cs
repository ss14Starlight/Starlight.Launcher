using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;
using Robust.Launcher.Api.Api;
using Robust.Launcher.Api.Utility;
using Serilog;
using Starlight.Launcher.WebUI.Bridge;
using Starlight.Launcher.WebUI.Components.Pages;
using Starlight.Launcher.WebUI.Localization;
using Starlight.Launcher.WebUI.Models.DiscordAuthService;

namespace Starlight.Launcher.WebUI.Components.Atoms.Auth;

public partial class SignInView : LocalizedComponentBase
{
    [Parameter, EditorRequired] public Action<Mode>? OnModeSwitch { get; set; }
    [Parameter] public Guid? RelogUserId { get; set; }
    [Parameter] public string Username { get; set; } = "";

    [Inject] private AuthApi _authApi { get; set; } = default!;
    [Inject] private ISnackbar _snackbar { get; set; } = default!;

    private bool Busy;

    private string Password = "";
    private string TfaCode = "";
    private bool TfaRequired;
    private string? Error;
    private bool ShowResend;
    private bool Pwd;
    [Inject] private IBridge _bridge { get; set; } = default!;

    private async Task OnSignInKey(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" && !Busy)
            await DoSignIn();
    }

    private async Task DoSignIn()
    {
        Error = null;
        ShowResend = false;

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
            AuthApi.AuthenticateRequest request;
            if (RelogUserId.HasValue)
            {
                request = new AuthApi.AuthenticateRequest(
                    null, RelogUserId.Value, Password,
                    TfaRequired ? TfaCode : null);
            }
            else
            {
                request = new AuthApi.AuthenticateRequest(
                    Username, null, Password,
                    TfaRequired ? TfaCode : null);
            }

            var result = await _authApi.AuthenticateAsync(request, new UrlFallbackSet(authServer));

            if (result.IsSuccess)
            {
                _bridge.AddFreshLogin(result.LoginInfo);
                _bridge.SetActiveAccountId(result.LoginInfo.UserId);
                _ = _snackbar.Add(L.GetString("auth-menu-welcome-message", ("username", result.LoginInfo.Username)), Severity.Success);
                OnModeSwitch?.Invoke(Mode.AccountList);
                return;
            }

            switch (result.Code)
            {
                case AuthApi.AuthenticateDenyResponseCode.InvalidCredentials:
                    Error = L["auth-menu-incorrect-info-error"];
                    break;

                case AuthApi.AuthenticateDenyResponseCode.AccountUnconfirmed:
                    Error = L["auth-menu-unconfirmed-info-error"];
                    ShowResend = true;
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

    private async Task LoginWithDiscord()
    {
        Busy = true;
        Error = null;
        await InvokeAsync(StateHasChanged);
        try
        {
            _ = await _bridge.LoginAsync();
            OnModeSwitch?.Invoke(Mode.AccountList);
        }
        catch (OperationCanceledException)
        {
            Error = L["auth-menu-discord-login-error"];
        }
        catch (DiscordAuthException ex)
        {
            Error = ex.Message;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Discord login failed");
            Error = L["auth-menu-discord-connect-fail"];
        }
        finally
        {
            Busy = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task ResendConfirmation()
    {
        if ((await _bridge.GetSettingsAsync()).SelectedAuthServer is not { } authServer)
        {
            _ = _snackbar.Add(L["auth-menu-no-server-error"], Severity.Error);
            return;
        }

        Busy = true;
        await InvokeAsync(StateHasChanged);
        try
        {
            if (Username.Contains('@'))
            {
                var errors = await _authApi.ResendConfirmationAsync(Username, new UrlFallbackSet(authServer));
                if (errors == null)
                    _ = _snackbar.Add(L.GetString("auth-menu-email-resent"), Severity.Success);
                else
                    _ = _snackbar.Add(string.Join("\n", errors), Severity.Error);
            }
            else
            {
                _ = _snackbar.Add(L["auth-menu-email-resent-info"], Severity.Warning);
            }
        }
        finally
        {
            Busy = false;
            await InvokeAsync(StateHasChanged);
        }
    }
}
