using Microsoft.AspNetCore.Components;
using MudBlazor;
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

    /// <summary>
    ///     Makes the account the one used to play.
    /// </summary>
    [Parameter, EditorRequired] public Func<LoggedInAccount, Task>? OnSelect { get; set; }

    /// <summary>
    ///     Shows the account's profile without making it active.
    /// </summary>
    [Parameter] public Action<LoggedInAccount>? OnView { get; set; }

    [Parameter] public Guid? ViewedUserId { get; set; }
    [Parameter] public bool Busy { get; set; }

    [Inject] private IBridge _bridge { get; set; } = default!;
    [Inject] private ISnackbar _snackbar { get; set; } = default!;

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

    internal static bool CanLinkDiscord(LoggedInAccount acc)
        => acc.Status != AccountLoginStatus.Expired
           && (acc.LoginInfo.Token != null || acc.LoginInfo.SteamToken == null)
           && acc.LoginInfo.DiscordToken == null;

    internal static bool CanLinkSteam(LoggedInAccount acc)
        => acc.Status != AccountLoginStatus.Expired
           && (acc.LoginInfo.Token != null || acc.LoginInfo.DiscordToken != null)
           && acc.LoginInfo.SteamToken == null;

    private Task Select(LoggedInAccount account)
        => OnSelect?.Invoke(account) ?? Task.CompletedTask;

    private void RemoveAccount(LoggedInAccount account)
    {
        _bridge.RemoveLogin(account.UserId);
        _ = _snackbar.Add(L.GetString("auth-menu-account-deleted", ("account", account.LoginInfo.Username)), Severity.Info);
    }
}
