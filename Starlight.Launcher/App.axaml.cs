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
using Starlight.Launcher.Models.EngineManager;
using Starlight.Launcher.Services;
using Starlight.Launcher.Services.Auth;
using Starlight.Launcher.Services.Bridge;
using Starlight.Launcher.Services.Discord;
using Starlight.Launcher.Services.EngineManager;
using Starlight.Launcher.Services.LocalServer;
using Starlight.Launcher.Services.Localization;
using Starlight.Launcher.Services.Logging;
using Starlight.Launcher.Services.ServerStatus;
using Starlight.Launcher.Services.Settings;
using Starlight.Launcher.WebUI.Bridge;
using Starlight.Launcher.WebUI.Localization;
using Starlight.Launcher.WebUI.Services;
using Starlight.Launcher.Platforms.Windows.Services.Settings;
using Starlight.Launcher.Services.ContentManagement;
using Starlight.Launcher.Services.DeepLink;
using Starlight.Launcher.Services.GameLaunch;
using Starlight.Launcher.Services.Tray;
using Starlight.Launcher.Services.WebUI;

namespace Starlight.Launcher;

public partial class App : Application
{
    internal static LauncherMessaging? PendingMessaging { get; set; }

    public static IServiceProvider Services { get; private set; } = default!;

    private EmbeddedBlazorHost? _blazorHost;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override async void OnFrameworkInitializationCompleted()
    {
        GlobalExceptionHandlers.InstallDispatcher();

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
            _ = Task.Run(async () => await Services.GetRequiredService<LoginManager>().InitializeAsync());
            try
            {
                Services.GetRequiredService<ContentManager>().Initialize();
            }
            catch (Exception e) when (DataDirectoryAccess.IsAccessDenied(e))
            {
                // The UI warns about the unwritable data folder; updates will report the access error themselves.
                Log.Error(e, "Can't initialize content DB: no access to the data folder");
            }

            Services.GetRequiredService<TrayCoordinator>().Initialize();

            var commands = Services.GetRequiredService<LauncherCommands>();
            var messaging = Services.GetRequiredService<LauncherMessaging>();
            commands.RunCommandTask();
            messaging.StartServerTask(commands);

            // WebView2 can't start in a folder it can't write to, which would leave the window blank
            // and hide the warning about it. An empty path falls back to the default WebView data folder.
            var dataDir = settings.GetSettings().DirLauncherData;
            var webViewDataDir = DataDirectoryAccess.CanWrite(dataDir) ? dataDir : "";

            var window = new MainWindow(_blazorHost.Url, webViewDataDir, Services.GetRequiredService<UiActivity>()) { Title = "Starlight.Launcher" };

            var flushing = false;

            desktop.ShutdownRequested += async (_, e) =>
            {
                // Keep cancelling while the flush below is running, otherwise repeated close requests
                // tear the app down underneath it.
                e.Cancel = true;
                if (flushing) return;
                flushing = true;

                var sw = System.Diagnostics.Stopwatch.StartNew();
                Log.Information("Shutdown: starting");

                async Task Step(string name, Func<Task> action, TimeSpan timeout)
                {
                    try
                    {
                        await action().WaitAsync(timeout);
                        Log.Information("Shutdown: {Step} done at {Elapsed}", name, sw.Elapsed);
                    }
                    catch (TimeoutException)
                    {
                        Log.Warning("Shutdown: {Step} timed out after {Timeout}, skipping", name, timeout);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Shutdown: {Step} failed", name);
                    }
                }

                try
                {
                    await Step("local server stop",
                        () => Services.GetRequiredService<LocalServerManager>().StopAndWaitAsync(),
                        TimeSpan.FromSeconds(10));

                    await Step("commands stop", () =>
                    {
                        commands.Shutdown();
                        return Task.CompletedTask;
                    }, TimeSpan.FromSeconds(2));

                    await Step("IPC messaging stop", () => Task.Run(messaging.StopAndWait), TimeSpan.FromSeconds(3));

                    await Step("Blazor host dispose",
                        () => Task.Run(async () => await _blazorHost.DisposeAsync()),
                        TimeSpan.FromSeconds(5));

                    await Step("settings flush", settings.FlushPendingSavesAsync, TimeSpan.FromSeconds(5));
                }
                finally
                {
                    desktop.Shutdown();
                    Log.Information("Shutdown: complete at {Elapsed}", sw.Elapsed);
                }
            };

            window.Closing += (_ , e) =>
            {
                if (settings.GetSettings().CollapseInTrayOnClose)
                {
                    e.Cancel = true;
                    Avalonia.Threading.Dispatcher.UIThread.Post(Services.GetRequiredService<INativeTray>().HideWindow);
                }
            };

            var previousState = window.WindowState;

            window.PropertyChanged += (_, e) =>
            {
                if (e.Property != Window.WindowStateProperty)
                    return;

                var current = window.WindowState;

                if (previousState != WindowState.Minimized &&
                    current == WindowState.Minimized)
                {
                    if (settings.GetSettings().CollapseInTrayOnMinimize)
                        Services.GetRequiredService<INativeTray>().HideWindow();
                }

                previousState = current;
            };

            desktop.MainWindow = window;

            Services.GetRequiredService<IBridge>().InitializeWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void RegisterServices(IServiceCollection services)
    {
        _ = services.AddLogging(b => b.ClearProviders()
            .AddSerilog(Serilog.Log.Logger)
            .AddFilter("Microsoft.AspNetCore", Microsoft.Extensions.Logging.LogLevel.Warning));

        _ = OperatingSystem.IsWindows()
            ? services.AddSingleton<ILoginKeyProvider, DpapiKeyProvider>()
            : services.AddSingleton<ILoginKeyProvider, FileKeyProvider>();

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
            return new ServerInfoLoader(fetcher.UpdateInfoForAsync);
        });
        _ = services.AddSingleton<ServerStatusCache>();
        _ = services.AddSingleton<ContentManager>();
        _ = services.AddSingleton<ICdnRegistry, CdnRegistry>();
        _ = services.AddSingleton<EngineSourceGuard>();
        _ = services.AddSingleton<IEngineManager, EngineManagerDynamic>();
        _ = services.AddSingleton<Updater>();
        _ = services.AddSingleton<LoginManager>();
        _ = services.AddSingleton<StarlightAuthApi>();
        _ = services.AddSingleton<SteamAuthService>();
        _ = services.AddSingleton<DiscordAuthService>();
        _ = services.AddSingleton<ClientLogManager>();
        _ = services.AddSingleton<LocalServerManager>();
        _ = services.AddTransient<Connector>();
        _ = services.AddSingleton<UiActivity>();
        _ = services.AddSingleton<UiTicker>();
        _ = services.AddSingleton<LauncherUpdater>();
        _ = services.AddSingleton<IBridge, Bridge>();
        _ = services.AddSingleton<AppState>(); // Service for UI refresh.
    }
}
