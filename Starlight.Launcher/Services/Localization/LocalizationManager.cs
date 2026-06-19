using Linguini.Bundle;
using Linguini.Bundle.Builder;
using Linguini.Shared.Types.Bundle;
using Linguini.Syntax.Parser;
using Microsoft.Extensions.Logging;
using Starlight.Launcher.Models.Data;
using Starlight.Launcher.Services.Settings;
using Starlight.Launcher.Services.State;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Starlight.Launcher.Services.Localization;

public sealed class LocalizationManager
{
    private const string DefaultLocale = "en-US";
    private const string PathToManifest = "Locale/manifest.json";

    private ILogger<LocalizationManager>? _logger;
    private LocalizationsManifest _localizationsManifest = new();
    private FluentBundle? _currentBundle;

    private AppState? _state;

    public CultureInfo SystemCulture { get; private set; } = CultureInfo.InvariantCulture;

    public string this[string key]
        => GetString(key);

    public async Task Initialize(ILogger<LocalizationManager> logger, SettingsService settings, AppState state)
    {
        _logger = logger;
        _state = state;
        var currentLocale = CultureInfo.CurrentUICulture;
        try
        {
            using var stream = await FileSystem.OpenAppPackageFileAsync(PathToManifest);
            using var reader = new StreamReader(stream);

            var content = await reader.ReadToEndAsync();
            var manifest = JsonSerializer.Deserialize<LocalizationsManifest>(content);
            _localizationsManifest = manifest ?? new();

            SystemCulture = MatchCultureAgainstAvailable(currentLocale) ?? new CultureInfo(DefaultLocale);
#if DEBUG
            _logger.LogDebug("Found system culture {SystemCulture} for current culture {CurrentCulture}", SystemCulture.Name, currentLocale.Name);
#endif
            var selectedLocale = settings.GetSettings().SelectedLanguage;
            if (string.IsNullOrEmpty(selectedLocale))
            {
                _logger.LogInformation("No locale saved in settings, using system culture");
                await LoadCulture(SystemCulture);
            }
            else
            {
                _logger.LogInformation("Using locale from settings: {Locale}", selectedLocale);
                await LoadCulture(new CultureInfo(selectedLocale));
            }
        }
        catch (FileNotFoundException)
        {
            _logger?.LogCritical("Can't find localization manifest file {PathToManifest}", PathToManifest);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to initialize localization: {Exception}", ex);
        }
    }

    private async Task LoadCulture(CultureInfo culture)
    {
        if (!_localizationsManifest.ContainsKey(culture.Name))
        {
            _logger?.LogWarning("Culture {Culture} is not available, falling back to default", culture.Name);
            culture = new CultureInfo(DefaultLocale);
        }

        var bundle = LinguiniBuilder.Builder().CultureInfo(culture).SkipResources().SetUseIsolating(false).UseConcurrent().UncheckedBuild();

        await AddLanguage(bundle, new CultureInfo(DefaultLocale));

        if (culture.Name != DefaultLocale)
            await AddLanguage(bundle, culture);

        _currentBundle = bundle;

        CultureInfo.CurrentUICulture = culture;
    }

    public List<string> EnumarateAllLoadedLanguages()
        => _localizationsManifest.Keys.ToList();

    public string GetString(string key)
    {
        try
        {
            return _currentBundle?.GetMessage(key) ?? key;
        }
        catch
        {
            _logger?.LogWarning("Can't find localization: {0} !", key);
            return key;
        }
    }

    public string GetString(string key, params (string, object?)[] args)
    {
        var argsDict = new Dictionary<string, IFluentType>(args.Length);

        foreach (var (argKey, argValue) in args)
        {
            argsDict.Add(argKey, ToFluentType(argValue));
        }

        return _currentBundle?.GetMessage(key, args: argsDict) ?? key;
    }

    private static IFluentType ToFluentType(object? o) => o switch
    {
        string s => new FluentString(s),
        float f => (FluentNumber)f,
        double d => (FluentNumber)d,
        int i => (FluentNumber)i,
        long l => (FluentNumber)l,
        null => FluentNone.None,
        _ => new FluentString(o.ToString())
    };

    private async Task AddLanguage(FluentBundle bundle, CultureInfo culture)
    {
        if (!culture.Parent.Equals(CultureInfo.InvariantCulture))
            await AddLanguage(bundle, culture.Parent);

        if (!_localizationsManifest.ContainsKey(culture.Name))
            return;

        var path = $"Locale/{culture.Name}"; // Path to folder with FTL FILES.

        var countFiles = 0;
        try
        {
            foreach (var file in _localizationsManifest[culture.Name])
            {
                using var stream = await FileSystem.OpenAppPackageFileAsync(Path.Combine(path, file));
                using var reader = new StreamReader(stream, Encoding.UTF8);
                var resources = LinguiniParser.FromTextReader(reader, file).Parse();
                foreach (var error in resources.Errors)
                {
                    _logger?.LogError("Failed to parse localization file {File} for culture {Culture}: {Error}", file, culture.Name, error.Message);
                }
                bundle.AddResourceOverriding(resources);
                countFiles++;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load localization for culture {Culture} from path {Path}", culture.Name, path);
            return;
        }

        _logger?.LogInformation("Loaded {Count} localization files for culture {Culture}", countFiles, culture.Name);
    }

    public void SwitchLanguage(string cultureName)
    {
        try
        {
            if (!_localizationsManifest.ContainsKey(cultureName))
                throw new ArgumentException($"Culture {cultureName} is not available");
            var culture = new CultureInfo(cultureName);
            SwitchLanguage(culture);
            _state?.CallUpdate();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to switch language to {CultureName}: invalid culture name", cultureName);
            return;
        }
    }

    public void SwitchLanguage(CultureInfo? culture)
        => LoadCulture(culture ?? SystemCulture).Wait();

    private CultureInfo? MatchCultureAgainstAvailable(CultureInfo culture)
    {
        foreach (var parent in EnumerateParents(culture))
            if (_localizationsManifest.ContainsKey(parent.Name))
                return parent;
        return null;
    }

    private static IEnumerable<CultureInfo> EnumerateParents(CultureInfo culture)
    {
        while (!culture.Equals(CultureInfo.InvariantCulture))
        {
            yield return culture;
            culture = culture.Parent;
        }
    }
}
