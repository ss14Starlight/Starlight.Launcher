using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;
using Robust.Launcher.Api.Api;
using Robust.Launcher.Api.Models;
using Robust.Launcher.Api.Utility;
using Serilog;
using Starlight.Launcher.WebUI.Bridge;
using Starlight.Launcher.WebUI.Localization;
using Starlight.Launcher.WebUI.Models.Auth;
using Starlight.Launcher.WebUI.Models.DiscordAuthService;

namespace Starlight.Launcher.WebUI.Components.Pages;

public partial class Auth : LocalizedComponentBase, IDisposable
{
    [Inject] private IBridge _bridge { get; set; } = default!;
    [Inject] private AuthApi _authApi { get; set; } = default!;
    [Inject] private NavigationManager _nav { get; set; } = default!;
    [Inject] private ISnackbar _snackbar { get; set; } = default!;

    private enum Mode
    {
        AccountList,
        SignIn,
        Register,
        ForgotPassword,
        LinkAccount
    }

    private Mode _mode = Mode.AccountList;
    private bool _busy;

    private string _signInUsername = "";
    private string _signInPassword = "";
    private string _signInTfaCode = "";
    private bool _signInTfaRequired;
    private string? _signInError;
    private bool _signInShowResend;
    private bool _showPwd;

    //private string _registerUsername = "";
    //private string _registerEmail = "";
    //private string _registerPassword = "";
    //private string _registerPasswordConfirm = "";
    //private string[]? _registerErrors;
    //private string? _registerSuccessMessage;

    private string _forgotEmail = "";
    private string? _forgotError;
    private bool _forgotSuccess;

    private Guid? _linkUserId;
    private string _linkUsername = "";
    private string _linkPassword = "";
    private string _linkTfaCode = "";
    private bool _linkTfaRequired;
    private string? _linkError;

    private Guid? _relogUserId;

    private const bool RegistrationEnabled = false;

    protected override void OnInitialized()
    {
        _bridge.LoginEntriesChanged += OnLoginsChanged;

        if (_bridge.GetLoginEntries().Count == 0)
            _mode = Mode.SignIn;
    }

    private void OnLoginsChanged() => InvokeAsync(StateHasChanged);

    public override void Dispose()
    {
        base.Dispose();
        _bridge.LoginEntriesChanged -= OnLoginsChanged;
    }

    private async Task SelectAccount(LoggedInAccount account)
    {
        _busy = true;
        try
        {
            try
            {
                await _bridge.UpdateSingleAccountStatus(account);
            }
            catch (AuthApiException ex)
            {
                _ = _snackbar.Add(L.GetString("auth-menu-token-verify-warning", ("ex", ex.Message)), Severity.Warning);
            }

            if (account.Status == AccountLoginStatus.Expired)
            {
                _ = _snackbar.Add(L["auth-menu-session-expired-warning"], Severity.Warning);
                await BeginRelogin(account);
                return;
            }

            _bridge.SetActiveAccountId(account.UserId);
            _nav.NavigateTo("/");
        }
        finally
        {
            _busy = false;
        }
    }

    private void RemoveAccount(LoggedInAccount account)
    {
        _bridge.RemoveLogin(account.UserId);
        _ = _snackbar.Add(L.GetString("auth-menu-account-deleted", ("account", account.LoginInfo.Username)), Severity.Info);
    }

    private void GoToSignIn()
    {
        ResetSignInForm();
        _mode = Mode.SignIn;
    }

    private async Task BeginRelogin(LoggedInAccount account)
    {
        if (account.LoginInfo.DiscordToken != null && account.LoginInfo.Token == null)
        {
            await ReloginDiscord(account);
            return;
        }

        ResetSignInForm();
        _relogUserId = account.UserId;
        _signInUsername = account.LoginInfo.Username;
        _mode = Mode.SignIn;
    }

    private string StatusLabel(AccountLoginStatus s) => s switch
    {
        AccountLoginStatus.Available => L["auth-menu-online-status"],
        AccountLoginStatus.Expired => L["auth-menu-expired-status"],
        AccountLoginStatus.Unsure => L["auth-menu-unsure-status"],
        _ => s.ToString()
    };

    private void LinkAccount(LoggedInAccount account)
    {
        _linkUserId = account.UserId;
        _linkUsername = account.LoginInfo.Username;
        _linkPassword = "";
        _linkTfaCode = "";
        _linkTfaRequired = false;
        _linkError = null;
        _mode = Mode.LinkAccount;
    }

    private async Task OnLinkKey(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" && !_busy)
            await DoLinkAccount();
    }

    private async Task DoLinkAccount()
    {
        _linkError = null;

        if (string.IsNullOrWhiteSpace(_linkUsername) || string.IsNullOrEmpty(_linkPassword))
        {
            _linkError = L["auth-menu-enter-info-error"];
            return;
        }

        if ((await _bridge.GetSettingsAsync()).SelectedAuthServer is not { } authServer)
        {
            _linkError = L["auth-menu-no-server-error"];
            return;
        }

        _busy = true;
        try
        {
            var request = new AuthApi.AuthenticateRequest(
                _linkUsername, null, _linkPassword,
                _linkTfaRequired ? _linkTfaCode : null);

            var result = await _authApi.AuthenticateAsync(request, new UrlFallbackSet(authServer));

            if (result.IsSuccess && _linkUserId != null)
            {
                _bridge.LinkAuthToken(_linkUserId.Value, result.LoginInfo.UserId, result.LoginInfo);

                _ = _snackbar.Add(L.GetString("auth-menu-account-linked", ("account", result.LoginInfo.Username)), Severity.Success);

                BackToAccountList();
                return;
            }

            switch (result.Code)
            {
                case AuthApi.AuthenticateDenyResponseCode.InvalidCredentials:
                    _linkError = L["auth-menu-incorrect-info-error"];
                    break;
                case AuthApi.AuthenticateDenyResponseCode.TfaRequired:
                    _linkTfaRequired = true;
                    _linkError = L["auth-menu-tfa-required-error"];
                    break;
                case AuthApi.AuthenticateDenyResponseCode.TfaInvalid:
                    _linkTfaRequired = true;
                    _linkError = L["auth-menu-tfa-invalid-error"];
                    break;
                case AuthApi.AuthenticateDenyResponseCode.AccountLocked:
                    _linkError = L["auth-menu-account-blocked-error"];
                    break;
                default:
                    _linkError = string.Join("\n", result.Errors);
                    break;
            }
        }
        finally
        {
            _busy = false;
        }
    }

    private Task LinkDiscord(LoggedInAccount account) =>
        RunDiscordAttach(account, L.GetString("auth-menu-linked-status", ("account", account.LoginInfo.Username)));

    private Task ReloginDiscord(LoggedInAccount account) =>
        RunDiscordAttach(account, L["auth-menu-discord-renewed"], navigateHome: true);

    private async Task RunDiscordAttach(LoggedInAccount account, string success, bool navigateHome = false)
    {
        _busy = true;
        await InvokeAsync(StateHasChanged);
        try
        {
            await _bridge.AttachToAccountAsync(account);
            _ = _snackbar.Add(success, Severity.Success);
            if (navigateHome)
            {
                _bridge.SetActiveAccountId(account.UserId);
                _nav.NavigateTo("/");
            }
        }
        catch (OperationCanceledException)
        {
            _ = _snackbar.Add(L["auth-menu-discord-login-error"], Severity.Warning);
        }
        catch (DiscordAuthException ex)
        {
            _ = _snackbar.Add(ex.Message, Severity.Error);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Discord attach failed");
            _ = _snackbar.Add(L["auth-menu-discord-connect-fail"], Severity.Error);
        }
        finally
        {
            _busy = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task OnSignInKey(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" && !_busy)
            await DoSignIn();
    }

    private async Task LoginWithDiscord()
    {
        _busy = true;
        _signInError = null;
        await InvokeAsync(StateHasChanged);
        try
        {
            _ = await _bridge.LoginAsync();
            _nav.NavigateTo("/");
        }
        catch (OperationCanceledException)
        {
            _signInError = L["auth-menu-discord-login-error"];
        }
        catch (DiscordAuthException ex)
        {
            _signInError = ex.Message;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Discord login failed");
            _signInError = L["auth-menu-discord-connect-fail"];
        }
        finally
        {
            _busy = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task DoSignIn()
    {
        _signInError = null;
        _signInShowResend = false;

        if (string.IsNullOrWhiteSpace(_signInUsername) || string.IsNullOrEmpty(_signInPassword))
        {
            _signInError = L["auth-menu-enter-info-error"];
            return;
        }

        if ((await _bridge.GetSettingsAsync()).SelectedAuthServer is not { } authServer)
        {
            _signInError = L["auth-menu-no-server-error"];
            return;
        }

        _busy = true;
        try
        {
            AuthApi.AuthenticateRequest request;
            if (_relogUserId.HasValue)
            {
                request = new AuthApi.AuthenticateRequest(
                    null, _relogUserId.Value, _signInPassword,
                    _signInTfaRequired ? _signInTfaCode : null);
            }
            else
            {
                request = new AuthApi.AuthenticateRequest(
                    _signInUsername, null, _signInPassword,
                    _signInTfaRequired ? _signInTfaCode : null);
            }

            var result = await _authApi.AuthenticateAsync(request, new UrlFallbackSet(authServer));

            if (result.IsSuccess)
            {
                _bridge.AddFreshLogin(result.LoginInfo);
                _bridge.SetActiveAccountId(result.LoginInfo.UserId);
                _ = _snackbar.Add(L.GetString("auth-menu-welcome-message", ("username", result.LoginInfo.Username)), Severity.Success);
                _nav.NavigateTo("/");
                return;
            }

            switch (result.Code)
            {
                case AuthApi.AuthenticateDenyResponseCode.InvalidCredentials:
                    _signInError = L["auth-menu-incorrect-info-error"];
                    break;

                case AuthApi.AuthenticateDenyResponseCode.AccountUnconfirmed:
                    _signInError = L["auth-menu-unconfirmed-info-error"];
                    _signInShowResend = true;
                    break;

                case AuthApi.AuthenticateDenyResponseCode.TfaRequired:
                    _signInTfaRequired = true;
                    _signInError = L["auth-menu-tfa-required-error"];
                    break;

                case AuthApi.AuthenticateDenyResponseCode.TfaInvalid:
                    _signInTfaRequired = true;
                    _signInError = L["auth-menu-tfa-invalid-error"];
                    break;

                case AuthApi.AuthenticateDenyResponseCode.AccountLocked:
                    _signInError = L["auth-menu-account-blocked-error"];
                    break;

                default:
                    _signInError = string.Join("\n", result.Errors);
                    break;
            }
        }
        finally
        {
            _busy = false;
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
        try
        {
            if (_signInUsername.Contains('@'))
            {
                var errors = await _authApi.ResendConfirmationAsync(_signInUsername, new UrlFallbackSet(authServer));
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
            _busy = false;
        }
    }

    private void ResetSignInForm()
    {
        _signInUsername = "";
        _signInPassword = "";
        _signInTfaCode = "";
        _signInTfaRequired = false;
        _signInError = null;
        _signInShowResend = false;
        _relogUserId = null;
    }

    private void BackToAccountList()
    {
        ResetSignInForm();
        _mode = Mode.AccountList;
    }

    private async Task DoForgotPassword()
    {
        _forgotError = null;

        if ((await _bridge.GetSettingsAsync()).SelectedAuthServer is not { } authServer)
        {
            _forgotError = L["auth-menu-no-server-error"];
            return;
        }

        if (string.IsNullOrWhiteSpace(_forgotEmail) || !_forgotEmail.Contains('@'))
        {
            _forgotError = L["auth-menu-forgot-notvalid-email-error"];
            return;
        }

        _busy = true;
        try
        {
            var errors = await _authApi.ForgotPasswordAsync(_forgotEmail, new UrlFallbackSet(authServer));
            if (errors == null)
            {
                _forgotSuccess = true;
            }
            else
            {
                _forgotError = string.Join("\n", errors);
            }
        }
        finally
        {
            _busy = false;
        }
    }

    private static string StatusCssVar(AccountLoginStatus s) => s switch
    {
        AccountLoginStatus.Available => "success",
        AccountLoginStatus.Expired => "warning",
        AccountLoginStatus.Unsure => "info",
        _ => "surface"
    };

    private void SwitchMode(Mode mode)
    {
        if (mode == Mode.SignIn) ResetSignInForm();
        //if (mode == Mode.Register) { _registerErrors = null; _registerSuccessMessage = null; }
        if (mode == Mode.ForgotPassword) { _forgotError = null; _forgotSuccess = false; }

        _mode = mode;
    }
}
