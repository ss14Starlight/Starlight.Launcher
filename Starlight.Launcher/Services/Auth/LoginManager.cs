using Avalonia.Threading;
using Robust.Launcher.Api.Api;
using Robust.Launcher.Api.Models;
using Robust.Launcher.Api.Models.Data;
using Robust.Launcher.Api.Utility;
using Serilog;
using Starlight.Launcher.Services.Settings;
using Starlight.Launcher.WebUI.Models.Auth;
using Starlight.Launcher.WebUI.Models.Helpers;
using System.Collections.ObjectModel;

namespace Starlight.Launcher.Services.Auth;

public sealed partial class LoginManager : ObservableObject, IAsyncDisposable
{
    private readonly AuthApi _authApi;
    private readonly StarlightAuthApi _starlightAuthApi;
    private readonly SettingsService _settings;

    public static readonly TimeSpan TokenRefreshInterval = TimeSpan.FromMinutes(5);

    private CancellationTokenSource? _cts;
    private Task? _refreshTask;

    private Guid? _activeLoginId;

    private readonly Dictionary<Guid, ActiveLoginData> _logins = new();
    private readonly object _loginsLock = new();

    private readonly ObservableCollection<LoggedInAccount> _loginsView = new();
    public ReadOnlyObservableCollection<LoggedInAccount> Logins { get; }

    public event Action? LoginsChanged;

    public Guid? ActiveAccountId
    {
        get => _activeLoginId;
        set
        {
            if (value != null)
            {
                lock (_loginsLock)
                {
                    if (!_logins.ContainsKey(value.Value))
                        throw new ArgumentException("We do not have a login with that ID.");
                }
            }

            if (SetField(ref _activeLoginId, value))
            {
                OnPropertyChanged(nameof(ActiveAccount));

                var appSettings = _settings.GetSettings();
                appSettings.SelectedLoginId = value;
                _settings.WriteSettings(appSettings);
            }
        }
    }

    public LoggedInAccount? ActiveAccount
    {
        get
        {
            if (_activeLoginId == null)
                return null;

            lock (_loginsLock)
            {
                return _logins.TryGetValue(_activeLoginId.Value, out var data) ? data : null;
            }
        }
        set => ActiveAccountId = value?.UserId;
    }

    public LoginManager(AuthApi authApi, SettingsService settings, StarlightAuthApi starlightAuthApi)
    {
        _authApi = authApi;
        _settings = settings;
        _starlightAuthApi = starlightAuthApi;

        Logins = new ReadOnlyObservableCollection<LoggedInAccount>(_loginsView);

        foreach (var loginInfo in _settings.GetLogins().Values)
        {
            var data = new ActiveLoginData(loginInfo);
            _logins[loginInfo.UserId] = data;
            _loginsView.Add(data);
        }

        var selectedId = _settings.GetSettings().SelectedLoginId;
        if (selectedId.HasValue && _logins.ContainsKey(selectedId.Value))
        {
            _activeLoginId = selectedId;
        }

        _settings.LoginsChanged += OnSettingsLoginsChanged;
    }

    public void LinkAuthToken(Guid oldUserID, Guid newUserId, LoginInfo authLogin)
    {
        var existing = Logins.FirstOrDefault(l => l.UserId == oldUserID);
        if (existing is null)
            return;

        var loginInfo = new LoginInfo()
        {
            UserId = newUserId,
            Username = authLogin.Username,
            Token = authLogin.Token,
            DiscordToken = existing.LoginInfo.DiscordToken,
            DiscordRefreshToken = existing.LoginInfo.DiscordRefreshToken,
            DiscordSessionId = existing.LoginInfo.DiscordSessionId,
            AuthServerUrl = authLogin.AuthServerUrl,
        };
        AddFreshLogin(loginInfo);
    }

    private void OnSettingsLoginsChanged()
    {
        var current = _settings.GetLogins();

        List<ActiveLoginData> toRemoveFromView = new();
        List<ActiveLoginData> toAddToView = new();
        bool activeWasRemoved = false;

        lock (_loginsLock)
        {
            var toRemove = _logins.Keys.Where(k => !current.ContainsKey(k)).ToList();
            foreach (var id in toRemove)
            {
                if (_logins.Remove(id, out var data))
                    toRemoveFromView.Add(data);

                if (_activeLoginId == id)
                {
                    _activeLoginId = null;
                    activeWasRemoved = true;
                }
            }

            foreach (var (id, info) in current)
            {
                if (!_logins.ContainsKey(id))
                {
                    var data = new ActiveLoginData(info);
                    _logins[id] = data;
                    toAddToView.Add(data);
                }
            }
        }

        if (toRemoveFromView.Count > 0 || toAddToView.Count > 0)
        {
            DispatchToUi(() =>
            {
                foreach (var d in toRemoveFromView)
                    _loginsView.Remove(d);
                foreach (var d in toAddToView)
                    _loginsView.Add(d);
            });
        }

        LoginsChanged?.Invoke();

        if (activeWasRemoved)
            OnPropertyChanged(nameof(ActiveAccount));
    }

    public void Initialize()
    {
        FixStoredDiscordUsernames();

        _cts = new CancellationTokenSource();
        _refreshTask = RunRefreshLoop(_cts.Token);

        Task.Run(async () => await RefreshAllTokens());
    }

    private async Task RunRefreshLoop(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TokenRefreshInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await RefreshAllTokens();
            }
        }
        catch (OperationCanceledException)
        {
            // normal shutdown
        }
    }

    private async Task RefreshAllTokens()
    {
        Log.Debug("Refreshing all tokens.");

        const int DelayStart = 2;
        const int DelayValue = 200;

        ActiveLoginData[] snapshot;
        lock (_loginsLock)
        {
            snapshot = _logins.Values.ToArray();
        }

        await Task.WhenAll(snapshot.Select(async (l, i) =>
        {
            if (l.Status == AccountLoginStatus.Expired)
            {
                Log.Warning("Token for {login} is already expired", l.LoginInfo);
                return;
            }

            if (l.LoginInfo.Token == null && l.LoginInfo.DiscordToken == null)
            {
                Log.Warning("Token for {login} doesn't have any access tokens", l.LoginInfo);
                l.SetStatus(AccountLoginStatus.Expired);
                return;
            }

            if (l.LoginInfo.DiscordToken != null && l.LoginInfo.DiscordToken.IsTimeExpired()
                && string.IsNullOrEmpty(l.LoginInfo.DiscordRefreshToken))
            {
                Log.Warning("Discord token for {login} expired and no refresh token", l.LoginInfo);
                l.SetStatus(AccountLoginStatus.Expired);
                return;
            }

            if (i > DelayStart)
                await Task.Delay(DelayValue * (i - DelayStart));

            try
            {
                await UpdateSingleAccountStatus(l);
            }
            catch (AuthApiException e)
            {
                Log.Warning(e, "AuthApiException refreshing {login}", l.LoginInfo);
            }
            catch (Exception e)
            {
                Log.Warning(e, "Unexpected error refreshing {login}", l.LoginInfo);
            }

            Log.Information("Status for {login} after refresh: {status}", l.LoginInfo, l.Status);
        }));

        PersistLogins();
        LoginsChanged?.Invoke();
    }

    public void AddFreshLogin(LoginInfo info)
    {
        ActiveLoginData data;
        bool isNew = false;

        lock (_loginsLock)
        {
            if (_logins.TryGetValue(info.UserId, out var existing))
            {
                existing.LoginInfo.Token = info.Token;
                existing.LoginInfo.DiscordToken = info.DiscordToken;
                existing.LoginInfo.DiscordRefreshToken = info.DiscordRefreshToken;
                existing.LoginInfo.DiscordSessionId = info.DiscordSessionId;
                data = existing;
            }
            else
            {
                data = new ActiveLoginData(info);
                _logins[info.UserId] = data;
                isNew = true;
            }
        }

        if (isNew)
        {
            DispatchToUi(() => _loginsView.Add(data));
            LoginsChanged?.Invoke();
        }

        data.SetStatus(AccountLoginStatus.Available);

        _settings.UpdateLogin(info);
    }

    public void RemoveLogin(Guid userId)
    {
        ActiveLoginData? removed = null;
        bool wasActive = false;

        lock (_loginsLock)
        {
            if (_logins.Remove(userId, out var data))
                removed = data;

            if (_activeLoginId == userId)
            {
                _activeLoginId = null;
                wasActive = true;
            }
        }

        if (removed != null)
        {
            DispatchToUi(() => _loginsView.Remove(removed));
        }

        if (wasActive)
        {
            OnPropertyChanged(nameof(ActiveAccount));

            var appSettings = _settings.GetSettings();
            appSettings.SelectedLoginId = null;
            _settings.WriteSettings(appSettings);
        }

        var current = _settings.GetLogins();
        if (current.Remove(userId))
            _settings.WriteLogins(current);
    }

    public void UpdateToNewToken(LoggedInAccount account, LoginToken token)
    {
        var cast = (ActiveLoginData)account;
        cast.SetStatus(AccountLoginStatus.Available);
        account.LoginInfo.Token = token;
        _settings.UpdateLogin(account.LoginInfo);

        PersistLogins();
    }

    /// <exception cref="AuthApiException">Thrown if an API error occured.</exception>
    public Task UpdateSingleAccountStatus(LoggedInAccount account) => UpdateSingleAccountStatus((ActiveLoginData)account);

    private async Task UpdateSingleAccountStatus(ActiveLoginData data)
    {
        if (data.LoginInfo.Token != null && data.LoginInfo.Token.ShouldRefresh() && data.LoginInfo.AuthServerUrl != null)
        {
            Log.Debug("Refreshing token for {login}", data.LoginInfo);
            var newTokenHopefully = await _authApi.RefreshTokenAsync(data.LoginInfo.Token.Token, new UrlFallbackSet(data.LoginInfo.AuthServerUrl));
            if (newTokenHopefully == null)
            {
                data.SetStatus(AccountLoginStatus.Expired);
                Log.Debug("Token for {login} expired while refreshing it", data.LoginInfo);
            }
            else
            {
                Log.Debug("Refreshed token for {login}", data.LoginInfo);
                data.LoginInfo.Token = newTokenHopefully;
                data.SetStatus(AccountLoginStatus.Available);

                _settings.UpdateLogin(data.LoginInfo);
            }
        }
        else if (data.LoginInfo.DiscordToken != null && data.LoginInfo.DiscordToken.ShouldRefresh())
        {
            Log.Debug("Refreshing Starlight token for {login}", data.LoginInfo);
            var result = await _starlightAuthApi.RefreshTokenAsync(
                data.LoginInfo.DiscordSessionId!, data.LoginInfo.DiscordRefreshToken!);

            if (result == null)
            {
                data.SetStatus(AccountLoginStatus.Expired);
                Log.Debug("Starlight token for {login} expired/revoked while refreshing", data.LoginInfo);
            }
            else
            {
                data.LoginInfo.DiscordToken = new LoginToken
                {
                    Token = result.AccessToken,
                    ExpireTime = result.AccessExpiresUtc,
                };
                data.LoginInfo.DiscordRefreshToken = result.RefreshToken;
                data.LoginInfo.DiscordSessionId = result.SessionId;

                data.SetStatus(AccountLoginStatus.Available);
                _settings.UpdateLogin(data.LoginInfo);

                Log.Debug("Refreshed Starlight token for {login}", data.LoginInfo);
            }
        }
        else if (data.Status == AccountLoginStatus.Unsure && data.LoginInfo.Token != null && data.LoginInfo.AuthServerUrl != null)
        {
            var valid = await _authApi.CheckTokenAsync(data.LoginInfo.Token.Token, new UrlFallbackSet(data.LoginInfo.AuthServerUrl));
            Log.Debug("Token for {login} still valid? {valid}", data.LoginInfo, valid);
            data.SetStatus(valid ? AccountLoginStatus.Available : AccountLoginStatus.Expired);
        }
        else if (data.Status == AccountLoginStatus.Unsure && data.LoginInfo.DiscordToken != null)
        {
            var valid = await _starlightAuthApi.ValidateDiscordToken(data.LoginInfo.DiscordToken.Token);
            Log.Debug("Discord token for {login} still valid? {valid}", data.LoginInfo, valid);
            data.SetStatus(valid ? AccountLoginStatus.Available : AccountLoginStatus.Expired);
        }
    }

    private void PersistLogins()
    {
        Dictionary<Guid, LoginInfo> snapshot;
        lock (_loginsLock)
        {
            snapshot = _logins.ToDictionary(kv => kv.Key, kv => kv.Value.LoginInfo);
        }
        _settings.WriteLogins(snapshot);
    }

    private void DispatchToUi(Action action) => Dispatcher.UIThread.Post(action);

    public async ValueTask DisposeAsync()
    {
        _settings.LoginsChanged -= OnSettingsLoginsChanged;

        if (_cts != null)
        {
            _cts.Cancel();
            try
            {
                if (_refreshTask != null)
                    await _refreshTask;
            }
            catch (OperationCanceledException) { }
            _cts.Dispose();
        }
    }

    private sealed partial class ActiveLoginData : LoggedInAccount
    {
        private AccountLoginStatus _status;

        public ActiveLoginData(LoginInfo info) : base(info)
        {
        }

        public override AccountLoginStatus Status => _status;

        public void SetStatus(AccountLoginStatus status)
        {
            if (_status == status)
                return;

            _status = status;

            Log.Debug(
                "Setting status for login {account} to {status}",
                LoginInfo,
                status);

            OnPropertyChanged(nameof(Status));
        }

        public void SetUsername(string username)
        {
            if (string.Equals(LoginInfo.Username, username, StringComparison.Ordinal))
                return;

            LoginInfo.Username = username;
            OnPropertyChanged(nameof(Username));
        }
    }

    public void FixStoredDiscordUsernames()
    {
        List<ActiveLoginData> candidates;
        lock (_loginsLock)
        {
            candidates = _logins.Values
                .Where(d => d.LoginInfo is { Token: null, DiscordToken: not null })
                .ToList();
        }

        var changed = new List<LoginInfo>();

        foreach (var data in candidates)
        {
            var current = data.LoginInfo.Username;
            var result = UsernameModerator.Moderate(current);

            var fixedName = result.Outcome switch
            {
                UsernameModerationOutcome.Accepted => current,
                UsernameModerationOutcome.Sanitized => result.Username,
                _ => FallbackUsername(data.UserId),
            };

            if (string.Equals(fixedName, current, StringComparison.Ordinal))
                continue;

            data.SetUsername(fixedName);
            changed.Add(data.LoginInfo);
            Log.Information("Auto-fixed Discord username {Old} -> {New}", current, fixedName);
        }

        if (changed.Count == 0)
            return;

        foreach (var info in changed)
            _settings.UpdateLogin(info);

        LoginsChanged?.Invoke();
    }

    private static string FallbackUsername(Guid userId) => $"Player{userId:N}"[..10]; // e.g. "Player3fa85f"
}
