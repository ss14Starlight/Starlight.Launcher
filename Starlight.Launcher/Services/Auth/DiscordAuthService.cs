using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Web;
using Robust.Launcher.Api.Models;
using Robust.Launcher.Api.Models.Data;
using Serilog;
using Starlight.Launcher.WebUI.Models.Auth;
using Starlight.Launcher.WebUI.Models.DiscordAuthService;

namespace Starlight.Launcher.Services.Auth;

public sealed class DiscordAuthService(StarlightAuthApi api, LoginManager loginManager)
{
    private static readonly TimeSpan _flowTimeout = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, TaskCompletionSource<HandoffResult>> _pending = new();

    private async Task<(HandoffResult handoff, DiscordUserResponse info)> AuthorizeAsync(CancellationToken cancel)
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
                    FileName = api.BuildLauncherLoginUrl(false, state).ToString(),
                    UseShellExecute = true
                });
            }
            catch
            {
                throw new DiscordAuthException("Unable to open the browser to log in.");
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeoutCts.CancelAfter(_flowTimeout);

            HandoffResult handoff;
            await using (timeoutCts.Token.Register(() => tcs.TrySetCanceled(timeoutCts.Token)))
                handoff = await tcs.Task;

            var info = await api.GetDiscordUserAsync(handoff.Token, cancel)
                       ?? throw new DiscordAuthException("Failed to retrieve user information.");
            return (handoff, info);
        }
        finally
        {
            _ = _pending.TryRemove(state, out _);
        }
    }

    public async Task<LoggedInAccount> LoginAsync(CancellationToken cancel = default)
    {
        var (handoff, info) = await AuthorizeAsync(cancel);

        var moderation = UsernameModerator.Moderate(info.Username);
        if (!moderation.IsUsable)
            throw new DiscordAuthException(
                moderation.Reason ?? "Your Discord username can't be used. Please set a normal name and try again.");

        var newLoginInfo = new LoginInfo
        {
            UserId = info.UserId,
            Username = moderation.Username,
            Token = null,
            DiscordToken = new LoginToken { Token = handoff.Token, ExpireTime = DateTime.UtcNow.AddDays(2) },
            DiscordRefreshToken = handoff.RefreshToken,
            DiscordSessionId = handoff.SessionId,
        };
        loginManager.AddFreshLogin(newLoginInfo);
        loginManager.ActiveAccountId = newLoginInfo.UserId;
        return loginManager.ActiveAccount!;
    }

    public async Task AttachToAccountAsync(LoggedInAccount account, CancellationToken cancel = default)
    {
        var (handoff, info) = await AuthorizeAsync(cancel);

        if (info.UserId != account.UserId)
            throw new DiscordAuthException(
                "This Discord account isn't linked to this player on the server yet.");

        var newLoginInfo = new LoginInfo
        {
            UserId = info.UserId,
            Username = account.LoginInfo.Username,
            Token = account.LoginInfo.Token,
            DiscordToken = new LoginToken { Token = handoff.Token, ExpireTime = DateTime.UtcNow.AddDays(2) },
            DiscordRefreshToken = handoff.RefreshToken,
            DiscordSessionId = handoff.SessionId,
            AuthServerUrl = account.LoginInfo.AuthServerUrl
        };
        loginManager.AddFreshLogin(newLoginInfo);
        loginManager.ActiveAccountId = newLoginInfo.UserId;
    }

    public void HandleDeepLink(Uri uri)
    {
        if (!uri.Scheme.Equals("starlight", StringComparison.OrdinalIgnoreCase) ||
            !uri.Host.Equals("auth", StringComparison.OrdinalIgnoreCase))
            return;

        var query = HttpUtility.ParseQueryString(uri.Query);
        var state = query["state"];

        if (string.IsNullOrEmpty(state) || !_pending.TryRemove(state, out var tcs))
        {
            Log.Warning("Discord deep link with an unknown state");
            return;
        }

        var error = query["error"];
        if (!string.IsNullOrEmpty(error))
        {
            _ = tcs.TrySetException(new DiscordAuthException(MapError(error)));
            return;
        }

        var token = query["token"];
        if (string.IsNullOrEmpty(token))
        {
            _ = tcs.TrySetException(new DiscordAuthException("No token in the response."));
            return;
        }

        _ = tcs.TrySetResult(new HandoffResult(token, query["refresh"], query["session"]));
    }

    private static string MapError(string error) => error switch
    {
        "link_required" => "Your Discord account isn't linked to your player. Link it on the website and try again.",
        _ => "Unable to log in via Discord.",
    };

    private static string GenerateState()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes);
    }
}
