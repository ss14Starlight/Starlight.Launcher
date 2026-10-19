using System.Text.RegularExpressions;

namespace Starlight.Launcher.Tests;

public sealed partial class LocalizationUsageTests
{
    private static readonly HashSet<string> _allowedLiterals =
    [
        "Starlight",
        "STARLIGHT",
        "Starlight.Launcher",
        "Discord",
        "Steam",
        "key=value",
        // Tool and file format names on the Development page.
        "Tracy",
        "RenderDoc",
        "dotnet-trace",
        "dotnet-counters",
        "dotnet-gcdump",
        "dotnet-stack",
        "dotnet-monitor",
        "NetTrace",
        "Speedscope",
        "Chromium",
    ];

    private static readonly HashSet<string> _skippedFiles = ["Components/App.razor"];

    private static readonly string _webUiDir = Path.Combine(FindRepositoryRoot(), "Starlight.Launcher.WebUI");

    public static TheoryData<string> RazorFiles()
    {
        var data = new TheoryData<string>();
        foreach (var file in EnumerateSources("*.razor"))
        {
            var relative = Path.GetRelativePath(_webUiDir, file).Replace('\\', '/');
            if (!_skippedFiles.Contains(relative))
                data.Add(relative);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(RazorFiles))]
    public void MarkupHasNoHardcodedText(string file)
    {
        var markup = StripNonMarkup(File.ReadAllText(Path.Combine(_webUiDir, file)));
        var found = new List<string>();

        foreach (Match m in TextNodeRegex().Matches(markup))
        {
            var text = m.Groups[1].Value.Trim();
            if (text.StartsWith('@') || CodeCharsRegex().IsMatch(text))
                continue;

            if (IsTranslatable(text))
                found.Add($"text: \"{text}\"");
        }

        foreach (Match m in TextAttributeRegex().Matches(markup))
        {
            var value = m.Groups[2].Value;
            if (IsTranslatable(value))
                found.Add($"{m.Groups[1].Value}=\"{value}\"");
        }

        Assert.True(found.Count == 0,
            $"{file} has {found.Count} hardcoded string(s), use L[\"key\"] instead:\n{string.Join('\n', found)}");
    }

    [Fact]
    public void UsedKeysExistInDefaultLocale()
    {
        var defaults = LocalizationKeysTests.DefaultKeys;
        var missing = new SortedSet<string>();

        foreach (var file in EnumerateSources("*.razor").Concat(EnumerateSources("*.cs")))
        {
            foreach (Match m in KeyUsageRegex().Matches(File.ReadAllText(file)))
            {
                var key = m.Groups[1].Value;
                if (!defaults.ContainsKey(key))
                    _ = missing.Add($"{Path.GetRelativePath(_webUiDir, file)}: {key}");
            }
        }

        Assert.True(missing.Count == 0,
            $"{missing.Count} key(s) used in code are missing from the default locale:\n{string.Join('\n', missing)}");
    }

    private static bool IsTranslatable(string text)
        => LetterRegex().IsMatch(text)
            && !_allowedLiterals.Contains(text)
            && !text.Contains("://", StringComparison.Ordinal);

    private static string StripNonMarkup(string source)
    {
        source = RazorCommentRegex().Replace(source, "");
        source = HtmlCommentRegex().Replace(source, "");
        source = StyleOrScriptRegex().Replace(source, "");

        var code = CodeBlockRegex().Match(source);
        return code.Success ? source[..code.Index] : source;
    }

    private static IEnumerable<string> EnumerateSources(string pattern)
        => Directory.EnumerateFiles(_webUiDir, pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Order();

    private static string FindRepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Starlight.Launcher.slnx")))
                return dir.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate Starlight.Launcher.slnx above the test output directory.");
    }

    [GeneratedRegex(@"(?<!=)>([^<>]*)<")]
    private static partial Regex TextNodeRegex();

    [GeneratedRegex(@"[{}();]")]
    private static partial Regex CodeCharsRegex();

    [GeneratedRegex(@"\b(Label|Placeholder|Title|Text|Helper|HelperText|Tooltip|Description|aria-label|alt|title|placeholder)=""([^""@][^""]*)""")]
    private static partial Regex TextAttributeRegex();

    [GeneratedRegex(@"\bL(?:\[|\.GetString\()\s*""([^""]+)""")]
    private static partial Regex KeyUsageRegex();

    [GeneratedRegex(@"\p{L}")]
    private static partial Regex LetterRegex();

    [GeneratedRegex(@"@\*.*?\*@", RegexOptions.Singleline)]
    private static partial Regex RazorCommentRegex();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex HtmlCommentRegex();

    [GeneratedRegex(@"<(style|script)\b.*?</\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex StyleOrScriptRegex();

    [GeneratedRegex(@"^@code\b", RegexOptions.Multiline)]
    private static partial Regex CodeBlockRegex();
}
