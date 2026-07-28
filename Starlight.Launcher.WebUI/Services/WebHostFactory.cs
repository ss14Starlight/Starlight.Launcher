using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MudBlazor.Services;
using Starlight.Launcher.WebUI.Components;

namespace Starlight.Launcher.WebUI;

public static class WebHostFactory
{
    public static WebApplication Create(Action<IServiceCollection>? configureServices = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory,
            EnvironmentName = Environments.Production,
        });

        _ = builder.WebHost.UseKestrel(o => o.Listen(IPAddress.Loopback, 0));

        _ = builder.WebHost.UseStaticWebAssets();

        _ = builder.Services.AddRazorComponents().AddInteractiveServerComponents();

        _ = builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(2));

        _ = builder.Services.AddMudServices();

        configureServices?.Invoke(builder.Services);

        var app = builder.Build();

        var env = app.Services.GetRequiredService<IWebHostEnvironment>();

        if (!app.Environment.IsDevelopment())
            _ = app.UseExceptionHandler("/Error");

        _ = app.UseStaticFiles();
        _ = app.UseAntiforgery();

        _ = app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        return app;
    }

    public static Uri GetListenAddress(WebApplication app)
    {
        var feature = app.Services
            .GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>();

        var address = feature?.Addresses.FirstOrDefault()
            ?? throw new InvalidOperationException("Kestrel didn't report a bound address.");

        return new Uri(address);
    }
}
