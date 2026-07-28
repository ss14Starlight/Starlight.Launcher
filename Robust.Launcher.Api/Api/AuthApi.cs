using System;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Robust.Launcher.Api.Models;
using Robust.Launcher.Api.Models.Data;
using Robust.Launcher.Api.Utility;

namespace Robust.Launcher.Api.Api;

/// <summary>
/// Provides methods for interacting with the authentication API.
/// </summary>
public sealed class AuthApi
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AuthApi> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthApi"/> class.
    /// </summary>
    public AuthApi(HttpClient http, ILogger<AuthApi> logger)
    {
        _httpClient = http;
        _logger = logger;
    }

    /// <summary>
    /// Authenticates a user.
    /// </summary>
    public async Task<AuthenticateResult> AuthenticateAsync(AuthenticateRequest request, UrlFallbackSet authSet)
    {
        try
        {
            var authUrl = authSet + "api/auth/authenticate";

            using var resp = await _httpClient.PostAsJsonAsync(authUrl, request);

            if (resp.IsSuccessStatusCode)
            {
                var respJson = await resp.Content.AsJson<AuthenticateResponse>();
                var token = new LoginToken() {  Token = respJson.Token, ExpireTime = respJson.ExpireTime };
                return new AuthenticateResult(new LoginInfo
                {
                    UserId = respJson.UserId,
                    Token = token,
                    Username = respJson.Username,
                    AuthServerUrl = authSet.GetMostSuccessfulUrl(),
                });
            }

            if (resp.StatusCode == HttpStatusCode.Unauthorized)
            {
                // Login failure.
                var respJson = await resp.Content.AsJson<AuthenticateDenyResponse>();
                return new AuthenticateResult(respJson.Errors, respJson.Code);
            }

            _logger.LogError("Server returned unexpected HTTP status code: {responseCode}", resp.StatusCode);
#if DEBUG
            _logger.LogDebug("Response for error:\n{response}\n{content}", resp, await resp.Content.ReadAsStringAsync());
#endif
            // Unknown error? uh oh.
            return new AuthenticateResult(
                ["Server returned unknown error"],
                AuthenticateDenyResponseCode.UnknownError);
        }
        catch (JsonException e)
        {
            _logger.LogError(e, "JsonException in AuthenticateAsync");
            return new AuthenticateResult(
                ["Server sent invalid response"],
                AuthenticateDenyResponseCode.UnknownError);
        }
        catch (HttpRequestException httpE)
        {
            _logger.LogError(httpE, "HttpRequestException in AuthenticateAsync");
            HttpSelfTest.StartSelfTest();
            return new AuthenticateResult(
                [$"Connection error to authentication server: {httpE.Message}"],
                AuthenticateDenyResponseCode.UnknownError);
        }
    }

    /// <summary>
    /// Registers a new user account.
    /// </summary>
    public async Task<RegisterResult> RegisterAsync(string username, string email, string password, UrlFallbackSet authSet)
    {
        try
        {
            var request = new RegisterRequest(username, email, password);

            var authUrl = authSet + "api/auth/register";

            using var resp = await _httpClient.PostAsJsonAsync(authUrl, request);

            if (resp.IsSuccessStatusCode)
            {
                var respJson = await resp.Content.AsJson<RegisterResponse>();
                return new RegisterResult(respJson.Status);
            }

            if (resp.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                // Register failure.
                var respJson = await resp.Content.AsJson<RegisterResponseError>();
                return new RegisterResult(respJson.Errors);
            }

            _logger.LogError("Server returned unexpected HTTP status code: {responseCode}", resp.StatusCode);
#if DEBUG
            _logger.LogDebug("Response for error:\n{response}\n{content}", resp, await resp.Content.ReadAsStringAsync());
#endif
            // Unknown error? uh oh.
            return new RegisterResult(["Server returned unknown error"]);
        }
        catch (JsonException e)
        {
            _logger.LogError(e, "JsonException in RegisterAsync");
            return new RegisterResult(["Server sent invalid response"]);
        }
        catch (HttpRequestException httpE)
        {
            _logger.LogError(httpE, "HttpRequestException in RegisterAsync");
            HttpSelfTest.StartSelfTest();
            return new RegisterResult([$"Connection error to authentication server: {httpE.Message}"]);
        }
    }

    /// <summary>
    /// Requests a password reset email.
    /// </summary>
    public async Task<string[]?> ForgotPasswordAsync(string email, UrlFallbackSet authSet)
    {
        try
        {
            var request = new ResetPasswordRequest(email);

            var authUrl = authSet + "api/auth/resetPassword";

            using var resp = await _httpClient.PostAsJsonAsync(authUrl, request);

            if (resp.IsSuccessStatusCode)
            {
                return null;
            }

            // Unknown error? uh oh.
            _logger.LogError("Server returned unexpected HTTP status code: {responseCode}", resp.StatusCode);
#if DEBUG
            _logger.LogDebug("Response for error:\n{response}\n{content}", resp, await resp.Content.ReadAsStringAsync());
#endif
            return ["Server returned unknown error"];
        }
        catch (HttpRequestException httpE)
        {
            _logger.LogError(httpE, "HttpRequestException in ForgotPasswordAsync");
            return new[] { $"Connection error to authentication server: {httpE.Message}" };
        }
    }

    /// <summary>
    /// Requests a new account confirmation email.
    /// </summary>
    public async Task<string[]?> ResendConfirmationAsync(string email, UrlFallbackSet authSet)
    {
        try
        {
            var request = new ResendConfirmationRequest(email);

            var authUrl = authSet + "api/auth/resendConfirmation";

            using var resp = await _httpClient.PostAsJsonAsync(authUrl, request);

            if (resp.IsSuccessStatusCode)
            {
                return null;
            }

            // Unknown error? uh oh.
            _logger.LogError("Server returned unexpected HTTP status code: {responseCode}", resp.StatusCode);
#if DEBUG
            _logger.LogDebug("Response for error:\n{response}\n{content}", resp, await resp.Content.ReadAsStringAsync());
#endif
            return ["Server returned unknown error"];
        }
        catch (HttpRequestException httpE)
        {
            _logger.LogError(httpE, "HttpRequestException in ResendConfirmationAsync");
            HttpSelfTest.StartSelfTest();
            return [$"Connection error to authentication server: {httpE.Message}"];
        }
    }

    /// <summary>
    /// Refreshes an authentication token.
    /// </summary>
    public async Task<LoginToken?> RefreshTokenAsync(string token, UrlFallbackSet authSet)
    {
        try
        {
            var request = new RefreshRequest(token);

            var authUrl = authSet + "api/auth/refresh";

            using var resp = await _httpClient.PostAsJsonAsync(authUrl, request);

            if (resp.IsSuccessStatusCode)
            {
                var response = await resp.Content.AsJson<RefreshResponse>();

                return new LoginToken() { Token = response.NewToken, ExpireTime = response.ExpireTime };
            }

            if (resp.StatusCode == HttpStatusCode.Unauthorized)
            {
                _logger.LogWarning("Got unauthorized while trying to refresh token. Guess it expired.");

                return null;
            }

            // Unknown error? uh oh.
            _logger.LogError("Server returned unexpected HTTP status code: {responseCode}", resp.StatusCode);
#if DEBUG
            _logger.LogDebug("Response for error:\n{response}\n{content}", resp, await resp.Content.ReadAsStringAsync());
#endif

            throw new AuthApiException($"Server returned unexpected HTTP status code: {resp.StatusCode}");
        }
        catch (HttpRequestException httpE)
        {
            _logger.LogError(httpE, "HttpRequestException in ResendConfirmationAsync");
            HttpSelfTest.StartSelfTest();
            throw new AuthApiException("HttpRequestException thrown", httpE);
        }
        catch (JsonException jsonE)
        {
            _logger.LogError(jsonE, "JsonException in ResendConfirmationAsync");
            throw new AuthApiException("JsonException thrown", jsonE);
        }
    }

    /// <summary>
    /// Logs out the specified authentication token.
    /// </summary>
    public async Task LogoutTokenAsync(string token, UrlFallbackSet authSet)
    {
        try
        {
            var request = new LogoutRequest(token);

            var authUrl = authSet + "api/auth/logout";

            using var resp = await _httpClient.PostAsJsonAsync(authUrl, request);

            if (resp.IsSuccessStatusCode)
            {
                return;
            }

            // Unknown error? uh oh.
            _logger.LogError("Server returned unexpected HTTP status code: {responseCode}", resp.StatusCode);
#if DEBUG
            _logger.LogDebug("Response for error:\n{response}\n{content}", resp, await resp.Content.ReadAsStringAsync());
#endif
        }
        catch (HttpRequestException httpE)
        {
            // Does it make sense to just... swallow this exception? The token will stay "active" until it expires.
            _logger.LogError(httpE, "HttpRequestException in LogoutTokenAsync");
            HttpSelfTest.StartSelfTest();
        }
    }

    /// <summary>
    /// Checks whether an authentication token is still valid.
    /// </summary>
    public async Task<bool> CheckTokenAsync(string token, UrlFallbackSet authSet)
    {
        try
        {
            var authUrl = authSet + "api/auth/ping";

            using var resp = await authUrl.SendAsync(_httpClient, url =>
            {
                var requestMessage = new HttpRequestMessage(HttpMethod.Get, url);
                requestMessage.Headers.Authorization = new AuthenticationHeaderValue("SS14Auth", token);
                return requestMessage;
            });

            if (resp.IsSuccessStatusCode)
            {
                return true;
            }

            if (resp.StatusCode == HttpStatusCode.Unauthorized)
            {
                return false;
            }

            // Unknown error? uh oh.
            _logger.LogError("Server returned unexpected HTTP status code: {responseCode}", resp.StatusCode);
            _logger.LogDebug("Response for error:\n{response}\n{content}", resp, await resp.Content.ReadAsStringAsync());
            throw new AuthApiException($"Server returned unexpected HTTP status code: {resp.StatusCode}");
        }
        catch (HttpRequestException httpE)
        {
            // Does it make sense to just... swallow this exception? The token will stay "active" until it expires.
            _logger.LogError(httpE, "HttpRequestException in CheckTokenAsync");
            HttpSelfTest.StartSelfTest();
            throw new AuthApiException("HttpRequestException thrown", httpE);
        }
    }

    /// <summary>
    /// Represents an authentication request.
    /// </summary>
    public sealed record AuthenticateRequest(string? Username, Guid? UserId, string Password, string? TfaCode = null)
    {
        /// <summary>
        /// Initializes an authentication request using a username.
        /// </summary>
        public AuthenticateRequest(string username, string password) : this(username, null, password)
        {

        }

        /// <summary>
        /// Initializes an authentication request using a user ID.
        /// </summary>
        public AuthenticateRequest(Guid userId, string password) : this(null, userId, password)
        {

        }
    }

    /// <summary>
    /// Represents a successful authentication response.
    /// </summary>
    public sealed record AuthenticateResponse(string Token, string Username, Guid UserId, DateTimeOffset ExpireTime);

    /// <summary>
    /// Represents a failed authentication response.
    /// </summary>
    public sealed record AuthenticateDenyResponse(string[] Errors, AuthenticateDenyResponseCode Code);

    /// <summary>
    /// Specifies the reason an authentication request was denied.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AuthenticateDenyResponseCode
    {
        /// <summary>
        /// No response
        /// </summary>
        None = 0,
        /// <summary>
        /// Invalid credentials
        /// </summary>
        InvalidCredentials = 1,
        /// <summary>
        /// Account needs confirmation via email
        /// </summary>
        AccountUnconfirmed = 2,
        /// <summary>
        /// Account needs 2fa
        /// </summary>
        TfaRequired = 3,
        /// <summary>
        /// Invalid 2fa
        /// </summary>
        TfaInvalid = 4,
        /// <summary>
        /// Account banned
        /// </summary>
        AccountLocked = 5,
        /// <summary>
        /// Unknown
        /// </summary>
        UnknownError = -1,
    }

    /// <summary>
    /// Represents a user registration request.
    /// </summary>
    public sealed record RegisterRequest(string Username, string Email, string Password);

    /// <summary>
    /// Represents a successful registration response.
    /// </summary>
    public sealed record RegisterResponse(RegisterResponseStatus Status);

    /// <summary>
    /// Represents a failed registration response.
    /// </summary>
    public sealed record RegisterResponseError(string[] Errors);

    /// <summary>
    /// Represents a password reset request.
    /// </summary>
    public sealed record ResetPasswordRequest(string Email);

    /// <summary>
    /// Represents an account confirmation request.
    /// </summary>
    public sealed record ResendConfirmationRequest(string Email);

    /// <summary>
    /// Represents a logout request.
    /// </summary>
    public sealed record LogoutRequest(string Token);

    /// <summary>
    /// Represents a token refresh request.
    /// </summary>
    public sealed record RefreshRequest(string Token);

    /// <summary>
    /// Represents a token refresh response.
    /// </summary>
    public sealed record RefreshResponse(DateTimeOffset ExpireTime, string NewToken);
}

/// <summary>
/// Represents the result of an authentication request.
/// </summary>
public readonly struct AuthenticateResult
{
    private readonly LoginInfo? _loginInfo;

    /// <summary>
    /// Gets the authentication failure code.
    /// </summary>
    public AuthApi.AuthenticateDenyResponseCode Code { get; }

    /// <summary>
    /// Initializes a successful authentication result.
    /// </summary>
    public AuthenticateResult(LoginInfo loginInfo)
    {
        _loginInfo = loginInfo;
        Errors = null;
        Code = default;
    }

    /// <summary>
    /// Initializes a failed authentication result.
    /// </summary>
    public AuthenticateResult(string[] errors, AuthApi.AuthenticateDenyResponseCode code)
    {
        _loginInfo = null;
        Errors = errors;
        Code = code;
    }

    /// <summary>
    /// Gets a value indicating whether the authentication succeeded.
    /// </summary>
    public bool IsSuccess => _loginInfo != null;

    /// <summary>
    /// Gets the authenticated login information.
    /// </summary>
    public LoginInfo LoginInfo => _loginInfo ?? throw new InvalidOperationException("This AuthenticateResult is not a success.");

    /// <summary>
    /// Gets the authentication errors.
    /// </summary>
    [AllowNull]
    public string[] Errors => field ?? throw new InvalidOperationException("This AuthenticateResult is not a failure.");
}

/// <summary>
/// Represents the result of a registration request.
/// </summary>
public readonly struct RegisterResult
{
    private readonly RegisterResponseStatus? _status;

    /// <summary>
    /// Initializes a successful registration result.
    /// </summary>
    public RegisterResult(RegisterResponseStatus status)
    {
        _status = status;
        Errors = null;
    }

    /// <summary>
    /// Initializes a failed registration result.
    /// </summary>
    public RegisterResult(string[] errors)
    {
        _status = null;
        Errors = errors;
    }

    /// <summary>
    /// Gets a value indicating whether the registration succeeded.
    /// </summary>
    public bool IsSuccess => _status != null;

    /// <summary>
    /// Gets the registration status.
    /// </summary>
    public RegisterResponseStatus Status => _status ?? throw new InvalidOperationException("This RegisterResult is not a success.");

    /// <summary>
    /// Gets the registration errors.
    /// </summary>
    [AllowNull]
    public string[] Errors => field ?? throw new InvalidOperationException("This RegisterResult is not a failure.");
}

/// <summary>
/// Specifies the result of a registration request.
/// </summary>
public enum RegisterResponseStatus
{
    /// <summary>
    /// Success
    /// </summary>
    Registered,
    /// <summary>
    /// Needs confirmation via email
    /// </summary>
    RegisteredNeedConfirmation
}

/// <summary>
/// Represents an error that occurs while communicating with the authentication API.
/// </summary>
[Serializable]
public class AuthApiException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AuthApiException"/> class.
    /// </summary>
    public AuthApiException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthApiException"/> class with the specified error message.
    /// </summary>
    public AuthApiException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthApiException"/> class with the specified error message and inner exception.
    /// </summary>
    public AuthApiException(string message, Exception inner) : base(message, inner)
    {
    }
}
