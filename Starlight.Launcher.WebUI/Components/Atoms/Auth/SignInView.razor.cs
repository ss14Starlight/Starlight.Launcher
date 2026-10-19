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
using Starlight.Launcher.WebUI.Models.SteamAuthService;

namespace Starlight.Launcher.WebUI.Components.Atoms.Auth;

public partial class SignInView : LocalizedComponentBase
{
    [Parameter, EditorRequired] public Action<Mode>? OnModeSwitch { get; set; }
    [Parameter] public Guid? RelogUserId { get; set; }
    [Parameter] public string Username { get; set; } = "";

    [Inject] private AuthApi _authApi { get; set; } = default!;
    [Inject] private ISnackbar _snackbar { get; set; } = default!;

    private bool _busy;

    private string _password = "";
    private string _tfaCode = "";
    private bool _tfaRequired;
    private string? _error;
    private bool _showResend;
    private bool _pwd;

    [Inject] private IBridge _bridge { get; set; } = default!;

    private async Task OnSignInKey(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" && !_busy)
            await DoSignIn();
    }

    private async Task DoSignIn()
    {
        _error = null;
        _showResend = false;

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
            var request = RelogUserId.HasValue
                ? new AuthApi.AuthenticateRequest(
                    null, RelogUserId.Value, _password,
                    _tfaRequired ? _tfaCode : null)
                : new AuthApi.AuthenticateRequest(
                    Username, null, _password,
                    _tfaRequired ? _tfaCode : null);
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
                    _error = L["auth-menu-incorrect-info-error"];
                    break;

                case AuthApi.AuthenticateDenyResponseCode.AccountUnconfirmed:
                    _error = L["auth-menu-unconfirmed-info-error"];
                    _showResend = true;
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

    private async Task LoginWithDiscord()
    {
        _busy = true;
        _error = null;
        await InvokeAsync(StateHasChanged);
        try
        {
            _ = await _bridge.LoginAsync(false);
            OnModeSwitch?.Invoke(Mode.AccountList);
        }
        catch (OperationCanceledException)
        {
            _error = L["auth-menu-discord-login-error"];
        }
        catch (DiscordAuthException ex)
        {
            _error = ex.Message;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Discord login failed");
            _error = L["auth-menu-discord-connect-fail"];
        }
        finally
        {
            _busy = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task LoginWithSteam()
    {
        _busy = true;
        _error = null;
        await InvokeAsync(StateHasChanged);
        try
        {
            _ = await _bridge.LoginAsync(true);
            OnModeSwitch?.Invoke(Mode.AccountList);
        }
        catch (OperationCanceledException)
        {
            _error = L["auth-menu-steam-login-error"];
        }
        catch (SteamAuthException ex)
        {
            _error = ex.Message;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Steam login failed");
            _error = L["auth-menu-steam-connect-fail"];
        }
        finally
        {
            _busy = false;
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

        _busy = true;
        await InvokeAsync(StateHasChanged);
        try
        {
            if (Username.Contains('@'))
            {
                var errors = await _authApi.ResendConfirmationAsync(Username, new UrlFallbackSet(authServer));
                _ = errors == null
                    ? _snackbar.Add(L.GetString("auth-menu-email-resent"), Severity.Success)
                    : _snackbar.Add(string.Join("\n", errors), Severity.Error);
            }
            else
            {
                _ = _snackbar.Add(L["auth-menu-email-resent-info"], Severity.Warning);
            }
        }
        finally
        {
            _busy = false;
            await InvokeAsync(StateHasChanged);
        }
    }
}
