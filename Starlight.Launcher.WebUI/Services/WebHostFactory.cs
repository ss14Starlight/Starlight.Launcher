using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MudBlazor;
using MudBlazor.Services;
using Starlight.Launcher.WebUI.Components;

namespace Starlight.Launcher.WebUI.Services;

public static class WebHostFactory
{
    public static readonly TimeSpan IdleConnectionTimeout = TimeSpan.FromDays(7);

    private const string AccessQueryKey = "access";
    private const string AccessCookieName = "starlight-ui-access";

    public static WebApplication Create(string accessToken, Action<IServiceCollection>? configureServices = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory,
            EnvironmentName = Environments.Production,
        });

        _ = builder.WebHost.UseKestrel(o => o.Listen(IPAddress.Loopback, 0));

        _ = builder.WebHost.UseStaticWebAssets();

        _ = builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents(o => o.DisconnectedCircuitRetentionPeriod = IdleConnectionTimeout)
            .AddHubOptions(o => o.ClientTimeoutInterval = IdleConnectionTimeout);

        _ = builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(2));

        _ = builder.Services.AddMudServices();
        _ = builder.Services.AddMudMarkdownServices();

        _ = builder.Logging.ClearProviders();

        configureServices?.Invoke(builder.Services);

        var app = builder.Build();

        _ = app.Use(async (context, next) =>
        {
            if (!IPAddress.TryParse(context.Request.Host.Host, out var hostIp) || !IPAddress.IsLoopback(hostIp))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            if (context.Request.Query.TryGetValue(AccessQueryKey, out var queryToken)
                && TokenEquals(queryToken.ToString(), accessToken))
            {
                context.Response.Cookies.Append(AccessCookieName, accessToken, new CookieOptions
                {
                    HttpOnly = true,
                    SameSite = SameSiteMode.Strict,
                    Path = "/",
                    IsEssential = true,
                });
                context.Response.Redirect(context.Request.PathBase + context.Request.Path);
                return;
            }

            if (context.Request.Cookies.TryGetValue(AccessCookieName, out var cookieToken)
                && TokenEquals(cookieToken, accessToken))
            {
                await next(context);
                return;
            }

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
        });

        var env = app.Services.GetRequiredService<IWebHostEnvironment>();

        if (!app.Environment.IsDevelopment())
            _ = app.UseExceptionHandler("/Error");

        _ = app.UseStaticFiles();
        _ = app.UseAntiforgery();

        _ = app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        return app;
    }

    public static string GenerateAccessToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    public static Uri GetEntryUrl(WebApplication app, string accessToken)
        => new UriBuilder(GetListenAddress(app)) { Query = $"{AccessQueryKey}={accessToken}" }.Uri;

    private static bool TokenEquals(string? candidate, string expected)
        => candidate is not null
            && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(candidate), Encoding.UTF8.GetBytes(expected));

    public static Uri GetListenAddress(WebApplication app)
    {
        var feature = app.Services
            .GetRequiredService<IServer>()
            .Features
            .Get<IServerAddressesFeature>();

        var address = feature?.Addresses.FirstOrDefault()
            ?? throw new InvalidOperationException("Kestrel didn't report a bound address.");

        return new Uri(address);
    }
}
