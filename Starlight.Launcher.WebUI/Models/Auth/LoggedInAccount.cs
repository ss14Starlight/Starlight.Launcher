using Robust.Launcher.Api.Models;
using Robust.Launcher.Api.Models.Data;
using Starlight.Launcher.WebUI.Models.Helpers;

namespace Starlight.Launcher.WebUI.Models.Auth;

public abstract class LoggedInAccount(LoginInfo loginInfo) : ObservableObject
{
    /// <summary>
    ///    The username of the account. This is used to identify the user in the UI and for logging purposes.
    /// </summary>
    public string Username => LoginInfo.Username;

    /// <summary>
    ///   The user ID of the account. This is used to identify the user in the UI and for logging purposes.
    /// </summary>
    public Guid UserId => LoginInfo.UserId;

    /// <summary>
    ///   The login information for the account. This includes the username, user ID, and any other relevant information.
    /// </summary>
    public LoginInfo LoginInfo { get; } = loginInfo;

    /// <summary>
    ///    The current status of the account. This is used to determine whether the user is logged in, logged out, or if there was an error.
    /// </summary>
    public abstract AccountLoginStatus Status { get; }

    /// <summary>
    ///     Whether a status check is in flight right now. Lets the UI distinguish "we are asking the
    ///     server" from "we asked and could not get an answer".
    /// </summary>
    public virtual bool IsRefreshing => false;
}
