using Robust.Launcher.Api.Models;
using Robust.Launcher.Api.Models.Data;
using Starlight.Launcher.WebUI.Bridge;
using Starlight.Launcher.WebUI.Models.Auth;
using System.Collections.ObjectModel;

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

    public Task<AccountLoginStatus> UpdateSingleAccountStatus(LoggedInAccount account, CancellationToken cancel = default)
        => _loginManager.UpdateSingleAccountStatus(account, cancel);

    public Task<AccountLoginStatus> EnsureAccountFreshAsync(LoggedInAccount? account, CancellationToken cancel = default)
        => _loginManager.EnsureFreshAsync(account, cancel);

    public Task LoginsFirstCheckCompleted => _loginManager.FirstCheckCompleted;

    public void RemoveLogin(Guid userId) => _loginManager.RemoveLogin(userId);

    public void LinkAuthToken(Guid oldUserID, Guid newUserId, LoginInfo authLogin) => _loginManager.LinkAuthToken(oldUserID, newUserId, authLogin);

    public void AddFreshLogin(LoginInfo info) => _loginManager.AddFreshLogin(info);
}
