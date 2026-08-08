using System.Collections.Immutable;
using System.Text;
using NSec.Cryptography;
using Serilog;
using Starlight.Launcher.Services.Settings;
using Starlight.Launcher.WebUI.Models.Data;
using Starlight.Launcher.WebUI.Models.Settings;

namespace Starlight.Launcher.Models.EngineManager;

public interface ICdnRegistry
{
    ImmutableArray<RobustCdn> Cdns { get; }

    event Action? Changed;
}

public sealed class CdnRegistry : ICdnRegistry, IDisposable
{
    private readonly IDisposable _subscription;
    private ImmutableArray<RobustCdn> _cdns = [];

    public ImmutableArray<RobustCdn> Cdns => _cdns;
    public event Action? Changed;

    public CdnRegistry(SettingsService settings)
        => _subscription = settings.Subscribe(
            s => s.RobustCdns,
            OnConfigChanged,
            fireImmediately: true,
            comparer: CdnListComparer.Instance);

    private void OnConfigChanged(List<RobustCdnConfig> configs)
    {
        var resolved = Resolve(configs);
        var previous = _cdns;
        _cdns = resolved;

        if (previous.IsDefaultOrEmpty)
            return;

        Changed?.Invoke();
    }

    private static ImmutableArray<RobustCdn> Resolve(IEnumerable<RobustCdnConfig>? configs)
    {
        var source = configs?.Where(c => c.Enabled).ToList() ?? [];
        if (source.Count == 0)
            source = [.. AppSettings.DefaultRobustCdns];

        var builder = ImmutableArray.CreateBuilder<RobustCdn>();

        foreach (var cfg in source)
        {
            if (!Validate(cfg, out var urls))
                continue;

            builder.Add(new RobustCdn(urls) { PublicKey = cfg.PublicKey });
        }

        if (builder.Count == 0)
        {
            Log.Error("There are no valid CDNs available, falling back to default.");
            return [.. AppSettings.DefaultRobustCdns
                .Where(c => Validate(c, out _))
                .Select(c => new RobustCdn([.. c.Urls]) { PublicKey = c.PublicKey })];
        }

        return builder.ToImmutable();
    }

    private static bool Validate(RobustCdnConfig cfg, out string[] urls)
    {
        urls = [];

        var valid = cfg.Urls
            .Where(u => Uri.TryCreate(u, UriKind.Absolute, out var uri)
                        && uri.Scheme == Uri.UriSchemeHttps)
            .ToArray();

        if (valid.Length == 0)
        {
            Log.Warning("CDN {Name}: there's no valid https URL available.", cfg.Name);
            return false;
        }

        try
        {
            _ = PublicKey.Import(
                SignatureAlgorithm.Ed25519,
                Encoding.UTF8.GetBytes(cfg.PublicKey),
                KeyBlobFormat.PkixPublicKeyText);
        }
        catch (Exception e)
        {
            Log.Warning(e, "CDN {Name}: public key is not parsable as Ed25519 PKIX, skipping.", cfg.Name);
            return false;
        }

        urls = valid;
        return true;
    }

    public void Dispose() => _subscription.Dispose();

    private sealed class CdnListComparer : IEqualityComparer<List<RobustCdnConfig>>
    {
        public static readonly CdnListComparer Instance = new();

        public bool Equals(List<RobustCdnConfig>? x, List<RobustCdnConfig>? y)
            => ReferenceEquals(x, y) || (x is not null && y is not null && x.SequenceEqual(y));

        public int GetHashCode(List<RobustCdnConfig> obj)
        {
            var hash = new HashCode();
            foreach (var c in obj)
                hash.Add(c);
            return hash.ToHashCode();
        }
    }
}
