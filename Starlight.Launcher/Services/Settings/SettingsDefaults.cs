using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Starlight.Launcher.WebUI.Models.Settings;

namespace Starlight.Launcher.Services.Settings;

public static class SettingsDefaults
{
    private static readonly JsonSerializerOptions _indented = new() { WriteIndented = true };

    private static readonly IReadOnlyList<JsonPropertyInfo> _tracked = [.. JsonSerializerOptions.Default
        .GetTypeInfo<AppSettings>()
        .Properties
        .Where(p => p.Get is not null
            && p.Set is not null
            && p.AttributeProvider?.IsDefined(typeof(NotTrackedDefaultAttribute), true) != true)];

    private static readonly Dictionary<string, Func<object>> _effectiveWhenNull = new()
    {
        [nameof(AppSettings.RobustCdns)] = () => AppSettings.DefaultRobustCdns,
    };

    public static IEnumerable<string> TrackedKeys => _tracked.Select(p => p.Name);

    /// <summary>
    /// Effective values of tracked settings, keyed by JSON name.
    /// </summary>
    public static JsonObject Snapshot(AppSettings settings)
    {
        var result = new JsonObject();
        foreach (var prop in _tracked)
        {
            var value = prop.Get!(settings);
            if (value is null && _effectiveWhenNull.TryGetValue(prop.Name, out var effective))
                value = effective();

            result[prop.Name] = JsonSerializer.SerializeToNode(value, prop.PropertyType);
        }

        return result;
    }

    public static JsonObject DefaultsSnapshot() => Snapshot(new AppSettings());

    /// <summary>
    /// Copy of <paramref name="settings"/> with the given settings reset to this version's defaults.
    /// </summary>
    public static AppSettings ApplyDefaults(AppSettings settings, IReadOnlyCollection<string> keys)
    {
        if (keys.Count == 0)
            return settings;

        var defaults = new AppSettings();
        var copy = settings with { };
        foreach (var prop in _tracked.Where(p => keys.Contains(p.Name)))
            prop.Set!(copy, prop.Get!(defaults));

        return copy;
    }

    public sealed record Plan(IReadOnlyList<string> ApplySilently, IReadOnlyList<DefaultSettingChange> Conflicts, JsonObject Baseline);

    /// <summary>
    /// Decides what to do with every tracked setting.
    /// </summary>
    public static Plan Reconcile(JsonObject current, JsonObject defaults, JsonObject? baseline, IReadOnlyDictionary<string, JsonNode?[]> legacy)
    {
        var silent = new List<string>();
        var conflicts = new List<DefaultSettingChange>();
        var newBaseline = new JsonObject();

        foreach (var (key, newDefault) in defaults)
        {
            _ = current.TryGetPropertyValue(key, out var value);

            if (JsonNode.DeepEquals(value, newDefault))
            {
                newBaseline[key] = newDefault?.DeepClone();
                continue;
            }

            JsonNode? oldDefault = null;
            var oldDefaultKnown = baseline is not null && baseline.TryGetPropertyValue(key, out oldDefault);

            if (!oldDefaultKnown)
            {
                if (!legacy.TryGetValue(key, out var pastDefaults) || pastDefaults.Length == 0)
                {
                    newBaseline[key] = newDefault?.DeepClone();
                    continue;
                }

                oldDefault = pastDefaults.Any(d => JsonNode.DeepEquals(d, value)) ? value : ClosestTo(value, pastDefaults);
            }

            if (JsonNode.DeepEquals(oldDefault, newDefault))
            {
                newBaseline[key] = newDefault?.DeepClone();
                continue;
            }

            if (JsonNode.DeepEquals(value, oldDefault))
            {
                silent.Add(key);
                newBaseline[key] = newDefault?.DeepClone();
                continue;
            }

            var merged = JsonMerge3.Merge(oldDefault, value, newDefault);
            if (JsonNode.DeepEquals(merged, value))
            {
                newBaseline[key] = newDefault?.DeepClone();
                continue;
            }

            conflicts.Add(new(key, Format(value), Format(newDefault), oldDefaultKnown ? Format(oldDefault) : null, Format(merged)));

            if (oldDefaultKnown)
                newBaseline[key] = oldDefault?.DeepClone();
        }

        return new(silent, conflicts, newBaseline);
    }

    private static JsonNode? ClosestTo(JsonNode? value, JsonNode?[] candidates)
    {
        if (value is not JsonArray items)
            return candidates[^1];

        var best = candidates[^1];
        var bestScore = -1;
        foreach (var candidate in candidates)
        {
            var score = candidate is JsonArray c ? c.Count(x => items.Any(i => JsonNode.DeepEquals(i, x))) : 0;
            if (score >= bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }

        return best;
    }

    /// <summary>
    /// Copy of <paramref name="settings"/> with the given settings set to these snapshot values.
    /// </summary>
    public static AppSettings ApplyValues(AppSettings settings, IReadOnlyDictionary<string, JsonNode?> values)
    {
        if (values.Count == 0)
            return settings;

        var copy = settings with { };
        foreach (var prop in _tracked)
        {
            if (!values.TryGetValue(prop.Name, out var node))
                continue;

            var value = _effectiveWhenNull.TryGetValue(prop.Name, out var effective)
                && JsonNode.DeepEquals(node, JsonSerializer.SerializeToNode(effective(), prop.PropertyType))
                    ? null
                    : node.Deserialize(prop.PropertyType);

            prop.Set!(copy, value);
        }

        return copy;
    }

    private static string Format(JsonNode? node) => node?.ToJsonString(_indented) ?? "null";

    #region Legacy

    private const string LegacyStarlightKey = """
        -----BEGIN PUBLIC KEY-----
        MCowBQYDK2VwAyEAvF9h6FVrVhh9cYoSk0g/XluUVIrg40PQy8VPNaGu1vQ=
        -----END PUBLIC KEY-----
        """;

    private const string LegacySecondaryKey = """
        -----BEGIN PUBLIC KEY-----
        MCowBQYDK2VwAyEApQ9mAhMLbmhQqRH7itgNo75S5rCSMsMXvVRmMv1d9NQ=
        -----END PUBLIC KEY-----
        """;

    /// <summary>
    /// Defaults shipped before the baseline existed. A stored value equal to one of them is updated
    /// silently, any other value of these settings is asked about once. Never needs new entries:
    /// changes made from now on are caught by the baseline.
    /// </summary>
    public static IReadOnlyDictionary<string, JsonNode?[]> LegacyDefaults { get; } = BuildLegacy();

    private static Dictionary<string, JsonNode?[]> BuildLegacy()
    {
        static JsonNode? N<T>(T value) => JsonSerializer.SerializeToNode(value);

        return new()
        {
            [nameof(AppSettings.Hubs)] =
            [
                N(new List<Hub> { new() { HubUri = new Uri("https://hub.playss14.com/"), Priority = 0 } }),
            ],
            [nameof(AppSettings.AuthServerUrls)] =
            [
                N(new List<string> { "https://auth.spacestation14.com/", "https://auth.fallback.spacestation14.com/" }),
                N(new List<string> { "https://auth.playss14.com/" }),
            ],
            [nameof(AppSettings.RobustCdns)] =
            [
                N(new List<RobustCdnConfig>
                {
                    new() { Name = "Starlight", Urls = ["https://robust-builds.starlight.network/"], PublicKey = LegacyStarlightKey },
                    new() { Name = "PlaySS14", Urls = ["https://robust-builds.playss14.com/"], PublicKey = LegacySecondaryKey },
                }),
                N(new List<RobustCdnConfig>
                {
                    new() { Name = "Starlight", Urls = ["https://robust-builds.starlight.network/"], PublicKey = LegacyStarlightKey, Important = true },
                    new() { Name = "PlaySS14", Urls = ["https://robust-builds.playss14.com/"], PublicKey = LegacySecondaryKey },
                }),
            ],
            [nameof(AppSettings.LocalServerSources)] =
            [
                N(new List<LocalServerSourceConfig>()),
            ],
        };
    }

    #endregion Legacy
}
