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

    public override async void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = new ServiceCollection();
            RegisterServices(services);
            Services = services.BuildServiceProvider();

            _blazorHost = new EmbeddedBlazorHost();
            await _blazorHost.StartAsync(Services);

            await Services.GetRequiredService<ILocalizationManager>().Initialize();
            if (OperatingSystem.IsWindows())
                Services.GetRequiredService<DiscordRichPresence>().Initialize();

            var settings = Services.GetRequiredService<SettingsService>();
            _ = Task.Run(async () => await settings.InitializeLoginsAsync());

            Services.GetRequiredService<HubServerFetcher>().RequestInitialUpdate();
            _ = Task.Run(async () => Services.GetRequiredService<LoginManager>().InitializeAsync());
            Services.GetRequiredService<ContentManager>().Initialize();
            Services.GetRequiredService<TrayCoordinator>().Initialize();

            var commands = Services.GetRequiredService<LauncherCommands>();
            var messaging = Services.GetRequiredService<LauncherMessaging>();
            commands.RunCommandTask();
            messaging.StartServerTask(commands);

            var window = new MainWindow(_blazorHost.Url) { Title = "Starlight.Launcher" };

            var flushing = false;

            desktop.ShutdownRequested += async (_, e) =>
            {
                if (flushing) return;
                flushing = true;

                e.Cancel = true;

                commands.Shutdown();
                messaging.StopAndWait();
                await _blazorHost.DisposeAsync();

                await settings.FlushPendingSavesAsync();
                desktop.Shutdown();
            };

            window.Closing += (_ , e) =>
            {
                if (settings.GetSettings().CollapseInTrayOnClose)
                {
                    e.Cancel = true;
                    Avalonia.Threading.Dispatcher.UIThread.Post(Services.GetRequiredService<INativeTray>().HideWindow);
                }
            };

            window.PropertyChanged += (_, e) =>
            {
                if (settings.GetSettings().CollapseInTrayOnMinimize && e.Property == Window.WindowStateProperty && window.WindowState == WindowState.Minimized)
                    Avalonia.Threading.Dispatcher.UIThread.Post(Services.GetRequiredService<INativeTray>().HideWindow);
            };

            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void RegisterServices(IServiceCollection services)
    {
        _ = services.AddLogging(b => b.ClearProviders().AddSerilog(Serilog.Log.Logger));

        if (OperatingSystem.IsWindows())
            _ = services.AddSingleton<ILoginKeyProvider, DpapiKeyProvider>();
        else
            _ = services.AddSingleton<ILoginKeyProvider, FileKeyProvider>();

        _ = services.AddSingleton<INativeTray, AvaloniaTray>();
        _ = services.AddSingleton<IFileDialogService, AvaloniaFileDialogService>();

        _ = services.AddSingleton<SettingsService>();
        _ = services.AddSingleton<DiscordRichPresence>();
        _ = services.AddSingleton<TrayCoordinator>();
        _ = services.AddSingleton(PendingMessaging ?? new LauncherMessaging());
        _ = services.AddSingleton<LauncherCommands>();

        var httpClient = HappyEyeballsHttp.CreateHttpClient();
        _ = services.AddSingleton(httpClient);
        _ = services.AddSingleton<ILocalizationManager, LocalizationManager>();
        _ = services.AddSingleton<HubApi>();
        _ = services.AddSingleton<AuthApi>();
        _ = services.AddSingleton<HubServerFetcher>();
        _ = services.AddSingleton(sp =>
        {
            var fetcher = sp.GetRequiredService<HubServerFetcher>();
            return new ServerInfoLoader();
        });
        _ = services.AddSingleton<ServerStatusCache>();
        _ = services.AddSingleton<ContentManager>();
        _ = services.AddSingleton<IEngineManager, EngineManagerDynamic>();
        _ = services.AddSingleton<Updater>();
        _ = services.AddSingleton<LoginManager>();
        _ = services.AddSingleton<StarlightAuthApi>();
        _ = services.AddSingleton<DiscordAuthService>();
        _ = services.AddTransient<Connector>();
        _ = services.AddSingleton<UiTicker>();
        _ = services.AddSingleton<LauncherUpdater>();
        _ = services.AddSingleton<IBridge, Bridge>();
        _ = services.AddSingleton<AppState>(); // Service for UI refresh.
    }
}
