using Microsoft.AspNetCore.Components;
using MudBlazor;
using Serilog;
using Starlight.Launcher.WebUI.Bridge;
using Starlight.Launcher.WebUI.Localization;
using Starlight.Launcher.WebUI.Models.Auth;
using Starlight.Launcher.WebUI.Models.DiscordAuthService;

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

    private async Task BeginRelogin(LoggedInAccount account)
    {
        if (account.LoginInfo.DiscordToken != null && account.LoginInfo.Token == null)
        {
            await ReloginDiscord(account);
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

    private void SwitchMode(Mode mode)
    {
        if (mode == Mode.SignIn) { _signInUsername = ""; }
        //if (mode == Mode.Register) { _registerErrors = null; _registerSuccessMessage = null; }

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
