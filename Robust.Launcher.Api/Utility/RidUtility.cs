using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace Robust.Launcher.Api.Utility;

public static class RidUtility
{
    private static readonly (OSPlatform Platform, string Name)[] _osNames =
    [
        (OSPlatform.Windows, "win"),
        (OSPlatform.Linux, "linux"),
        (OSPlatform.FreeBSD, "freebsd"),
        (OSPlatform.OSX, "osx"),
    ];

    public static string? FindBestRid(ICollection<string> runtimes, string? currentRid = null)
    {
        var catalog = LoadRidCatalog();

        if (currentRid == null)
        {
            var reportedRid = RuntimeInformation.RuntimeIdentifier;
            if (!catalog.Runtimes.ContainsKey(reportedRid))
            {
                currentRid = GuessRid();
                Log.Debug(".NET reported unknown RID: {reported}, guessing: {guessed}", reportedRid, currentRid);
            }
            else
            {
                currentRid = reportedRid;
            }
        }

        // Breadth-first search.
        var q = new Queue<string>();
        if (!catalog.Runtimes.TryGetValue(currentRid, out var root))
            return null; // RID doesn't exist in catalog???

        root.Discovered = true;
        q.Enqueue(currentRid);

        while (q.TryDequeue(out var v))
        {
            if (runtimes.Contains(v))
            {
                return v;
            }

            foreach (var w in catalog.Runtimes[v].Imports)
            {
                var r = catalog.Runtimes[w];
                if (!r.Discovered)
                {
                    q.Enqueue(w);
                    r.Discovered = true;
                }
            }
        }

        return null;
    }

    private static string GuessRid()
    {
        var arch = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();

        foreach (var (platform, os) in _osNames)
        {
            if (RuntimeInformation.IsOSPlatform(platform))
                return $"{os}-{arch}";
        }

        return "unknown";
    }

    private static RidCatalog LoadRidCatalog()
    {
        using var stream = typeof(RidCatalog).Assembly.GetManifestResourceStream("Utility.runtime.json")!;
        var ms = new MemoryStream();
        stream.CopyTo(ms);

        return JsonSerializer.Deserialize<RidCatalog>(ms.GetBuffer().AsSpan(0, (int)ms.Length))!;
    }

    private sealed class RidCatalog
    {
        [JsonInclude]
        [JsonPropertyName("runtimes")]
        public Dictionary<string, Runtime> Runtimes = default!;

        public sealed class Runtime
        {
            public bool Discovered;

            [JsonInclude]
            [JsonPropertyName("#import")]
            public string[] Imports = default!;
        }
    }
}
