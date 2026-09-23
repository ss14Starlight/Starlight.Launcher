using Robust.Launcher.Api.Models;
using Robust.Launcher.Api.Models.Data;
using Serilog;
using Starlight.Launcher.WebUI.Models.Auth;
using Starlight.Launcher.WebUI.Models.DiscordAuthService;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Web;

namespace Starlight.Launcher.Services.Auth;

/// <summary>
///     Shared browser-handoff login flow for the Starlight OAuth providers.
/// </summary>
public abstract class StarlightOAuthService(StarlightAuthApi api, LoginManager loginManager)
{
    private static readonly TimeSpan _flowTimeout = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, TaskCompletionSource<HandoffResult>> _pending = new();

    protected StarlightAuthApi Api { get; } = api;

    /// <summary>Whether this provider occupies the Steam token slot on <see cref="LoginInfo"/>.</summary>
    protected abstract bool IsSteam { get; }

    /// <summary>The provider segment used in <c>starlight://auth/&lt;provider&gt;</c> deep links.</summary>
    protected abstract string ProviderSlug { get; }

    /// <summary>Human-readable provider name, used in error messages.</summary>
    protected abstract string DisplayName { get; }

    protected abstract Exception Error(string message);

    protected abstract Task<(Guid UserId, string Username)> GetUserAsync(string token, CancellationToken cancel);

    public async Task<LoggedInAccount> LoginAsync(CancellationToken cancel = default)
    {
        var (handoff, user) = await AuthorizeAsync(cancel);

        var moderation = UsernameModerator.Moderate(user.Username);
        if (!moderation.IsUsable)
        {
            throw Error(moderation.Reason
                        ?? $"Your {DisplayName} username can't be used. Please set a normal name and try again.");
        }

        var info = new LoginInfo
        {
            UserId = user.UserId,
            Username = moderation.Username,
        };

        ApplyTokens(info, handoff);

        loginManager.AddFreshLogin(info);
        loginManager.ActiveAccountId = info.UserId;

        return loginManager.ActiveAccount
               ?? throw Error("The account was signed in but could not be selected.");
    }

    public async Task AttachToAccountAsync(LoggedInAccount account, CancellationToken cancel = default)
    {
        var (handoff, user) = await AuthorizeAsync(cancel);

        if (user.UserId != account.UserId && ReadTokenUserId(GetStoredToken(account.LoginInfo)) != user.UserId)
            throw Error($"This {DisplayName} account isn't linked to this player on the server yet.");

        var info = new LoginInfo
        {
            UserId = account.UserId,
            Username = account.LoginInfo.Username,
            Token = account.LoginInfo.Token,
            AuthServerUrl = account.LoginInfo.AuthServerUrl,
        };

        ApplyTokens(info, handoff);

        loginManager.AddFreshLogin(info);
        loginManager.ActiveAccountId = info.UserId;
    }

    private async Task<(HandoffResult Handoff, (Guid UserId, string Username) User)> AuthorizeAsync(
        CancellationToken cancel)
    {
        var state = GenerateState();
        var tcs = new TaskCompletionSource<HandoffResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[state] = tcs;

        try
        {
            try
            {
                _ = Process.Start(new ProcessStartInfo
                {
                    FileName = Api.BuildLauncherLoginUrl(IsSteam, state),
                    UseShellExecute = true
                });
            }
            catch (Exception e)
            {
                Log.Warning(e, "Could not open a browser for the {Provider} login", DisplayName);
                throw Error("Unable to open the browser to log in.");
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeoutCts.CancelAfter(_flowTimeout);

            HandoffResult handoff;
            await using (timeoutCts.Token.Register(() => tcs.TrySetCanceled(timeoutCts.Token)))
                handoff = await tcs.Task;

            var user = await GetUserAsync(handoff.Token, cancel);
            return (handoff, user);
        }
        finally
        {
            _ = _pending.TryRemove(state, out _);
        }
    }

    private string? GetStoredToken(LoginInfo info)
        => (IsSteam ? info.SteamToken : info.DiscordToken)?.Token;

    private static Guid? ReadTokenUserId(string? token)
    {
        var parts = token?.Split('.');
        if (parts is not { Length: 3 })
            return null;

        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload += (payload.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };

            using var json = JsonDocument.Parse(Convert.FromBase64String(payload));
            return json.RootElement.TryGetProperty("ss14_id", out var claim) && Guid.TryParse(claim.GetString(), out var id)
                ? id
                : null;
        }
        catch (Exception e) when (e is FormatException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    protected void ApplyTokens(LoginInfo info, HandoffResult handoff)
    {
        var token = new LoginToken
        {
            Token = handoff.Token,
            ExpireTime = handoff.EffectiveExpiry,
            IssuedTime = DateTimeOffset.UtcNow,
        };

        if (IsSteam)
        {
            info.SteamToken = token;
            info.SteamRefreshToken = handoff.RefreshToken;
            info.SteamSessionId = handoff.SessionId;
        }
        else
        {
            info.DiscordToken = token;
            info.DiscordRefreshToken = handoff.RefreshToken;
            info.DiscordSessionId = handoff.SessionId;
        }
    }

    public void HandleDeepLink(Uri uri)
    {
        if (!IsForThisProvider(uri))
            return;

        var query = HttpUtility.ParseQueryString(uri.Query);
        var state = query["state"];

        if (string.IsNullOrEmpty(state) || !_pending.TryRemove(state, out var tcs))
        {
            Log.Warning("{Provider} deep link with an unknown state", DisplayName);
            return;
        }

        var error = query["error"];
        if (!string.IsNullOrEmpty(error))
        {
            _ = tcs.TrySetException(Error(MapError(error)));
            return;
        }

        var token = query["token"];
        if (string.IsNullOrEmpty(token))
        {
            _ = tcs.TrySetException(Error("No token in the response."));
            return;
        }

        var expiry = HandoffResult.ParseExpiry(query["expires"], query["expires_in"]);
        if (expiry == null)
        {
            Log.Debug(
                "{Provider} handoff did not include an expiry; assuming {Lifetime} until the first refresh",
                DisplayName, HandoffResult.AssumedLifetime);
        }

        _ = tcs.TrySetResult(new HandoffResult(token, query["refresh"], query["session"], expiry));
    }

    private string MapError(string error) => error switch
    {
        "link_required" =>
            $"Your {DisplayName} account isn't linked to your player. Link it on the website and try again.",
        _ => $"Unable to log in via {DisplayName}.",
    };

    private bool IsForThisProvider(Uri uri)
    {
        if (!uri.Scheme.Equals("starlight", StringComparison.OrdinalIgnoreCase)
            || !uri.Host.Equals("auth", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var segment = uri.Segments
            .Select(s => s.Trim('/'))
            .FirstOrDefault(s => !string.IsNullOrEmpty(s)) ?? "discord";

        return string.Equals(segment, ProviderSlug, StringComparison.OrdinalIgnoreCase);
    }

    private static string GenerateState()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes);
    }
}
