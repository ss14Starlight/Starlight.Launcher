using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using Starlight.Launcher.Components.Layout;

namespace Starlight.Launcher.Services;

public partial class LauncherUpdater
{
    public static string GetVersion() => Environment.ProcessPath == null ? "" : FileVersionInfo.GetVersionInfo(Environment.ProcessPath).ProductVersion?.Split('+')[0] ?? "";

    // Man... I remember why i hate async sometimes... i mean they are good, but this has given so many issues.
    // But this... WORKS... VERY WELL... I am happy.
    public async Task<(bool IsUpdateAvailable, string CurrentVersion, string LatestVersion, string LatestUrl)> IsUpdateAvailable()
    {
        var (tagName, htmlUrl) = await GetLatestRelease();
        var currentVersion = NormalizeVersion(GetVersion());
        var latestVersion = NormalizeVersion(tagName);

        Console.WriteLine($"Current version: {currentVersion}");
        Console.WriteLine($"Latest version: {latestVersion}");

        return (!string.Equals(currentVersion, latestVersion, StringComparison.OrdinalIgnoreCase), currentVersion, latestVersion, htmlUrl ?? string.Empty);
    }

    private static string NormalizeVersion(string? version)
        => version?.Trim().TrimStart('v', 'V') ?? string.Empty;

    private async Task<(string? TagName, string? HtmlUrl)> GetLatestRelease()
    {
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Starlight.Launcher"); // Without that we get an error 403... Ok github?

        using var response = await httpClient.GetAsync("https://api.github.com/repos/ss14Starlight/Starlight.Launcher/releases/latest", HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        string responseBody = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(responseBody);

        document.RootElement.TryGetProperty("tag_name", out var tagName); // Yep we get the TAG... DONT FUCK IT UP.
        document.RootElement.TryGetProperty("html_url", out var htmlUrl); // We also get the URL for the latest download so we can easly offer it after checking update.

        return (tagName.GetString(), htmlUrl.GetString());
    }
}
