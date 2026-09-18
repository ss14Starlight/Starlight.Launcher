namespace Robust.Launcher.Api.Models;

public enum AccountLoginStatus
{
    /// <summary>
    ///     We have not checked this login yet. This is a transient state: a check is either
    ///     in flight or about to start, and the account must never be left here indefinitely.
    /// </summary>
    Unsure = 0,

    /// <summary>
    ///     Last we checked, the login token was still valid.
    /// </summary>
    Available,

    /// <summary>
    ///     The login token expired and we need the user to log in again.
    /// </summary>
    Expired,

    /// <summary>
    ///     We could not reach the auth server, so we do not know whether the token is still good.
    ///     The token is probably fine; this is not a reason to make the user log in again.
    /// </summary>
    Unreachable
}
