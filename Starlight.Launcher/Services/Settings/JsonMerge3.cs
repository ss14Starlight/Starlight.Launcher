using System.Text.Json.Nodes;

namespace Starlight.Launcher.Services.Settings;

public static class JsonMerge3
{
    private static readonly string[] _idProperties = ["Key", "Name", "HubUri", "Url", "Address"];

    public static JsonNode? Merge(JsonNode? baseValue, JsonNode? mine, JsonNode? theirs, bool baseKnown = true)
    {
        if (JsonNode.DeepEquals(mine, theirs))
            return mine?.DeepClone();

        if (baseKnown && JsonNode.DeepEquals(mine, baseValue))
            return theirs?.DeepClone();

        if (baseKnown && JsonNode.DeepEquals(theirs, baseValue))
            return mine?.DeepClone();

        if (mine is JsonObject mo && theirs is JsonObject to && baseValue is JsonObject or null)
            return MergeObjects(baseValue as JsonObject, mo, to, baseKnown);

        if (mine is JsonArray ma
            && theirs is JsonArray ta
            && baseValue is JsonArray or null
            && TryMergeArrays(baseValue as JsonArray ?? [], ma, ta, baseKnown, out var merged))
        {
            return merged;
        }

        return (baseKnown ? theirs : mine)?.DeepClone();
    }

    private static JsonObject MergeObjects(JsonObject? b, JsonObject m, JsonObject t, bool baseKnown)
    {
        var result = new JsonObject();

        foreach (var key in m.Select(p => p.Key).Concat(t.Select(p => p.Key)).Distinct())
        {
            var inMine = m.TryGetPropertyValue(key, out var mv);
            var inTheirs = t.TryGetPropertyValue(key, out var tv);
            JsonNode? bv = null;
            var inBase = b?.TryGetPropertyValue(key, out bv) == true;

            if (!inTheirs && inBase)
                continue;

            if (!inMine && (inBase || !inTheirs))
                continue;

            if (!inMine)
                result[key] = tv?.DeepClone();
            else if (!inTheirs)
                result[key] = mv?.DeepClone();
            else
                result[key] = Merge(bv, mv, tv, baseKnown && inBase);
        }

        return result;
    }

    private static bool TryMergeArrays(JsonArray b, JsonArray m, JsonArray t, bool baseKnown, out JsonArray merged)
    {
        merged = [];
        if (!TryGetIds(b, m, t, out var id))
            return false;

        var baseById = b.ToDictionary(id);
        var theirsById = t.ToDictionary(id);
        var result = new List<(string Id, JsonNode? Value)>();

        foreach (var item in m)
        {
            var key = id(item);
            var inBase = baseById.TryGetValue(key, out var bv);
            var inTheirs = theirsById.TryGetValue(key, out var tv);

            if (inBase && !inTheirs)
                continue;

            result.Add((key, inTheirs ? Merge(bv, item, tv, baseKnown && inBase) : item?.DeepClone()));
        }

        var present = result.Select(r => r.Id).ToHashSet();
        for (var i = 0; i < t.Count; i++)
        {
            var key = id(t[i]);
            if (present.Contains(key) || baseById.ContainsKey(key))
                continue;

            var at = 0;
            for (var j = i - 1; j >= 0; j--)
            {
                var prev = result.FindIndex(r => r.Id == id(t[j]));
                if (prev >= 0)
                {
                    at = prev + 1;
                    break;
                }
            }

            result.Insert(at, (key, t[i]?.DeepClone()));
            _ = present.Add(key);
        }

        merged = [.. result.Select(r => r.Value)];
        return true;
    }

    private static bool TryGetIds(JsonArray b, JsonArray m, JsonArray t, out Func<JsonNode?, string> id)
    {
        id = n => n?.ToJsonString() ?? "null";
        JsonNode?[] all = [.. b, .. m, .. t];

        if (all.All(n => n is JsonValue or null))
            return Unique(id, b, m, t);

        if (!all.All(n => n is JsonObject))
            return false;

        foreach (var prop in _idProperties)
        {
            if (!all.All(n => n![prop] is JsonValue v && v.TryGetValue<string>(out _)))
                continue;

            string ByProp(JsonNode? n) => n![prop]!.GetValue<string>();
            if (Unique(ByProp, b, m, t))
            {
                id = ByProp;
                return true;
            }
        }

        return false;
    }

    private static bool Unique(Func<JsonNode?, string> id, params JsonArray[] arrays)
        => arrays.All(a => a.Select(id).Distinct().Count() == a.Count);
}
