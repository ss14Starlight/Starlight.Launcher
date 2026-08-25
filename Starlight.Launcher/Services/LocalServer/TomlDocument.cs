using System.Text;
using Starlight.Launcher.WebUI.Models.LocalServer;

namespace Starlight.Launcher.Services.LocalServer;

internal sealed class TomlDocument
{
    private readonly List<string> _groupOrder = [];
    private readonly Dictionary<string, List<string>> _keyOrder = new();
    private readonly Dictionary<string, Dictionary<string, string>> _rawValues = new();

    public static TomlDocument Parse(string text)
    {
        var doc = new TomlDocument();
        string? currentGroup = null;

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim().TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                currentGroup = line[1..^1].Trim();
                doc.EnsureGroup(currentGroup);
                continue;
            }

            var eq = line.IndexOf('=');
            if (eq < 0 || currentGroup is null)
                continue;

            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            doc.SetRaw(currentGroup, key, value);
        }

        return doc;
    }

    private void EnsureGroup(string group)
    {
        if (_rawValues.ContainsKey(group))
            return;

        _rawValues[group] = new Dictionary<string, string>();
        _keyOrder[group] = [];
        _groupOrder.Add(group);
    }

    private void SetRaw(string group, string key, string rawLiteral)
    {
        EnsureGroup(group);
        if (!_rawValues[group].ContainsKey(key))
            _keyOrder[group].Add(key);

        _rawValues[group][key] = rawLiteral;
    }

    public void Set(string group, string name, ServerCVarType type, string value) =>
        SetRaw(group, name, EncodeLiteral(type, value));

    public string Serialize()
    {
        var sb = new StringBuilder();

        foreach (var group in _groupOrder)
        {
            if (_keyOrder[group].Count == 0)
                continue;

            _ = sb.Append('[').Append(group).Append(']').Append('\n');
            foreach (var key in _keyOrder[group])
                _ = sb.Append(key).Append(" = ").Append(_rawValues[group][key]).Append('\n');
            _ = sb.Append('\n');
        }

        return sb.ToString();
    }

    private static string EncodeLiteral(ServerCVarType type, string value) => type switch
    {
        ServerCVarType.String => $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"",
        ServerCVarType.Bool => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ? "true" : "false",
        _ => value
    };
}
