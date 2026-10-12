using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Robust.LoaderApi;

namespace Robust.Loader;

internal sealed class ZipFileApi(ZipArchive archive, string? prefix) : IFileApi, IDisposable
{
    private readonly ZipArchive _archive = archive;
    private readonly string? _prefix = prefix;

    public bool TryOpen(string path, [NotNullWhen(true)] out Stream? stream)
    {
        var entry = _archive.GetEntry(_prefix != null ? _prefix + path : path);
        if (entry == null)
        {
            stream = null;
            return false;
        }

        stream = new MemoryStream();
        lock (_archive)
        {
            using var zipStream = entry.Open();
            zipStream.CopyTo(stream);
        }

        stream.Position = 0;
        return true;
    }

    public IEnumerable<string> AllFiles
        => _prefix != null
        ? _archive.Entries
            .Where(e => e.Name != "" && e.FullName.StartsWith(_prefix))
            .Select(e => e.FullName[_prefix.Length..])
        : _archive.Entries
            .Where(e => e.Name != "")
            .Select(entry => entry.FullName);

    public void Dispose() => _archive.Dispose();
}
