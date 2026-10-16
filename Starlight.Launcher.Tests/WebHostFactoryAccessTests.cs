using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Starlight.Launcher.WebUI;

namespace Starlight.Launcher.Tests;

public sealed class WebHostFactoryAccessTests : IAsyncLifetime
{
    private const string Token = "0123456789ABCDEF";
    private const string CookieName = "starlight-ui-access";

    private WebApplication _app = default!;

    public async ValueTask InitializeAsync()
    {
        _app = WebHostFactory.Create(Token, services => services.AddSingleton<IServer, TestServer>());
        await _app.StartAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    private HttpClient CreateClient(string baseAddress = "http://127.0.0.1/")
    {
        var server = (TestServer)_app.Services.GetRequiredService<IServer>();
        var client = server.CreateClient();
        client.BaseAddress = new Uri(baseAddress);
        return client;
    }

    private static HttpRequestMessage Get(string path, string? cookie = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (cookie != null)
            request.Headers.Add("Cookie", $"{CookieName}={cookie}");
        return request;
    }

    [Fact]
    public async Task NoToken_IsForbidden()
    {
        using var client = CreateClient();

        var response = await client.SendAsync(Get("/"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("WRONG")]
    [InlineData("0123456789abcdef")] // comparison is case-sensitive
    [InlineData("0123456789ABCDEF0")]
    [InlineData("")]
    public async Task WrongQueryToken_IsForbidden_AndSetsNoCookie(string token)
    {
        using var client = CreateClient();

        var response = await client.SendAsync(Get($"/?access={token}"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task ValidQueryToken_SetsCookie_AndRedirectsWithoutToken()
    {
        using var client = CreateClient();

        var response = await client.SendAsync(Get($"/settings?access={Token}"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/settings", response.Headers.Location?.OriginalString);

        var setCookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith($"{CookieName}={Token}", setCookie);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidCookie_PassesGate()
    {
        using var client = CreateClient();

        var response = await client.SendAsync(Get("/does-not-exist", Token), TestContext.Current.CancellationToken);

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task WrongCookie_IsForbidden()
    {
        using var client = CreateClient();

        var response = await client.SendAsync(Get("/", "WRONG"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("http://localhost/")]
    [InlineData("http://example.com/")]
    [InlineData("http://192.168.1.10/")]
    public async Task NonLoopbackIpHost_IsForbidden_EvenWithValidToken(string baseAddress)
    {
        using var client = CreateClient(baseAddress);

        var viaQuery = await client.SendAsync(Get($"/?access={Token}"), TestContext.Current.CancellationToken);
        var viaCookie = await client.SendAsync(Get("/", Token), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, viaQuery.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, viaCookie.StatusCode);
    }

    [Fact]
    public async Task IPv6LoopbackHost_IsAllowed()
    {
        using var client = CreateClient("http://[::1]/");

        var response = await client.SendAsync(Get($"/?access={Token}"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }
}
