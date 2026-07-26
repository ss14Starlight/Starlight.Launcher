using Avalonia.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Robust.Launcher.Api.Api;
using Robust.Launcher.Api.Models.ServerStatus;
using Starlight.Launcher.Services;
using Starlight.Launcher.Services.Auth;
using Starlight.Launcher.Services.Discord;
using Starlight.Launcher.Services.ServerStatus;
using Starlight.Launcher.Services.Settings;
using Starlight.Launcher.WebUI;
using Starlight.Launcher.WebUI.Bridge;
using Starlight.Launcher.WebUI.Localization;
using Starlight.Launcher.WebUI.Services;

namespace Starlight.Launcher.Services;

public sealed class EmbeddedBlazorHost : IAsyncDisposable
{
    private WebApplication? _app;
    public Uri? Url { get; private set; }

    public async Task StartAsync(IServiceProvider nativeServices)
    {
        _app = WebHostFactory.Create(
            args: Array.Empty<string>(),
            loopbackOnlyRandomPort: true,
            configureServices: services =>
            {
                // Only re-expose what your .razor components actually @inject - trim
                services.AddSingleton(nativeServices.GetRequiredService<ILocalizationManager>());
                services.AddSingleton(nativeServices.GetRequiredService<SettingsService>());
                services.AddSingleton(nativeServices.GetRequiredService<LauncherCommands>());
                services.AddSingleton(nativeServices.GetRequiredService<LauncherMessaging>());
                services.AddSingleton(nativeServices.GetRequiredService<INativeTray>());
                services.AddSingleton(nativeServices.GetRequiredService<IFileDialogService>());
                services.AddSingleton(nativeServices.GetRequiredService<HubApi>());
                services.AddSingleton(nativeServices.GetRequiredService<AuthApi>());
                services.AddSingleton(nativeServices.GetRequiredService<ContentManager>());
                services.AddSingleton(nativeServices.GetRequiredService<StarlightAuthApi>());
                services.AddSingleton(nativeServices.GetRequiredService<DiscordAuthService>());
                services.AddSingleton(nativeServices.GetRequiredService<ServerStatusCache>());
                services.AddSingleton(nativeServices.GetRequiredService<UiTicker>());
                services.AddSingleton(nativeServices.GetRequiredService<IBridge>());
            });

        await _app.StartAsync();
        Url = WebHostFactory.GetListenAddress(_app);
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
            await _app.StopAsync();
    }
}
