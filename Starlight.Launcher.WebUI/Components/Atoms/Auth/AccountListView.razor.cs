using Microsoft.AspNetCore.Components;
using MudBlazor;
using Robust.Launcher.Api.Models;
using Serilog;
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

    private static string StatusCssVar(LoggedInAccount account) => account.Status switch
    {
        AccountLoginStatus.Available => "success",
        AccountLoginStatus.Expired => "warning",
        AccountLoginStatus.Unreachable => "error",
        _ => "info"
    };

    private string StatusLabel(LoggedInAccount account) => account.Status switch
    {
        AccountLoginStatus.Available => L["auth-menu-online-status"],
        AccountLoginStatus.Expired => L["auth-menu-expired-status"],
        AccountLoginStatus.Unreachable => L["auth-menu-unreachable-status"],
        _ => L["auth-menu-unsure-status"]
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
                OnAccountRelogin?.Invoke(account);
                return;
            }

            if (status == AccountLoginStatus.Unreachable)
            {
                // Select it anyway; it will be re-checked in the background.
                _ = _snackbar.Add(L["auth-menu-token-verify-offline"], Severity.Info);
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
