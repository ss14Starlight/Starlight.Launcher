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

/// <summary>
///     Owns the set of logged-in accounts and keeps their tokens alive.
/// </summary>
public sealed partial class LoginManager : ObservableObject, IAsyncDisposable
{
    private readonly AuthApi _authApi;
    private readonly StarlightAuthApi _starlightAuthApi;
    private readonly SettingsService _settings;

    /// <summary>
    ///     How often we look for tokens that are about to expire. This is a poll, not a refresh:
    ///     a tick only costs network traffic for accounts that are actually close to expiry.
    /// </summary>
    public static readonly TimeSpan TokenRefreshInterval = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan _revalidateInterval = TimeSpan.FromMinutes(30);

    private static readonly TimeSpan _retryAfterUnreachable = TimeSpan.FromSeconds(45);

    private static readonly TimeSpan _launchFreshnessBuffer = TimeSpan.FromMinutes(5);

    private const int MaxParallelChecks = 4;

    private CancellationTokenSource? _cts;
    private Task? _refreshTask;

    private Guid? _activeLoginId;

    private readonly Dictionary<Guid, ActiveLoginData> _logins = new();
    private readonly object _loginsLock = new();

    private readonly ObservableCollection<LoggedInAccount> _loginsView = new();
    public ReadOnlyObservableCollection<LoggedInAccount> Logins { get; }

    public event Action? LoginsChanged;

    private volatile bool _initialized;

    private int _suppressSettingsSync;

    private readonly TaskCompletionSource _firstCheckDone = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    ///     Completes once every stored account has been checked at least once (or the attempt failed).
    /// </summary>
    public Task FirstCheckCompleted => _firstCheckDone.Task;

    /// <summary>
    ///    The currently selected account, or null if none is selected. This is the account that will be
    /// </summary>
    public Guid? ActiveAccountId
    {
        get => _activeLoginId;
        set
        {
            if (value != null)
            {
                bool known;
                lock (_loginsLock)
                {
                    known = _logins.ContainsKey(value.Value);
                }

                if (!known)
                {
                    // Not fatal, and definitely not worth taking the UI down for.
                    Log.Warning("Ignoring attempt to select unknown login {UserId}", value);
                    return;
                }
            }

            if (!SetField(ref _activeLoginId, value))
                return;

            OnPropertyChanged(nameof(ActiveAccount));

            var appSettings = _settings.GetSettings();
            appSettings.SelectedLoginId = value;
            _settings.WriteSettings(appSettings);
        }
    }

    /// <summary>
    ///    The currently selected account, or null if none is selected. This is the account that will be
    /// </summary>
    public LoggedInAccount? ActiveAccount
    {
        get
        {
            if (_activeLoginId is not { } id)
                return null;

            lock (_loginsLock)
            {
                return _logins.TryGetValue(id, out var data) ? data : null;
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

        _settings.LoginsChanged += OnSettingsLoginsChanged;
    }

    #region Lifetime

    public async Task InitializeAsync()
    {
        try
        {
            await _settings.WaitForLoginsLoadedAsync();

            var stored = await _settings.GetLoginsAsync();
            var added = new List<ActiveLoginData>(stored.Count);

            lock (_loginsLock)
            {
                foreach (var (id, info) in stored)
                {
                    if (_logins.ContainsKey(id))
                        continue;

                    var data = new ActiveLoginData(info);
                    _logins[id] = data;
                    added.Add(data);
                }
            }

            AddToView(added);

            var selectedId = (await _settings.GetSettingsAsync()).SelectedLoginId;
            if (selectedId.HasValue)
            {
                lock (_loginsLock)
                {
                    if (_logins.ContainsKey(selectedId.Value))
                        _activeLoginId = selectedId;
                }
            }

            FixStoredDiscordUsernames();

            _initialized = true;
            LoginsChanged?.Invoke();
            OnPropertyChanged(nameof(ActiveAccount));

            _cts = new CancellationTokenSource();
            _refreshTask = RunRefreshLoopAsync(_cts.Token);
        }
        catch (Exception e)
        {
            // Without this the whole manager would die silently and every account would render
            // as "Checking..." with nothing ever coming to update it.
            Log.Error(e, "LoginManager failed to initialize");

            ActiveLoginData[] loaded;
            lock (_loginsLock)
            {
                loaded = _logins.Values.ToArray();
            }

            foreach (var data in loaded)
            {
                data.SetStatus(AccountLoginStatus.Unreachable);
                data.MarkChecked();
            }

            _initialized = true;
            LoginsChanged?.Invoke();
            _ = _firstCheckDone.TrySetResult();
        }
    }

    private async Task RunRefreshLoopAsync(CancellationToken cancel)
    {
        try
        {
            // First pass runs immediately: stored accounts start out unchecked.
            await RefreshAllAsync(cancel);
            _ = _firstCheckDone.TrySetResult();

            using var timer = new PeriodicTimer(TokenRefreshInterval);
            while (await timer.WaitForNextTickAsync(cancel))
                await RefreshAllAsync(cancel);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        catch (Exception e)
        {
            Log.Error(e, "Token refresh loop stopped unexpectedly");
        }
        finally
        {
            _ = _firstCheckDone.TrySetResult();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _settings.LoginsChanged -= OnSettingsLoginsChanged;

        if (_cts != null)
        {
            await _cts.CancelAsync();
            try
            {
                if (_refreshTask != null)
                    await _refreshTask;
            }
            catch (OperationCanceledException) { }
            _cts.Dispose();
            _cts = null;
        }
    }

    #endregion

    #region Refreshing

    private async Task RefreshAllAsync(CancellationToken cancel)
    {
        ActiveLoginData[] snapshot;
        lock (_loginsLock)
        {
            snapshot = _logins.Values.ToArray();
        }

        if (snapshot.Length == 0)
            return;

        var due = snapshot.Where(IsCheckDue).ToArray();
        if (due.Length == 0)
            return;

        Log.Debug("Checking {Count} of {Total} logins", due.Length, snapshot.Length);

        using var gate = new SemaphoreSlim(MaxParallelChecks, MaxParallelChecks);

        await Task.WhenAll(due.Select(async data =>
        {
            await gate.WaitAsync(cancel);
            try
            {
                _ = await RefreshAccountAsync(data, force: false, cancel);
            }
            finally
            {
                _ = gate.Release();
            }
        }));

        LoginsChanged?.Invoke();
    }

    private static bool IsCheckDue(ActiveLoginData data)
    {
        // Never checked: always due. This is the state that used to get stuck.
        if (data.LastCheckUtc is not { } last)
            return true;

        // Expired accounts need the user, not us. Re-check occasionally in case the
        // server un-revoked the session, but do not hammer it.
        if (data.Status == AccountLoginStatus.Expired)
            return DateTime.UtcNow - last >= _revalidateInterval;

        if (data.Status == AccountLoginStatus.Unreachable)
            return DateTime.UtcNow - last >= _retryAfterUnreachable;

        if (NeedsRenewal(data.LoginInfo))
            return true;

        return DateTime.UtcNow - last >= _revalidateInterval;
    }

    private static bool NeedsRenewal(LoginInfo info)
    {
        if (info.Token is { } robust && robust.ShouldRefresh(LoginTokenExt.RefreshBuffer))
            return true;

        if (info.DiscordToken is { } discord && discord.ShouldRefresh(StarlightRenewalBuffer(discord)))
            return true;

        if (info.SteamToken is { } steam && steam.ShouldRefresh(StarlightRenewalBuffer(steam)))
            return true;

        return false;
    }

    private static TimeSpan StarlightRenewalBuffer(LoginToken token)
        => token.RenewalBuffer(LoginTokenExt.ShortRefreshBuffer);

    private async Task<AccountLoginStatus> RefreshAccountAsync(
        ActiveLoginData data,
        bool force,
        CancellationToken cancel)
    {
        // One refresh per session at a time: rotating refresh tokens cannot survive a race.
        await data.Gate.WaitAsync(cancel);
        data.SetRefreshing(true);
        try
        {
            var info = data.LoginInfo;

            if (info.Token == null && info.DiscordToken == null && info.SteamToken == null)
            {
                Log.Warning("Login {Login} has no tokens at all", info);
                data.SetStatus(AccountLoginStatus.Expired);
                data.MarkChecked();
                return data.Status;
            }

            var robust = await CheckRobustAsync(data, force, cancel);
            var discord = await CheckStarlightAsync(data, steam: false, force, cancel);
            var steam = await CheckStarlightAsync(data, steam: true, force, cancel);

            if (robust.Persist || discord.Persist || steam.Persist)
                await PersistAsync(info);

            var status = Combine(robust.State, discord.State, steam.State);
            data.SetStatus(status);
            data.MarkChecked();

            Log.Information(
                "Login {Login}: ss14={Robust} discord={Discord} steam={Steam} => {Status}",
                info, robust.State, discord.State, steam.State, status);

            return status;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            // Anything unexpected is our problem, not the player's. Do not log them out over it.
            Log.Warning(e, "Unexpected error checking login {Login}", data.LoginInfo);
            data.SetStatus(AccountLoginStatus.Unreachable);
            data.MarkChecked();
            return data.Status;
        }
        finally
        {
            data.SetRefreshing(false);
            _ = data.Gate.Release();
        }
    }

    private static AccountLoginStatus Combine(params CredentialState[] states)
    {
        if (states.Any(s => s == CredentialState.Valid))
            return AccountLoginStatus.Available;

        if (states.Any(s => s == CredentialState.Unknown))
            return AccountLoginStatus.Unreachable;

        if (states.Any(s => s == CredentialState.Invalid))
            return AccountLoginStatus.Expired;

        // No credentials present at all; caught earlier, but be explicit.
        return AccountLoginStatus.Expired;
    }

    #endregion

    #region SS14 auth server tokens

    private async Task<(CredentialState State, bool Persist)> CheckRobustAsync(
        ActiveLoginData data,
        bool force,
        CancellationToken cancel)
    {
        var info = data.LoginInfo;
        if (info.Token is not { } token || string.IsNullOrWhiteSpace(token.Token))
            return (CredentialState.None, false);

        // Older logins were stored without the server they came from; fall back to the selected one
        // rather than leaving the account permanently unverifiable.
        var authServerUrl = info.AuthServerUrl ?? _settings.GetSettings().SelectedAuthServer;
        if (authServerUrl == null)
        {
            Log.Warning("Login {Login} has an SS14 token but no auth server to check it against", info);
            return (token.IsTimeExpired() ? CredentialState.Invalid : CredentialState.Unknown, false);
        }

        var urls = new UrlFallbackSet(authServerUrl);

        var buffer = force ? LoginTokenExt.RefreshBuffer + _launchFreshnessBuffer : LoginTokenExt.RefreshBuffer;
        if (token.ShouldRefresh(buffer))
        {
            try
            {
                Log.Debug("Renewing SS14 token for {Login}", info);
                var renewed = await _authApi.RefreshTokenAsync(token.Token, urls);

                if (renewed == null)
                {
                    Log.Information("SS14 token for {Login} was rejected on renewal", info);
                    return (CredentialState.Invalid, false);
                }

                await ApplyAsync(info, i =>
                {
                    i.Token = renewed;
                    i.AuthServerUrl ??= authServerUrl;
                });

                return (CredentialState.Valid, true);
            }
            catch (AuthApiException e)
            {
                Log.Warning(e, "Could not reach the SS14 auth server to renew {Login}", info);
                return (token.IsTimeExpired() ? CredentialState.Invalid : CredentialState.Unknown, false);
            }
        }

        if (token.IsTimeExpired())
            return (CredentialState.Invalid, false);

        // Not near expiry. Ping only when we have no opinion yet or it is time to re-check.
        if (data.Status == AccountLoginStatus.Available && !force && data.LastCheckUtc is { } last
            && DateTime.UtcNow - last < _revalidateInterval)
        {
            return (CredentialState.Valid, false);
        }

        cancel.ThrowIfCancellationRequested();

        try
        {
            var valid = await _authApi.CheckTokenAsync(token.Token, urls);
            return (valid ? CredentialState.Valid : CredentialState.Invalid, false);
        }
        catch (AuthApiException e)
        {
            Log.Warning(e, "Could not reach the SS14 auth server to check {Login}", info);
            return (CredentialState.Unknown, false);
        }
    }

    #endregion

    #region Starlight (Discord / Steam) tokens

    private async Task<(CredentialState State, bool Persist)> CheckStarlightAsync(
        ActiveLoginData data,
        bool steam,
        bool force,
        CancellationToken cancel)
    {
        var info = data.LoginInfo;
        var token = GetAccessToken(info, steam);

        if (token == null || string.IsNullOrWhiteSpace(token.Token))
            return (CredentialState.None, false);

        var sessionId = GetSessionId(info, steam);
        var refreshToken = GetRefreshToken(info, steam);
        var provider = steam ? "Steam" : "Discord";

        var buffer = StarlightRenewalBuffer(token);
        if (force)
            buffer += _launchFreshnessBuffer;

        var canRenew = !string.IsNullOrEmpty(sessionId) && !string.IsNullOrEmpty(refreshToken);

        async Task<(CredentialState State, bool Persist)> RenewAsync()
        {
            Log.Debug("Renewing {Provider} token for {Login}", provider, info);
            var result = await _starlightAuthApi.RefreshTokenAsync(sessionId!, refreshToken!, cancel);

            switch (result.Outcome)
            {
                case TokenCheckOutcome.Valid when result.Tokens is { } tokens:
                    await ApplyAsync(info, i => SetStarlightTokens(i, steam, tokens));
                    Log.Debug(
                        "Renewed {Provider} token for {Login}, valid for {Left}",
                        provider, info, GetAccessToken(info, steam)!.TimeLeft());
                    return (CredentialState.Valid, true);

                case TokenCheckOutcome.Invalid:
                    Log.Information("{Provider} session for {Login} was revoked or expired", provider, info);
                    return (CredentialState.Invalid, false);

                default:
                    // Could not reach the server. The access token may still have life left in it.
                    return (token.IsTimeExpired() ? CredentialState.Unknown : CredentialState.Valid, false);
            }
        }

        if (token.ShouldRefresh(buffer))
        {
            if (canRenew)
                return await RenewAsync();

            // Nothing to renew with. Only the server can tell us whether the access token
            // still works, so ask instead of assuming the worst.
            Log.Debug("{Provider} token for {Login} needs renewal but has no refresh token", provider, info);
            return (await ValidateStarlightAsync(steam, token, cancel), false);
        }

        if (data.Status == AccountLoginStatus.Available && !force && data.LastCheckUtc is { } last
            && DateTime.UtcNow - last < _revalidateInterval)
        {
            return (CredentialState.Valid, false);
        }

        var validation = await ValidateStarlightAsync(steam, token, cancel);

        // The stored expiry claimed the token was still good but the server disagrees. That is
        // normal whenever the expiry we have is a guess, so renew instead of making the player
        // log in again -- the refresh token is what actually holds the session.
        if (validation == CredentialState.Invalid && canRenew)
        {
            Log.Debug("{Provider} access token for {Login} was rejected; trying the refresh token", provider, info);
            return await RenewAsync();
        }

        return (validation, false);
    }

    private async Task<CredentialState> ValidateStarlightAsync(
        bool steam,
        LoginToken token,
        CancellationToken cancel)
    {
        var outcome = steam
            ? await _starlightAuthApi.ValidateSteamTokenAsync(token.Token, cancel)
            : await _starlightAuthApi.ValidateDiscordTokenAsync(token.Token, cancel);

        return outcome switch
        {
            TokenCheckOutcome.Valid => CredentialState.Valid,
            TokenCheckOutcome.Invalid => CredentialState.Invalid,
            _ => CredentialState.Unknown
        };
    }

    private static LoginToken? GetAccessToken(LoginInfo info, bool steam)
        => steam ? info.SteamToken : info.DiscordToken;

    private static string? GetRefreshToken(LoginInfo info, bool steam)
        => steam ? info.SteamRefreshToken : info.DiscordRefreshToken;

    private static string? GetSessionId(LoginInfo info, bool steam)
        => steam ? info.SteamSessionId : info.DiscordSessionId;

    private static void SetStarlightTokens(LoginInfo info, bool steam, StarlightRefreshResult tokens)
    {
        var token = new LoginToken
        {
            Token = tokens.AccessToken,
            ExpireTime = tokens.AccessExpires,
            IssuedTime = DateTimeOffset.UtcNow,
        };

        if (steam)
        {
            info.SteamToken = token;
            info.SteamRefreshToken = tokens.RefreshToken;
            info.SteamSessionId = tokens.SessionId;
        }
        else
        {
            info.DiscordToken = token;
            info.DiscordRefreshToken = tokens.RefreshToken;
            info.DiscordSessionId = tokens.SessionId;
        }
    }

    #endregion

    #region Public operations

    /// <summary>
    ///     Re-checks a single account. Never throws for an auth failure.
    /// </summary>
    public async Task<AccountLoginStatus> UpdateSingleAccountStatus(
        LoggedInAccount account,
        CancellationToken cancel = default)
    {
        if (account is not ActiveLoginData data)
        {
            Log.Warning("UpdateSingleAccountStatus called with an account we do not own");
            return account.Status;
        }

        var status = await RefreshAccountAsync(data, force: false, cancel);
        LoginsChanged?.Invoke();
        return status;
    }

    /// <summary>
    ///     Makes sure an account's tokens will still be valid for a while, renewing them if needed.
    ///     Call this immediately before handing tokens to the game client.
    /// </summary>
    public async Task<AccountLoginStatus> EnsureFreshAsync(
        LoggedInAccount? account,
        CancellationToken cancel = default)
    {
        if (account is not ActiveLoginData data)
            return AccountLoginStatus.Expired;

        try
        {
            var status = await RefreshAccountAsync(data, force: true, cancel);
            LoginsChanged?.Invoke();
            return status;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            Log.Warning(e, "Could not freshen {Login} before launch", data.LoginInfo);
            return data.Status;
        }
    }

    /// <summary>
    ///    Adds a new account to the set of known logins. If an account with the same user ID already exists,
    /// </summary>
    public void AddFreshLogin(LoginInfo info)
    {
        ActiveLoginData data;
        ActiveLoginData? existing;

        lock (_loginsLock)
        {
            if (!_logins.TryGetValue(info.UserId, out existing))
            {
                data = new ActiveLoginData(info);
                _logins[info.UserId] = data;
            }
            else
            {
                data = existing;
            }
        }

        if (existing != null)
        {
            // Merge: a Discord re-login must not wipe a linked SS14 token and vice versa.
            WithSuppressedSync(() => _settings.UpdateLoginAtomic(existing.LoginInfo, target =>
            {
                if (!string.IsNullOrWhiteSpace(info.Username))
                    target.Username = info.Username;

                if (info.Token != null)
                {
                    target.Token = info.Token;
                    target.AuthServerUrl = info.AuthServerUrl ?? target.AuthServerUrl;
                }

                if (info.DiscordToken != null)
                {
                    target.DiscordToken = info.DiscordToken;
                    target.DiscordRefreshToken = info.DiscordRefreshToken;
                    target.DiscordSessionId = info.DiscordSessionId;
                }

                if (info.SteamToken != null)
                {
                    target.SteamToken = info.SteamToken;
                    target.SteamRefreshToken = info.SteamRefreshToken;
                    target.SteamSessionId = info.SteamSessionId;
                }
            }));
        }
        else
        {
            Persist(data.LoginInfo);
            AddToView([data]);
        }

        data.SetStatus(AccountLoginStatus.Available);
        data.MarkChecked();
        data.RaiseUsernameChanged();

        LoginsChanged?.Invoke();
    }

    /// <summary>
    ///     Attaches a password-based SS14 login to an account that was created through Discord/Steam.
    /// </summary>
    public void LinkAuthToken(Guid oldUserId, Guid newUserId, LoginInfo authLogin)
    {
        ActiveLoginData? existing;
        lock (_loginsLock)
        {
            _ = _logins.TryGetValue(oldUserId, out existing);
        }

        if (existing is null)
        {
            Log.Warning("LinkAuthToken: no login with id {UserId}", oldUserId);
            return;
        }

        var old = existing.LoginInfo;

        var hasStarlightToken = old.DiscordToken != null || old.SteamToken != null;

        var merged = new LoginInfo
        {
            UserId = newUserId,
            Username = hasStarlightToken && !string.IsNullOrWhiteSpace(old.Username) ? old.Username : authLogin.Username,
            Token = authLogin.Token,
            AuthServerUrl = authLogin.AuthServerUrl,
            DiscordToken = old.DiscordToken,
            DiscordRefreshToken = old.DiscordRefreshToken,
            DiscordSessionId = old.DiscordSessionId,
            SteamToken = old.SteamToken,
            SteamRefreshToken = old.SteamRefreshToken,
            SteamSessionId = old.SteamSessionId,
        };

        // The user id changes when linking, so the old entry would otherwise linger as a
        // duplicate account the player can never log into.
        if (newUserId != oldUserId)
            RemoveLogin(oldUserId);

        AddFreshLogin(merged);
    }

    /// <summary>
    ///    Removes a login from the set of known accounts. If the removed account was the active one, no
    /// </summary>
    public void RemoveLogin(Guid userId)
    {
        ActiveLoginData? removed;
        var wasActive = false;

        lock (_loginsLock)
        {
            _ = _logins.Remove(userId, out removed);

            if (_activeLoginId == userId)
            {
                _activeLoginId = null;
                wasActive = true;
            }
        }

        if (removed != null)
            DispatchToUi(() => _loginsView.Remove(removed));

        if (wasActive)
        {
            OnPropertyChanged(nameof(ActiveAccount));

            var appSettings = _settings.GetSettings();
            appSettings.SelectedLoginId = null;
            _settings.WriteSettings(appSettings);
        }

        WithSuppressedSync(() =>
        {
            var current = _settings.GetLogins();
            if (current.Remove(userId))
                _settings.WriteLogins(current);
        });

        LoginsChanged?.Invoke();
    }

    #endregion

    #region Persistence

    private void Persist(LoginInfo info)
        => WithSuppressedSync(() => _settings.UpdateLogin(info));

    private async Task ApplyAsync(LoginInfo info, Action<LoginInfo> mutate)
    {
        _ = Interlocked.Increment(ref _suppressSettingsSync);
        try
        {
            await _settings.UpdateLoginAtomicAsync(info, mutate);
        }
        finally
        {
            _ = Interlocked.Decrement(ref _suppressSettingsSync);
        }
    }

    private async Task PersistAsync(LoginInfo info)
    {
        try
        {
            await _settings.SaveLoginsNowAsync();
        }
        catch (Exception e)
        {
            Log.Error(e, "Failed to persist renewed tokens for {Login}", info);
        }
    }

    private void WithSuppressedSync(Action action)
    {
        _ = Interlocked.Increment(ref _suppressSettingsSync);
        try
        {
            action();
        }
        finally
        {
            _ = Interlocked.Decrement(ref _suppressSettingsSync);
        }
    }

    private void OnSettingsLoginsChanged()
    {
        if (!_initialized || Volatile.Read(ref _suppressSettingsSync) > 0)
            return;

        var current = _settings.GetLogins();

        List<ActiveLoginData> toRemove = new();
        List<ActiveLoginData> toAdd = new();
        var activeWasRemoved = false;

        lock (_loginsLock)
        {
            foreach (var id in _logins.Keys.Where(k => !current.ContainsKey(k)).ToList())
            {
                if (_logins.Remove(id, out var data))
                    toRemove.Add(data);

                if (_activeLoginId == id)
                {
                    _activeLoginId = null;
                    activeWasRemoved = true;
                }
            }

            foreach (var (id, info) in current)
            {
                if (_logins.ContainsKey(id))
                    continue;

                var data = new ActiveLoginData(info);
                _logins[id] = data;
                toAdd.Add(data);
            }
        }

        if (toRemove.Count > 0 || toAdd.Count > 0)
        {
            DispatchToUi(() =>
            {
                foreach (var d in toRemove)
                    _ = _loginsView.Remove(d);

                foreach (var d in toAdd)
                    _loginsView.Add(d);
            });

            LoginsChanged?.Invoke();
        }

        if (activeWasRemoved)
            OnPropertyChanged(nameof(ActiveAccount));
    }

    #endregion

    #region Usernames

    /// <summary>
    ///     Repairs Discord-derived usernames that the server would refuse.
    /// </summary>
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
            Persist(info);

        LoginsChanged?.Invoke();
    }

    private static string FallbackUsername(Guid userId) => $"Player{userId:N}"[..10];

    #endregion

    private void AddToView(IReadOnlyList<ActiveLoginData> added)
    {
        if (added.Count == 0)
            return;

        DispatchToUi(() =>
        {
            foreach (var data in added)
                _loginsView.Add(data);
        });
    }

    private static void DispatchToUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }

    private enum CredentialState
    {
        /// <summary>The account does not have this kind of credential.</summary>
        None,

        /// <summary>The server accepted it, or we just renewed it.</summary>
        Valid,

        /// <summary>The server rejected it. Only the user can fix this.</summary>
        Invalid,

        /// <summary>We could not find out. Assume nothing.</summary>
        Unknown
    }

    private sealed class ActiveLoginData(LoginInfo info) : LoggedInAccount(info)
    {
        private AccountLoginStatus _status;
        private bool _refreshing;

        /// <summary>
        ///     Serializes refreshes for this account. Rotating refresh tokens mean a concurrent
        ///     refresh would replay a spent token and get the whole session revoked.
        /// </summary>
        public SemaphoreSlim Gate { get; } = new(1, 1);

        /// <summary>
        ///     When we last got an answer about this account, in UTC. Null means never.
        /// </summary>
        public DateTime? LastCheckUtc { get; private set; }

        public override AccountLoginStatus Status => _status;

        public override bool IsRefreshing => _refreshing;

        /// <summary>
        ///    Updates the status of this account and notifies the UI if it changed.
        /// </summary>
        public void SetStatus(AccountLoginStatus status)
        {
            if (_status == status)
                return;

            _status = status;
            Log.Debug("Login {Account} is now {Status}", LoginInfo, status);
            OnPropertyChanged(nameof(Status));
        }

        /// <summary>
        ///    Whether a status check is in flight right now. Lets the UI distinguish "we are asking the
        /// </summary>
        public void SetRefreshing(bool refreshing)
        {
            if (_refreshing == refreshing)
                return;

            _refreshing = refreshing;
            OnPropertyChanged(nameof(IsRefreshing));
        }

        /// <summary>
        ///   Updates the last-check timestamp to now. This is called after a check completes, even if it failed.
        /// </summary>
        public void MarkChecked() => LastCheckUtc = DateTime.UtcNow;

        /// <summary>
        ///   Updates the username of this account and notifies the UI if it changed.
        /// </summary>
        public void SetUsername(string username)
        {
            if (string.Equals(LoginInfo.Username, username, StringComparison.Ordinal))
                return;

            LoginInfo.Username = username;
            OnPropertyChanged(nameof(Username));
        }

        /// <summary>
        ///  Raises a property change notification for the username. This is used when the username is changed externally, such as when the server updates it.
        /// </summary>
        public void RaiseUsernameChanged() => OnPropertyChanged(nameof(Username));
    }
}
