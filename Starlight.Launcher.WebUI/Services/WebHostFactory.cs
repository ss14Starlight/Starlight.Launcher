using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MudBlazor;
using MudBlazor.Services;
using Starlight.Launcher.WebUI.Components;

namespace Starlight.Launcher.WebUI;

public static class WebHostFactory
{
    public static WebApplication Create(
        string[] args,
        Action<IServiceCollection>? configureServices = null,
        bool loopbackOnlyRandomPort = false)
    {
        var builder = WebApplication.CreateBuilder(args);

        if (loopbackOnlyRandomPort)
            builder.WebHost.UseUrls("http://127.0.0.1:0");

        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        builder.Services.AddMudServices();

        configureServices?.Invoke(builder.Services);

        var app = builder.Build();

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
