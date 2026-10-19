using System.Text;
using Linguini.Syntax.Ast;
using Linguini.Syntax.Parser;
using Starlight.Launcher.Services.Localization;

namespace Starlight.Launcher.Tests;

public sealed class LocalizationKeysTests
{
    private const string DefaultLocale = "en-US";

    private static readonly Dictionary<string, Dictionary<string, string>> _keysByCulture = LoadKeys();

    internal static IReadOnlyDictionary<string, string> DefaultKeys => _keysByCulture[DefaultLocale];

    public static TheoryData<string> Translations()
    {
        var data = new TheoryData<string>();
        foreach (var culture in _keysByCulture.Keys.Where(c => c != DefaultLocale).Order())
            data.Add(culture);

        return data;
    }

    [Fact]
    public void DefaultLocaleAndTranslationsAreEmbedded()
    {
        Assert.Contains(DefaultLocale, _keysByCulture.Keys);
        Assert.NotEmpty(Translations());
    }

    [Theory]
    [MemberData(nameof(Translations))]
    public void TranslationHasEveryDefaultKey(string culture)
    {
        var translated = _keysByCulture[culture];
        var missing = _keysByCulture[DefaultLocale]
            .Where(kv => !translated.ContainsKey(kv.Key))
            .Select(kv => $"{kv.Value}: {kv.Key}")
            .Order()
            .ToList();

        Assert.True(missing.Count == 0, $"{culture} is missing {missing.Count} key(s):\n{string.Join('\n', missing)}");
    }

    [Theory]
    [MemberData(nameof(Translations))]
    public void TranslationHasNoUnknownKeys(string culture)
    {
        var defaults = _keysByCulture[DefaultLocale];
        var extra = _keysByCulture[culture]
            .Where(kv => !defaults.ContainsKey(kv.Key))
            .Select(kv => $"{kv.Value}: {kv.Key}")
            .Order()
            .ToList();

        Assert.True(extra.Count == 0, $"{culture} has {extra.Count} key(s) not present in {DefaultLocale}:\n{string.Join('\n', extra)}");
    }

    private static Dictionary<string, Dictionary<string, string>> LoadKeys()
    {
        var assembly = typeof(LocalizationManager).Assembly;
        var result = new Dictionary<string, Dictionary<string, string>>();

        foreach (var resource in assembly.GetManifestResourceNames())
        {
            var separator = resource.IndexOfAny(['\\', '/']);
            if (separator == -1 || !resource.EndsWith(".ftl", StringComparison.Ordinal))
                continue;

            var culture = resource[..separator];
            var file = resource[(separator + 1)..].Replace('\\', '/');

            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var parsed = LinguiniParser.FromTextReader(reader, resource).Parse();

            Assert.True(parsed.Errors.Count == 0,
                $"{resource} has parse errors:\n{string.Join('\n', parsed.Errors.Select(e => e.Message))}");

            if (!result.TryGetValue(culture, out var keys))
                result[culture] = keys = [];

            foreach (var entry in parsed.Entries)
            {
                var id = entry switch
                {
                    AstMessage message => message.GetId(),
                    AstTerm term => "-" + term.GetId(),
                    _ => null
                };

                if (id is not null)
                    Assert.True(keys.TryAdd(id, file), $"{culture}: key {id} is defined in both {keys.GetValueOrDefault(id)} and {file}");
            }
        }

        return result;
    }
}
