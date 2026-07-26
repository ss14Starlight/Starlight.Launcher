using Robust.Launcher.Api.Models;
using Robust.Launcher.Api.Models.Data;
using Starlight.Launcher.WebUI.Models.Helpers;

namespace Starlight.Launcher.WebUI.Models.Auth;

public abstract class LoggedInAccount : ObservableObject
{
    public string Username => LoginInfo.Username;
    public Guid UserId => LoginInfo.UserId;

    protected LoggedInAccount(LoginInfo loginInfo) => LoginInfo = loginInfo;

    public LoginInfo LoginInfo { get; }

    public abstract AccountLoginStatus Status { get; }
}
