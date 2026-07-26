using System.Globalization;
using System.Reflection;
using System.Text;
using Linguini.Bundle;
using Linguini.Bundle.Builder;
using Linguini.Shared.Types.Bundle;
using Linguini.Syntax.Parser;
using Microsoft.Extensions.Logging;
using Starlight.Launcher.Services.Settings;
using Starlight.Launcher.WebUI.Localization;
using TerraFX.Interop.Windows;

namespace Starlight.Launcher.Services.Localization;

public sealed class LocalizationManager : ILocalizationManager
{
    private const string DefaultLocale = "en-US";

    private Assembly _assembly => typeof(LocalizationManager).Assembly;

    private ILogger<LocalizationManager> _logger;
    private SettingsService _settings;
    private readonly Dictionary<string, List<string>> _resourcesByCulture = new();
    private FluentBundle? _currentBundle;

    public CultureInfo SystemCulture { get; private set; } = CultureInfo.InvariantCulture;

    public string this[string key]
        => GetString(key);

    public event Action? Changed;

    public LocalizationManager(ILogger<LocalizationManager> logger, SettingsService settings)
    {
        _logger = logger;
        _settings = settings;
    }

    public async Task Initialize()
    {
        var currentLocale = CultureInfo.CurrentUICulture;
        try
        {
            IndexResources();

            SystemCulture = MatchCultureAgainstAvailable(currentLocale) ?? new CultureInfo(DefaultLocale);
#if DEBUG
            _logger.LogDebug("Found system culture {SystemCulture} for current culture {CurrentCulture}", SystemCulture.Name, currentLocale.Name);
#endif
            var selectedLocale = _settings.GetSettings().SelectedLanguage;
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize localization: {Exception}", ex);
        }
    }

    private async Task LoadCulture(CultureInfo culture)
    {
        if (!_resourcesByCulture.ContainsKey(culture.Name))
        {
            _logger.LogWarning("Culture {Culture} is not available, falling back to default", culture.Name);
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
        => _resourcesByCulture.Keys.ToList();

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

        if (!_resourcesByCulture.TryGetValue(culture.Name, out var resources))
            return;

        var countFiles = 0;

        foreach (var resource in resources)
        {
            try
            {
                using var stream = _assembly.GetManifestResourceStream(resource);

                if (stream == null)
                    continue;

                using var reader = new StreamReader(stream, Encoding.UTF8);

                var parsed = LinguiniParser
                    .FromTextReader(reader, resource)
                    .Parse();

                foreach (var error in parsed.Errors)
                {
                    _logger?.LogError(
                        "Failed to parse {File}: {Error}",
                        resource,
                        error.Message);
                }

                bundle.AddResourceOverriding(parsed);

                countFiles++;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex,
                    "Failed to load resource {Resource}",
                    resource);
            }
        }

        _logger?.LogInformation("Loaded {Count} localization files for culture {Culture}", countFiles, culture.Name);
    }

    public void SwitchLanguage(string cultureName)
    {
        try
        {
            if (!_resourcesByCulture.ContainsKey(cultureName))
                throw new ArgumentException($"Culture {cultureName} is not available");
            var culture = new CultureInfo(cultureName);
            SwitchLanguage(culture);
            Changed.Invoke();
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
            if (_resourcesByCulture.ContainsKey(parent.Name))
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

    private void IndexResources()
    {
        _resourcesByCulture.Clear();

        foreach (var resource in _assembly.GetManifestResourceNames())
        {
            var separator = resource.IndexOfAny(['\\', '/']);

            if (separator == -1)
                continue;

            var culture = resource[..separator];

            if (!_resourcesByCulture.TryGetValue(culture, out var list))
            {
                list = [];
                _resourcesByCulture[culture] = list;
            }

            list.Add(resource);
        }
    }
}
