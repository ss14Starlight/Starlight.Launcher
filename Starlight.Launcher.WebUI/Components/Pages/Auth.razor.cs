using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;
using Robust.Launcher.Api.Api;
using Robust.Launcher.Api.Models;
using Robust.Launcher.Api.Utility;
using Serilog;
using Starlight.Launcher.Api.Models;
using Starlight.Launcher.Services.Auth;
using Starlight.Launcher.Services.Localization;
using Starlight.Launcher.Services.Settings;

namespace Starlight.Launcher.Components.WebUI.Pages;

public partial class Auth : ComponentBase, IDisposable
{
    [Inject] private LoginManager _loginManager { get; set; } = default!;
    [Inject] private SettingsService _settings { get; set; } = default!;
    [Inject] private AuthApi _authApi { get; set; } = default!;
    [Inject] private NavigationManager _nav { get; set; } = default!;
    [Inject] private DiscordAuthService _discordAuth { get; set; } = default!;
    [Inject] private ISnackbar _snackbar { get; set; } = default!;
    [Inject] private LocalizationManager _localization { get; set; } = default!;

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

    private string _registerUsername = "";
    private string _registerEmail = "";
    private string _registerPassword = "";
    private string _registerPasswordConfirm = "";
    private string[]? _registerErrors;
    private string? _registerSuccessMessage;

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
        _loginManager.LoginsChanged += OnLoginsChanged;

        if (_loginManager.Logins.Count == 0)
            _mode = Mode.SignIn;
    }

    private void OnLoginsChanged() => InvokeAsync(StateHasChanged);

    public void Dispose() => _loginManager.LoginsChanged -= OnLoginsChanged;

    private async Task SelectAccount(LoggedInAccount account)
    {
        _busy = true;
        try
        {
            try
            {
                await _loginManager.UpdateSingleAccountStatus(account);
            }
            catch (AuthApiException ex)
            {
                _snackbar.Add(_localization.GetString("auth-menu-token-verify-warning", ("ex", ex.Message)), Severity.Warning);
            }

            if (account.Status == AccountLoginStatus.Expired)
            {
                _snackbar.Add(_localization["auth-menu-session-expired-warning"], Severity.Warning);
                await BeginRelogin(account);
                return;
            }

            _loginManager.ActiveAccountId = account.UserId;
            _nav.NavigateTo("/");
        }
        finally
        {
            _busy = false;
        }
    }

    private void RemoveAccount(LoggedInAccount account)
    {
        _loginManager.RemoveLogin(account.UserId);
        _snackbar.Add(_localization.GetString("auth-menu-account-deleted", ("account", account.LoginInfo.Username)), Severity.Info);
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
        AccountLoginStatus.Available => _localization["auth-menu-online-status"],
        AccountLoginStatus.Expired => _localization["auth-menu-expired-status"],
        AccountLoginStatus.Unsure => _localization["auth-menu-unsure-status"],
        _ => s.ToString()
    };

    private MudBlazor.Color StatusColor(AccountLoginStatus s) => s switch
    {
        AccountLoginStatus.Available => MudBlazor.Color.Success,
        AccountLoginStatus.Expired => MudBlazor.Color.Warning,
        AccountLoginStatus.Unsure => MudBlazor.Color.Surface,
        _ => MudBlazor.Color.Default
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
            _linkError = _localization["auth-menu-enter-info-error"];
            return;
        }

        if ((await _settings.GetSettingsAsync()).SelectedAuthServer is not { } authServer)
        {
            _linkError = _localization["auth-menu-no-server-error"];
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
                _loginManager.LinkAuthToken(_linkUserId.Value, result.LoginInfo.UserId, result.LoginInfo);

                _snackbar.Add(_localization.GetString("auth-menu-account-linked", ("account", result.LoginInfo.Username)), Severity.Success);

                BackToAccountList();
                return;
            }

            switch (result.Code)
            {
                case AuthApi.AuthenticateDenyResponseCode.InvalidCredentials:
                    _linkError = _localization["auth-menu-incorrect-info-error"];
                    break;
                case AuthApi.AuthenticateDenyResponseCode.TfaRequired:
                    _linkTfaRequired = true;
                    _linkError = _localization["auth-menu-tfa-required-error"];
                    break;
                case AuthApi.AuthenticateDenyResponseCode.TfaInvalid:
                    _linkTfaRequired = true;
                    _linkError = _localization["auth-menu-tfa-invalid-error"];
                    break;
                case AuthApi.AuthenticateDenyResponseCode.AccountLocked:
                    _linkError = _localization["auth-menu-account-blocked-error"];
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
        RunDiscordAttach(account, _localization.GetString("auth-menu-linked-status", ("account", account.LoginInfo.Username)));

    private Task ReloginDiscord(LoggedInAccount account) =>
        RunDiscordAttach(account, _localization["auth-menu-discord-renewed"], navigateHome: true);

    private async Task RunDiscordAttach(LoggedInAccount account, string success, bool navigateHome = false)
    {
        _busy = true;
        await InvokeAsync(StateHasChanged);
        try
        {
            await _discordAuth.AttachToAccountAsync(account);
            _snackbar.Add(success, Severity.Success);
            if (navigateHome)
            {
                _loginManager.ActiveAccountId = account.UserId;
                _nav.NavigateTo("/");
            }
        }
        catch (OperationCanceledException)
        {
            _snackbar.Add(_localization["auth-menu-discord-login-error"], Severity.Warning);
        }
        catch (DiscordAuthException ex)
        {
            _snackbar.Add(ex.Message, Severity.Error);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Discord attach failed");
            _snackbar.Add(_localization["auth-menu-discord-connect-fail"], Severity.Error);
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
            await _discordAuth.LoginAsync();
            _nav.NavigateTo("/");
        }
        catch (OperationCanceledException)
        {
            _signInError = _localization["auth-menu-discord-login-error"];
        }
        catch (DiscordAuthException ex)
        {
            _signInError = ex.Message;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Discord login failed");
            _signInError = _localization["auth-menu-discord-connect-fail"];
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
            _signInError = _localization["auth-menu-enter-info-error"];
            return;
        }

        if ((await _settings.GetSettingsAsync()).SelectedAuthServer is not { } authServer)
        {
            _signInError = _localization["auth-menu-no-server-error"];
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
                _loginManager.AddFreshLogin(result.LoginInfo);
                _loginManager.ActiveAccountId = result.LoginInfo.UserId;
                _snackbar.Add(_localization.GetString("auth-menu-welcome-message", ("username", result.LoginInfo.Username)), Severity.Success);
                _nav.NavigateTo("/");
                return;
            }

            switch (result.Code)
            {
                case AuthApi.AuthenticateDenyResponseCode.InvalidCredentials:
                    _signInError = _localization["auth-menu-incorrect-info-error"];
                    break;

                case AuthApi.AuthenticateDenyResponseCode.AccountUnconfirmed:
                    _signInError = _localization["auth-menu-unconfirmed-info-error"];
                    _signInShowResend = true;
                    break;

                case AuthApi.AuthenticateDenyResponseCode.TfaRequired:
                    _signInTfaRequired = true;
                    _signInError = _localization["auth-menu-tfa-required-error"];
                    break;

                case AuthApi.AuthenticateDenyResponseCode.TfaInvalid:
                    _signInTfaRequired = true;
                    _signInError = _localization["auth-menu-tfa-invalid-error"];
                    break;

                case AuthApi.AuthenticateDenyResponseCode.AccountLocked:
                    _signInError = _localization["auth-menu-account-blocked-error"];
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
        if ((await _settings.GetSettingsAsync()).SelectedAuthServer is not { } authServer)
        {
            _snackbar.Add(_localization["auth-menu-no-server-error"], Severity.Error);
            return;
        }

        _busy = true;
        try
        {
            if (_signInUsername.Contains('@'))
            {
                var errors = await _authApi.ResendConfirmationAsync(_signInUsername, new UrlFallbackSet(authServer));
                if (errors == null)
                    _snackbar.Add(_localization.GetString("auth-menu-email-resent"), Severity.Success);
                else
                    _snackbar.Add(string.Join("\n", errors), Severity.Error);
            }
            else
            {
                _snackbar.Add(_localization["auth-menu-email-resent-info"], Severity.Warning);
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

    private async Task DoRegister()
    {
        if (!RegistrationEnabled)
            return;

        _registerErrors = null;
        _registerSuccessMessage = null;

        var authServer = (await _settings.GetSettingsAsync()).SelectedAuthServer;

        var validationErrors = new List<string>();
        if (authServer == null)
            validationErrors.Add(_localization["auth-menu-no-server-error"]);
        if (string.IsNullOrWhiteSpace(_registerUsername))
            validationErrors.Add(_localization["auth-menu-register-username-missing"]);
        if (string.IsNullOrWhiteSpace(_registerEmail) || !_registerEmail.Contains('@'))
            validationErrors.Add(_localization["auth-menu-register-invalid-email"]);
        if (_registerPassword.Length < 8)
            validationErrors.Add(_localization["auth-menu-register-too-short-pass"]);
        if (_registerPassword != _registerPasswordConfirm)
            validationErrors.Add(_localization["auth-menu-register-dont-match-pass"]);

        if (validationErrors.Count > 0)
        {
            _registerErrors = validationErrors.ToArray();
            return;
        }

        _busy = true;
        try
        {
            var result = await _authApi.RegisterAsync(_registerUsername, _registerEmail, _registerPassword, new UrlFallbackSet(authServer!));

            if (!result.IsSuccess)
            {
                _registerErrors = result.Errors;
                return;
            }

            _registerSuccessMessage = result.Status switch
            {
                RegisterResponseStatus.Registered =>
                    _localization["auth-menu-register-success"],
                RegisterResponseStatus.RegisteredNeedConfirmation => _localization.GetString("auth-menu-register-required-confirmation", ("email", _registerEmail)),
                _ => _localization["auth-menu-register-success"]
            };

            _registerPassword = "";
            _registerPasswordConfirm = "";
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task DoForgotPassword()
    {
        _forgotError = null;

        if ((await _settings.GetSettingsAsync()).SelectedAuthServer is not { } authServer)
        {
            _forgotError = _localization["auth-menu-no-server-error"];
            return;
        }

        if (string.IsNullOrWhiteSpace(_forgotEmail) || !_forgotEmail.Contains('@'))
        {
            _forgotError = _localization["auth-menu-forgot-notvalid-email-error"];
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
        if (mode == Mode.Register) { _registerErrors = null; _registerSuccessMessage = null; }
        if (mode == Mode.ForgotPassword) { _forgotError = null; _forgotSuccess = false; }

        _mode = mode;
    }
}
