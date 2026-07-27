using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Robust.Launcher.Api.Api;
using Robust.Launcher.Api.Models.ServerStatus;
using Robust.Launcher.Api.Utility;
using Serilog;
using Starlight.Launcher.Services;
using Starlight.Launcher.Services.Auth;
using Starlight.Launcher.Services.Bridge;
using Starlight.Launcher.Services.Discord;
using Starlight.Launcher.Services.EngineManager;
using Starlight.Launcher.Services.Localization;
using Starlight.Launcher.Services.ServerStatus;
using Starlight.Launcher.Services.Settings;
using Starlight.Launcher.WebUI.Bridge;
using Starlight.Launcher.WebUI.Localization;
using Starlight.Launcher.WebUI.Services;

namespace Starlight.Launcher;

public partial class App : Application
{
    internal static LauncherMessaging? PendingMessaging { get; set; }

    public static IServiceProvider Services { get; private set; } = default!;

    private EmbeddedBlazorHost? _blazorHost;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = new ServiceCollection();
            RegisterServices(services);
            Services = services.BuildServiceProvider();

            _blazorHost = new EmbeddedBlazorHost();
            _blazorHost.StartAsync(Services).GetAwaiter().GetResult();

            Services.GetRequiredService<ILocalizationManager>().Initialize();
            if (OperatingSystem.IsWindows())
                Services.GetRequiredService<DiscordRichPresence>().Initialize();
            Services.GetRequiredService<HubServerFetcher>().RequestInitialUpdate();
            Services.GetRequiredService<LoginManager>().Initialize();
            Services.GetRequiredService<ContentManager>().Initialize();
            Services.GetRequiredService<TrayCoordinator>().Initialize();

            var commands = Services.GetRequiredService<LauncherCommands>();
            var messaging = Services.GetRequiredService<LauncherMessaging>();
            commands.RunCommandTask();
            messaging.StartServerTask(commands);

            var window = new MainWindow(_blazorHost.Url) { Title = "Starlight.Launcher" };

            window.Closing += async (_, _) =>
            {
                commands.Shutdown();
                messaging.StopAndWait();
                await _blazorHost.DisposeAsync();
            };

            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void RegisterServices(IServiceCollection services)
    {
        services.AddLogging(b => b.ClearProviders().AddSerilog(Serilog.Log.Logger));

        if (OperatingSystem.IsWindows())
            services.AddSingleton<ILoginKeyProvider, DpapiKeyProvider>();
        else
            services.AddSingleton<ILoginKeyProvider, FileKeyProvider>();

        services.AddSingleton<INativeTray, AvaloniaTray>();
        services.AddSingleton<IFileDialogService, AvaloniaFileDialogService>();

        services.AddSingleton<SettingsService>();
        services.AddSingleton<DiscordRichPresence>();
        services.AddSingleton<TrayCoordinator>();
        services.AddSingleton(PendingMessaging ?? new LauncherMessaging());
        services.AddSingleton<LauncherCommands>();

        var httpClient = HappyEyeballsHttp.CreateHttpClient();
        services.AddSingleton(httpClient);
        services.AddSingleton<ILocalizationManager, LocalizationManager>();
        services.AddSingleton<HubApi>();
        services.AddSingleton<AuthApi>();
        services.AddSingleton<HubServerFetcher>();
        services.AddSingleton(sp =>
        {
            var fetcher = sp.GetRequiredService<HubServerFetcher>();
            return new ServerInfoLoader(fetcher.UpdateInfoForAsync);
        });
        services.AddSingleton<ServerStatusCache>();
        services.AddSingleton<ContentManager>();
        services.AddSingleton<IEngineManager, EngineManagerDynamic>();
        services.AddSingleton<Updater>();
        services.AddSingleton<LoginManager>();
        services.AddSingleton<StarlightAuthApi>();
        services.AddSingleton<DiscordAuthService>();
        services.AddTransient<Connector>();
        services.AddSingleton<UiTicker>();
        services.AddSingleton<LauncherUpdater>();
        services.AddSingleton<IBridge, Bridge>();
        services.AddSingleton<AppState>(); // Service for UI refresh.
    }
}
