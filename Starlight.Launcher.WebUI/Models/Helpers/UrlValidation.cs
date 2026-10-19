namespace Starlight.Launcher.WebUI.Models.Helpers;

/// <summary>
/// Validators for URL fields in settings. Each returns null when the value is valid, otherwise the error text.
/// </summary>
public static class UrlValidation
{
    public static string? HttpOrHttps(string value) => Validate(value, httpsOnly: false);

    /// <summary>
    /// For URLs whose content is trusted as-is (CDNs), where plain HTTP would allow tampering in transit.
    /// </summary>
    public static string? HttpsOnly(string value) => Validate(value, httpsOnly: true);

    private static string? Validate(string value, bool httpsOnly)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "URL is required";

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return "Invalid URL";

        if (uri.Scheme == Uri.UriSchemeHttps || (!httpsOnly && uri.Scheme == Uri.UriSchemeHttp))
            return null;

        return httpsOnly ? "Only HTTPS allowed" : "Only HTTP/HTTPS allowed";
    }
}
