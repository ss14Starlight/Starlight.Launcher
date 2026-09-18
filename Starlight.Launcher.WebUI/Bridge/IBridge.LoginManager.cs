using Robust.Launcher.Api.Models;
using Robust.Launcher.Api.Models.Data;
using Starlight.Launcher.WebUI.Models.Auth;
using System.Collections.ObjectModel;

namespace Starlight.Launcher.WebUI.Bridge;

public partial interface IBridge
{
    event Action? LoginEntriesChanged;

    Guid? GetActiveAccountId();

    void SetActiveAccountId(Guid? activeAccountId);

    LoggedInAccount? GetActiveAccount();

    ReadOnlyObservableCollection<LoggedInAccount> GetLoginEntries();

    Task<AccountLoginStatus> UpdateSingleAccountStatus(LoggedInAccount account, CancellationToken cancel = default);

    Task<AccountLoginStatus> EnsureAccountFreshAsync(LoggedInAccount? account, CancellationToken cancel = default);

    Task LoginsFirstCheckCompleted { get; }

    void RemoveLogin(Guid userId);

    void LinkAuthToken(Guid oldUserID, Guid newUserId, LoginInfo authLogin);

    void AddFreshLogin(LoginInfo info);
}
