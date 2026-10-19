using System.Text.Json;
using System.Text.Json.Nodes;
using Serilog;

namespace Starlight.Launcher.Services.LocalServer.Dotnet;

public enum RollForwardPolicy
{
    Disable,
    LatestPatch,
    Minor,
    LatestMinor,
    Major,
    LatestMajor
}

public sealed record FrameworkRequirement(string Name, RuntimeVersion Version, RollForwardPolicy RollForward)
{
    public bool IsSatisfiedBy(RuntimeVersion candidate)
        => RollForward == RollForwardPolicy.Disable
        ? candidate == Version
        : (!candidate.IsPrerelease || Version.IsPrerelease)
            && candidate >= Version
            && RollForward switch
            {
                RollForwardPolicy.LatestPatch => candidate.Major == Version.Major && candidate.Minor == Version.Minor,
                RollForwardPolicy.Minor or RollForwardPolicy.LatestMinor => candidate.Major == Version.Major,
                _ => true
            };
}

public sealed record DotnetRuntimeRequirements(IReadOnlyList<FrameworkRequirement> Frameworks)
{
    public const string NetCoreApp = "Microsoft.NETCore.App";
    public const string AspNetCoreApp = "Microsoft.AspNetCore.App";

    public const string RuntimeConfigFileName = "Robust.Server.runtimeconfig.json";

    public bool NeedsAspNetCore => Frameworks.Any(f => f.Name == AspNetCoreApp);

    public FrameworkRequirement NetCore => Frameworks.First(f => f.Name == NetCoreApp);

    public string DisplayVersion => $"{NetCore.Version.Major}.{NetCore.Version.Minor}";

    public bool IsSatisfiedBy(IReadOnlyDictionary<string, List<RuntimeVersion>> installed)
        => Frameworks.All(f => installed.TryGetValue(f.Name, out var versions) && versions.Any(f.IsSatisfiedBy));

    public static DotnetRuntimeRequirements? TryLoad(string installDir)
    {
        var path = Path.Combine(installDir, RuntimeConfigFileName);
        if (!File.Exists(path))
            return null;

        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (Exception e) when (e is JsonException or IOException or FormatException)
        {
            Log.Warning(e, "Failed to read {RuntimeConfig}, falling back to the server apphost", path);
            return null;
        }
    }

    public static DotnetRuntimeRequirements? Parse(string json)
    {
        var options = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        })?["runtimeOptions"];

        if (options is null)
            return null;

        if (options["includedFrameworks"] is JsonArray { Count: > 0 })
            return null;

        var defaultPolicy = ReadPolicy(options) ?? RollForwardPolicy.Minor;

        var references = new List<JsonNode>();
        if (options["framework"] is JsonObject single)
            references.Add(single);

        if (options["frameworks"] is JsonArray many)
            references.AddRange(many.OfType<JsonObject>());

        var frameworks = new List<FrameworkRequirement>();
        foreach (var reference in references)
        {
            var name = reference["name"]?.GetValue<string>();
            var versionText = reference["version"]?.GetValue<string>();
            if (string.IsNullOrEmpty(name))
                continue;

            if (!RuntimeVersion.TryParse(versionText, out var version))
                throw new FormatException($"Invalid version '{versionText}' for framework {name}.");

            frameworks.Add(new FrameworkRequirement(name, version, ReadPolicy(reference) ?? defaultPolicy));
        }

        if (frameworks.Count == 0)
            return null;

        if (frameworks.All(f => f.Name != NetCoreApp) && frameworks.FirstOrDefault(f => f.Name == AspNetCoreApp) is { } asp)
            frameworks.Add(asp with { Name = NetCoreApp });

        return new DotnetRuntimeRequirements(frameworks);
    }

    private static RollForwardPolicy? ReadPolicy(JsonNode node)
    {
        if (node["rollForward"] is JsonValue value
            && value.TryGetValue<string>(out var text)
            && Enum.TryParse<RollForwardPolicy>(text, ignoreCase: true, out var policy))
        {
            return policy;
        }

        if (node["rollForwardOnNoCandidateFx"] is not JsonValue legacy || !legacy.TryGetValue<int>(out var level))
            return null;

        return level switch
        {
            0 => RollForwardPolicy.LatestPatch,
            2 => RollForwardPolicy.Major,
            _ => RollForwardPolicy.Minor
        };
    }
}
