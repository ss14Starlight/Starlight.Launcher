using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MudBlazor;
using MudBlazor.Services;
using Starlight.Launcher.WebUI.Components;
using Starlight.Launcher.WebUI.Services;

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

        builder.WebHost.UseKestrel(o => o.Listen(IPAddress.Loopback, 0));

        builder.WebHost.UseStaticWebAssets();

        builder.Services.AddRazorComponents().AddInteractiveServerComponents();

        builder.Services.Configure<HostOptions>(options =>
        {
            options.ShutdownTimeout = TimeSpan.FromSeconds(2);
        });

        builder.Services.AddMudServices();

        configureServices?.Invoke(builder.Services);

        var app = builder.Build();

        var env = app.Services.GetRequiredService<IWebHostEnvironment>();

        if (!app.Environment.IsDevelopment())
            app.UseExceptionHandler("/Error");

        app.UseStaticFiles();
        app.UseAntiforgery();

        app.MapRazorComponents<App>()
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
