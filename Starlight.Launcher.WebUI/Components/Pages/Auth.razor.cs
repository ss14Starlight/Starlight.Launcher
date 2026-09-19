using Microsoft.AspNetCore.Components;
using MudBlazor;
using Robust.Launcher.Api.Models;
using Serilog;
using Starlight.Launcher.WebUI.Bridge;
using Starlight.Launcher.WebUI.Localization;
using Starlight.Launcher.WebUI.Models.Auth;
using Starlight.Launcher.WebUI.Models.DiscordAuthService;
using Starlight.Launcher.WebUI.Models.StarlightAuthService;

namespace Starlight.Launcher.WebUI.Components.Pages;

public partial class Auth : LocalizedComponentBase, IDisposable
{
    [Inject] private IBridge _bridge { get; set; } = default!;
    [Inject] private NavigationManager _nav { get; set; } = default!;
    [Inject] private ISnackbar _snackbar { get; set; } = default!;

    private Mode _mode = Mode.AccountList;
    private bool _busy;

    private Guid? _linkUserId;
    private string _linkUsername = "";

    private string _signInUsername = "";
    private Guid? _relogUserId;

    private Guid? _viewedUserId;

    private LoggedInAccount? ViewedAccount
    {
        get
        {
            var logins = _bridge.GetLoginEntries();
            return logins.FirstOrDefault(x => x.UserId == _viewedUserId)
                ?? logins.FirstOrDefault(x => x.UserId == _bridge.GetActiveAccountId())
                ?? logins.FirstOrDefault();
        }
    }

    protected override void OnInitialized()
    {
        _bridge.LoginEntriesChanged += OnLoginsChanged;

        if (_bridge.GetLoginEntries().Count == 0)
        {
            _mode = Mode.SignIn;
            StateHasChanged();
        }
    }

    private void OnLoginsChanged() => InvokeAsync(StateHasChanged);

    public override void Dispose()
    {
        base.Dispose();
        _bridge.LoginEntriesChanged -= OnLoginsChanged;
    }

    private void ViewAccount(LoggedInAccount account)
    {
        _viewedUserId = account.UserId;
        StateHasChanged();
    }

    private async Task ActivateAccount(LoggedInAccount account)
    {
        _busy = true;
        await InvokeAsync(StateHasChanged);
        try
        {
            AccountLoginStatus status;
            try
            {
                status = await _bridge.UpdateSingleAccountStatus(account);
            }
            catch (Exception ex)
            {
                // Selecting an account must never dead-end. Fall back to what we already knew.
                Log.Warning(ex, "Could not verify account {UserId} on selection", account.UserId);
                status = account.Status;
            }

            if (status == AccountLoginStatus.Expired)
            {
                _ = _snackbar.Add(L["auth-menu-session-expired-warning"], Severity.Warning);
                BeginRelogin(account);
                return;
            }

            if (status == AccountLoginStatus.Unreachable)
            {
                // Select it anyway; it will be re-checked in the background.
                _ = _snackbar.Add(L["auth-menu-token-verify-offline"], Severity.Info);
            }

            _bridge.SetActiveAccountId(account.UserId);
            _viewedUserId = account.UserId;
        }
        finally
        {
            _busy = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private void BeginRelogin(LoggedInAccount account)
    {
        if (account.LoginInfo.DiscordToken != null && account.LoginInfo.Token == null)
        {
            ReloginDiscord(account);
            return;
        }

        _relogUserId = account.UserId;
        _signInUsername = account.LoginInfo.Username;
        _mode = Mode.SignIn;
        StateHasChanged();
    }

    private void LinkAccount(LoggedInAccount account)
    {
        _linkUserId = account.UserId;
        _linkUsername = account.LoginInfo.Username;
        _mode = Mode.LinkAccount;
        StateHasChanged();
    }

    private void LinkSteam(LoggedInAccount account) =>
        Task.Run(async () => await RunAttach(true, account, L.GetString("auth-menu-steam-linked-status", ("account", account.LoginInfo.Username))));

    private void LinkDiscord(LoggedInAccount account) =>
        Task.Run(async () => await RunAttach(false, account, L.GetString("auth-menu-discord-linked-status", ("account", account.LoginInfo.Username))));

    private void ReloginDiscord(LoggedInAccount account) =>
        Task.Run(async () => await RunAttach(false, account, L["auth-menu-discord-renewed"], navigateHome: true));

    private async Task RunAttach(bool steam, LoggedInAccount account, string success, bool navigateHome = false)
    {
        _busy = true;
        await InvokeAsync(StateHasChanged);
        try
        {
            await _bridge.AttachToAccountAsync(steam, account);
            _ = _snackbar.Add(success, Severity.Success);
            if (navigateHome)
            {
                _bridge.SetActiveAccountId(account.UserId);
                _nav.NavigateTo("/");
            }
        }
        catch (OperationCanceledException)
        {
            _ = _snackbar.Add(L["auth-menu-attach-login-error"], Severity.Warning);
        }
        catch (DiscordAuthException ex)
        {
            _ = _snackbar.Add(ex.Message, Severity.Error);
        }
        catch (SteamAuthException ex)
        {
            _ = _snackbar.Add(ex.Message, Severity.Error);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "{Type} attach failed", steam ? "Steam" : "Discord");
            _ = _snackbar.Add(L["auth-menu-attach-connect-fail"], Severity.Error);
        }
        finally
        {
            _busy = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private void SwitchMode(Mode mode)
    {
        if (mode == Mode.SignIn) { _signInUsername = ""; }

        _mode = mode;

        StateHasChanged();
    }
}

public enum Mode
{
    AccountList,
    SignIn,
    Register,
    ForgotPassword,
    LinkAccount
}
