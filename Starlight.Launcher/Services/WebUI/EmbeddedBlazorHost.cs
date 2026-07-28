using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Robust.Launcher.Api.Api;
using Robust.Launcher.Api.Models.ServerStatus;
using Starlight.Launcher.Services.Auth;
using Starlight.Launcher.Services.Settings;
using Starlight.Launcher.WebUI;
using Starlight.Launcher.WebUI.Bridge;
using Starlight.Launcher.WebUI.Localization;
using Starlight.Launcher.WebUI.Services;

namespace Starlight.Launcher.Services;

/// <summary>
/// Hosts the embedded Blazor web application.
/// </summary>
public sealed class EmbeddedBlazorHost : IAsyncDisposable
{
    private WebApplication? _app;

    /// <summary>
    /// Target url for embedded blazor host
    /// </summary>
    public Uri? Url { get; private set; }

    /// <summary>
    /// Starts the embedded Blazor host.
    /// </summary>
    public async Task StartAsync(IServiceProvider nativeServices)
    {
        _app = WebHostFactory.Create(
            configureServices: services =>
            {
                // Only re-expose what your .razor components actually @inject - trim
                _ = services.AddSingleton(nativeServices.GetRequiredService<ILocalizationManager>());
                _ = services.AddSingleton(nativeServices.GetRequiredService<SettingsService>());
                _ = services.AddSingleton(nativeServices.GetRequiredService<LauncherCommands>());
                _ = services.AddSingleton(nativeServices.GetRequiredService<LauncherMessaging>());
                _ = services.AddSingleton(nativeServices.GetRequiredService<INativeTray>());
                _ = services.AddSingleton(nativeServices.GetRequiredService<IFileDialogService>());
                _ = services.AddSingleton(nativeServices.GetRequiredService<HubApi>());
                _ = services.AddSingleton(nativeServices.GetRequiredService<AuthApi>());
                _ = services.AddSingleton(nativeServices.GetRequiredService<ContentManager>());
                _ = services.AddSingleton(nativeServices.GetRequiredService<StarlightAuthApi>());
                _ = services.AddSingleton(nativeServices.GetRequiredService<DiscordAuthService>());
                _ = services.AddSingleton(nativeServices.GetRequiredService<ServerStatusCache>());
                _ = services.AddSingleton(nativeServices.GetRequiredService<UiTicker>());
                _ = services.AddSingleton(nativeServices.GetRequiredService<IBridge>());
                _ = services.AddSingleton(nativeServices.GetRequiredService<HttpClient>());
                _ = services.AddSingleton(nativeServices.GetRequiredService<AppState>());
            });

        await _app.StartAsync();
        Url = WebHostFactory.GetListenAddress(_app);
    }

    /// <summary>
    /// Stops the embedded Blazor host and releases its resources.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
            await _app.StopAsync();
    }
}
