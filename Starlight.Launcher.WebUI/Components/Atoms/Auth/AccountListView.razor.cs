using Microsoft.AspNetCore.Components;
using MudBlazor;
using Robust.Launcher.Api.Api;
using Robust.Launcher.Api.Models;
using Starlight.Launcher.WebUI.Bridge;
using Starlight.Launcher.WebUI.Localization;
using Starlight.Launcher.WebUI.Models.Auth;

namespace Starlight.Launcher.WebUI.Components.Atoms.Auth;

public partial class AccountListView : LocalizedComponentBase
{
    [Parameter, EditorRequired] public Action<LoggedInAccount>? OnAccountRelogin { get; set; }
    [Parameter, EditorRequired] public Action<LoggedInAccount>? OnDiscordLink { get; set; }
    [Parameter, EditorRequired] public Action<LoggedInAccount>? OnSteamLink { get; set; }
    [Parameter, EditorRequired] public Action<LoggedInAccount>? OnLink { get; set; }
    [Parameter, EditorRequired] public Action? OnSignIn { get; set; }
    [Inject] private IBridge _bridge { get; set; } = default!;
    [Inject] private ISnackbar _snackbar { get; set; } = default!;

    private bool Busy;

    private static string StatusCssVar(AccountLoginStatus s) => s switch
    {
        AccountLoginStatus.Available => "success",
        AccountLoginStatus.Expired => "warning",
        AccountLoginStatus.Unsure => "info",
        _ => "surface"
    };

    private string StatusLabel(AccountLoginStatus s) => s switch
    {
        AccountLoginStatus.Available => L["auth-menu-online-status"],
        AccountLoginStatus.Expired => L["auth-menu-expired-status"],
        AccountLoginStatus.Unsure => L["auth-menu-unsure-status"],
        _ => s.ToString()
    };

    private void RemoveAccount(LoggedInAccount account)
    {
        _bridge.RemoveLogin(account.UserId);
        _ = _snackbar.Add(L.GetString("auth-menu-account-deleted", ("account", account.LoginInfo.Username)), Severity.Info);
    }

    private async Task SelectAccount(LoggedInAccount account)
    {
        Busy = true;
        await InvokeAsync(StateHasChanged);
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
                OnAccountRelogin?.Invoke(account);
                return;
            }

            _bridge.SetActiveAccountId(account.UserId);
        }
        finally
        {
            Busy = false;
            await InvokeAsync(StateHasChanged);
        }
    }
}
