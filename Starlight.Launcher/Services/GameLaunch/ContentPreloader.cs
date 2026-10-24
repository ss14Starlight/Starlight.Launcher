using Robust.Launcher.Api.Models;
using Serilog;
using Starlight.Launcher.Services.Settings;

namespace Starlight.Launcher.Services.GameLaunch;

public sealed class ContentPreloader(SettingsService settings, Connector connector, Updater updater)
{
    private static readonly TimeSpan _startupDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan _interval = TimeSpan.FromMinutes(15);

    private static readonly TimeSpan _favoritesChangedDelay = TimeSpan.FromSeconds(15);

    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _wake = new(0, 1);

    public void Start()
    {
        settings.FavoritesChanged += Wake;
        _ = Task.Run(() => RunAsync(_cts.Token));
    }

    public void Stop()
    {
        settings.FavoritesChanged -= Wake;
        _cts.Cancel();
    }

    private void Wake()
    {
        try
        {
            _ = _wake.Release();
        }
        catch (SemaphoreFullException)
        {
        }
    }

    private async Task RunAsync(CancellationToken cancel)
    {
        try
        {
            await Task.Delay(_startupDelay, cancel);

            while (true)
            {
                try
                {
                    await PreloadFavoritesAsync(cancel);
                }
                catch (OperationCanceledException) when (cancel.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception e)
                {
                    Log.Error(e, "Favorite content preload pass failed");
                }

                if (await _wake.WaitAsync(_interval, cancel))
                    await Task.Delay(_favoritesChangedDelay, cancel);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private bool IsIdle()
        => settings.GetSettings().PreloadFavoriteContent
            && connector.ActiveLaunches == 0
            && !updater.IsUpdating;

    private async Task PreloadFavoritesAsync(CancellationToken cancel)
    {
        if (!IsIdle())
            return;

        var appSettings = settings.GetSettings();

        var maxBuilds = Math.Max(0, appSettings.MaxVersionsToKeep - 1);
        var maxForkBuilds = Math.Max(0, appSettings.MaxForkVersionsToKeep);

        var builds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var forkBuilds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var favorite in settings.GetFavorites())
        {
            if (builds.Count >= maxBuilds)
                break;

            if (!IsIdle())
                return;

            if (await GetBuildAsync(favorite.Address, cancel) is not { } build)
                continue;

            var key = $"{build.ForkId}/{build.ManifestHash ?? build.Hash ?? build.Version}/{build.EngineVersion}";
            if (builds.Contains(key))
                continue;

            if (forkBuilds.GetValueOrDefault(build.ForkId) >= maxForkBuilds)
                continue;

            _ = builds.Add(key);
            forkBuilds[build.ForkId] = forkBuilds.GetValueOrDefault(build.ForkId) + 1;

            Log.Debug("Preloading content for favorite {Address}: {ForkId}/{Version}", favorite.Address, build.ForkId, build.Version);

            try
            {
                if (!await updater.TryPreloadAsync(build, cancel))
                    return;
            }
            catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
            {
                Log.Debug("Favorite content preload gave way to another update");
                return;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                Log.Warning(e, "Failed to preload content for favorite {Address}", favorite.Address);
            }
        }
    }

    private async Task<ServerBuildInformation?> GetBuildAsync(string address, CancellationToken cancel)
    {
        ServerInfo info;
        try
        {
            (info, _, _) = await connector.GetServerInfoAsync(address, cancel);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Log.Debug(e, "Can't get server info of favorite {Address} for preloading", address);
            return null;
        }

        if (info.PrivacyPolicy is { } policy
            && (!settings.HasAcceptedPrivacyPolicy(policy.Identifier, out var accepted) || accepted != policy.Version))
        {
            return null;
        }

        return info.BuildInformation;
    }
}
