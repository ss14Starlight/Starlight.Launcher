using Robust.Launcher.Api.Utility;

namespace Starlight.Launcher.WebUI.Models.Data;

public sealed class RobustCdn
{
    /// <summary>
    /// Mirror URLs for this single CDN. Treated as interchangeable for
    /// availability (Happy Eyeballs) fallback — they must serve the same content.
    /// </summary>
    public UrlFallbackSet BaseUrl { get; }

    /// <summary>
    /// Ed25519 public key (PKIX / PEM text) used to verify engine and module
    /// signatures served by this CDN. One key per CDN, shared across its mirrors
    /// since mirrors serve identical, identically-signed content.
    /// </summary>
    public required string PublicKey { get; init; }

    public UrlFallbackSet BuildsManifest => BaseUrl + "manifest.json";
    public UrlFallbackSet ModulesManifest => BaseUrl + "modules.json";

    public RobustCdn(UrlFallbackSet baseUrl) => BaseUrl = baseUrl;

    public RobustCdn(params string[] mirrorUrls) : this(new UrlFallbackSet([.. mirrorUrls]))
    {
    }
}
