using Avalonia.Controls;
using Avalonia.Platform;
using Serilog;

namespace Starlight.Launcher.Services.WebUI;

/// <summary>
///     Linux-specific WebView wiring.
/// </summary>
public static class LinuxWebViewSetup
{
    /// <summary>Forces a backend: <c>gtk</c> (WebKitGTK) or <c>wpe</c> (WPE WebKit).</summary>
    public const string BackendVariable = "STARLIGHT_WEBVIEW_BACKEND";

    /// <summary>Renders WebKitGTK offscreen and composites it through Avalonia instead of embedding a native window.</summary>
    public const string OffscreenVariable = "STARLIGHT_WEBVIEW_OFFSCREEN";

    /// <summary>WPE rendering mode: <c>auto</c>, <c>shm</c>, <c>egl</c> or <c>dmabuf</c>.</summary>
    public const string RenderingVariable = "STARLIGHT_WEBVIEW_WPE_RENDERING";

    private static readonly WebViewAdapterType[] _candidates = [WebViewAdapterType.WebKitGtk, WebViewAdapterType.WpeWebKit];

    /// <summary>
    ///     Picks a backend and applies it to <paramref name="webView" />. Safe to call before the control
    ///     is attached; the choice is applied when the adapter asks for its environment.
    /// </summary>
    public static void Configure(NativeWebView webView)
    {
        var backend = ChooseBackend();
        var offscreen = ReadFlag(OffscreenVariable);
        var rendering = ReadRenderingMode();

        Log.Information(
            "Linux WebView: backend {Backend}, offscreen {Offscreen}, WPE rendering {Rendering}. Availability: {Availability}. Session: {Session}",
            backend, offscreen?.ToString() ?? "default", rendering?.ToString() ?? "default", DescribeAvailability(), DescribeSession());

        webView.EnvironmentRequested += (_, args) =>
        {
            switch (args)
            {
                // Raised by the WPE adapter, which is the default on Linux and can hand over to WebKitGTK.
                case LinuxWpeWebViewEnvironmentRequestedEventArgs wpe:
                    wpe.PreferWebKitGtkInstead = backend == WebViewAdapterType.WebKitGtk;
                    if (rendering is { } mode)
                        wpe.RenderingMode = mode;
                    break;

                // Raised once WebKitGTK actually builds its adapter.
                case GtkWebViewEnvironmentRequestedEventArgs gtk:
                    if (offscreen is { } useOffscreen)
                        gtk.ExperimentalOffscreen = useOffscreen;
                    break;
            }
        };
    }

    /// <summary>Whether any WebView backend is installed and usable on this machine.</summary>
    public static bool AnyBackendAvailable()
        => _candidates.Any(c => Probe(c) is { IsInstalled: true, IsSupported: true });

    public static string DescribeSession()
    {
        static string Env(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : "-";

        return $"type={Env("XDG_SESSION_TYPE")} desktop={Env("XDG_CURRENT_DESKTOP")} " +
               $"wayland={Env("WAYLAND_DISPLAY")} x11={Env("DISPLAY")} flatpak={(Settings.InstallKindDetector.IsFlatpak ? "yes" : "no")} " +
               $"compositing-off={Env("WEBKIT_DISABLE_COMPOSITING_MODE")} dmabuf-off={Env("WEBKIT_DISABLE_DMABUF_RENDERER")} " +
               $"dmabuf-shm={Env("WEBKIT_DMABUF_RENDERER_FORCE_SHM")} " +
               $"{BackendVariable}={Env(BackendVariable)} {OffscreenVariable}={Env(OffscreenVariable)}";
    }

    /// <summary>A one-line, log- and UI-friendly summary of which backends are present.</summary>
    public static string DescribeAvailability()
        => string.Join("; ", _candidates.Select(c =>
        {
            var info = Probe(c);
            return info is null
                ? $"{c}: unknown"
                : info is { IsInstalled: true, IsSupported: true }
                ? $"{c}: available{(string.IsNullOrEmpty(info.Version) ? "" : $" ({info.Version})")}"
                : $"{c}: unavailable{(string.IsNullOrEmpty(info.UnavailableReason) ? "" : $" - {info.UnavailableReason}")}";
        }));

    private static WebViewAdapterType ChooseBackend()
    {
        switch (Environment.GetEnvironmentVariable(BackendVariable)?.Trim().ToLowerInvariant())
        {
            case "gtk" or "webkitgtk" or "webkit2gtk":
                return WebViewAdapterType.WebKitGtk;
            case "wpe" or "wpewebkit":
                return WebViewAdapterType.WpeWebKit;
        }

        // WebKitGTK ships with practically every desktop distro; WPE usually has to be installed by hand.
        // Fall back to whichever one is actually there rather than insisting on a missing backend.
        foreach (var candidate in _candidates)
        {
            if (Probe(candidate) is { IsInstalled: true, IsSupported: true })
                return candidate;
        }

        return WebViewAdapterType.WebKitGtk;
    }

    private static DetailedWebViewAdapterInfo? Probe(WebViewAdapterType type)
    {
        try
        {
            return WebViewAdapterInfo.GetAdapterInfo(type);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Probing WebView adapter {Type} failed", type);
            return null;
        }
    }

    private static bool? ReadFlag(string variable)
        => Environment.GetEnvironmentVariable(variable)?.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "on" => true,
            "0" or "false" or "no" or "off" => false,
            _ => null,
        };

    private static WpeRenderingMode? ReadRenderingMode()
        => Environment.GetEnvironmentVariable(RenderingVariable)?.Trim().ToLowerInvariant() switch
        {
            "auto" => WpeRenderingMode.Auto,
            "shm" or "software" => WpeRenderingMode.Shm,
            "egl" => WpeRenderingMode.Egl,
            "dmabuf" or "dma-buf" => WpeRenderingMode.DmaBuf,
            _ => null,
        };
}
