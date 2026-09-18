using System;

namespace Robust.Launcher.Api.Models;

public sealed class LoginToken
{
    /// <summary>
    ///   The actual token string that can be used to authenticate with the server.
    /// </summary>
    public string Token { get; set; } = "";

    /// <summary>
    ///    When this token expires. If the token is expired, it should not be used to authenticate.
    /// </summary>
    public DateTimeOffset ExpireTime { get; set; }

    /// <summary>
    ///     When we received this token, if we know. Default means unknown, which is the case for
    ///     tokens saved by older launcher versions.
    /// </summary>
    public DateTimeOffset IssuedTime { get; set; }
}
