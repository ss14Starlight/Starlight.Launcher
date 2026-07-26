using System.Collections.ObjectModel;
using Robust.Launcher.Api.Models.Data;
using Starlight.Launcher.WebUI.Models.Auth;

namespace Starlight.Launcher.WebUI.Bridge;

public partial interface IBridge
{
    event Action? LoginEntriesChanged;

    Guid? GetActiveAccountId();

    void SetActiveAccountId(Guid? activeAccountId);

    LoggedInAccount? GetActiveAccount();

    ReadOnlyObservableCollection<LoggedInAccount> GetLoginEntries();

    Task UpdateSingleAccountStatus(LoggedInAccount account);

    void RemoveLogin(Guid userId);

    void LinkAuthToken(Guid oldUserID, Guid newUserId, LoginInfo authLogin);

    void AddFreshLogin(LoginInfo info);
}
