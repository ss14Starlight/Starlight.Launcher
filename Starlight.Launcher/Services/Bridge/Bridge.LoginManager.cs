using System.Collections.ObjectModel;
using Robust.Launcher.Api.Models.Data;
using Starlight.Launcher.WebUI.Bridge;
using Starlight.Launcher.WebUI.Models.Auth;

namespace Starlight.Launcher.Services.Bridge;

public sealed partial class Bridge : IBridge
{
    public event Action? LoginEntriesChanged
    {
        add => _loginManager.LoginsChanged += value;
        remove => _loginManager.LoginsChanged -= value;
    }

    public Guid? GetActiveAccountId() => _loginManager.ActiveAccountId;

    public void SetActiveAccountId(Guid? activeAccountId) => _loginManager.ActiveAccountId = activeAccountId;

    public LoggedInAccount? GetActiveAccount() => _loginManager.ActiveAccount;

    public ReadOnlyObservableCollection<LoggedInAccount> GetLoginEntries() => _loginManager.Logins;

    public async Task UpdateSingleAccountStatus(LoggedInAccount account) => _loginManager.UpdateSingleAccountStatus(account);

    public void RemoveLogin(Guid userId) => _loginManager.RemoveLogin(userId);

    public void LinkAuthToken(Guid oldUserID, Guid newUserId, LoginInfo authLogin) => _loginManager.LinkAuthToken(oldUserID, newUserId, authLogin);

    public void AddFreshLogin(LoginInfo info) => _loginManager.AddFreshLogin(info);
}
