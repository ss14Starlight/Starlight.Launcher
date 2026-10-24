using System.Buffers;
using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Dapper;
using Microsoft.Data.Sqlite;
using Robust.Launcher.Api.Models.ContentManagement;
using Robust.Launcher.Api.Utility;
using Serilog;
using SpaceWizards.Sodium;
using Starlight.Launcher.WebUI.Models.Data;

namespace Starlight.Launcher.Services.ContentManagement;

public sealed partial class ContentManager
{
    private static readonly HashSet<string> _droppableFkTables = ["ContentEngineDependency", "InterruptedDownloadContent", "RunningClient"];

    public async Task<ContentRepairResult> VerifyAndRepair(CancellationToken cancel = default)
        => await Task.Run(() =>
            {
                var con = GetSqliteConnection();
                try
                {
                    if (GetRunningClientVersions(con).Count > 0)
                        return new ContentRepairResult(DataResetResult.ClientRunning);

                    if (!IsIntegrityOk(con))
                    {
                        Log.Warning("Content DB failed the integrity check, rebuilding indexes");
                        TryExecute(con, "REINDEX");

                        if (!IsIntegrityOk(con))
                        {
                            Log.Error("Content DB is still damaged after REINDEX, recreating it");
                            con.Dispose();
                            RecreateDatabase();
                            return new ContentRepairResult(DataResetResult.Success, DatabaseRebuilt: true);
                        }
                    }

                    return RepairContents(con, cancel);
                }
                finally
                {
                    con.Dispose();
                }
            }, cancel);

    internal static ContentRepairResult RepairContents(SqliteConnection con, CancellationToken cancel)
    {
        var brokenVersions = new HashSet<long>();
        var orphans = 0;

        using (var tx = con.BeginTransaction())
        {
            foreach (var (table, rowId, _, _) in con.Query<(string, long, string, long)>("PRAGMA foreign_key_check"))
            {
                if (table == "ContentManifest")
                {
                    if (con.QueryFirstOrDefault<long?>("SELECT VersionId FROM ContentManifest WHERE Id = @RowId", new { RowId = rowId }) is { } version)
                        _ = brokenVersions.Add(version);
                }
                else if (_droppableFkTables.Contains(table))
                {
                    orphans += con.Execute($"DELETE FROM {table} WHERE rowid = @RowId", new { RowId = rowId });
                }
            }

            tx.Commit();
        }

        var blobs = con
            .Query<(long Id, byte[] Hash, long Size, ContentCompressionScheme Compression)>("SELECT Id, Hash, Size, Compression FROM Content")
            .ToList();

        Log.Information("Verifying {BlobCount} content blobs", blobs.Count);

        var corrupt = new List<long>();
        foreach (var (id, hash, size, compression) in blobs)
        {
            cancel.ThrowIfCancellationRequested();

            if (!IsBlobValid(con, id, hash, size, compression))
                corrupt.Add(id);
        }

        var removedVersions = 0;
        using (var tx = con.BeginTransaction())
        {
            foreach (var chunk in corrupt.Chunk(500))
            {
                brokenVersions.UnionWith(con.Query<long>(
                    "SELECT DISTINCT VersionId FROM ContentManifest WHERE ContentId IN @Ids", new { Ids = chunk }));
            }

            foreach (var chunk in brokenVersions.Chunk(500))
                removedVersions += con.Execute("DELETE FROM ContentVersion WHERE Id IN @Ids", new { Ids = chunk });

            foreach (var chunk in corrupt.Chunk(500))
                _ = con.Execute("DELETE FROM Content WHERE Id IN @Ids", new { Ids = chunk });

            orphans += con.Execute("""
                DELETE FROM Content
                WHERE Id NOT IN (SELECT ContentId FROM ContentManifest)
                    AND Id NOT IN (SELECT ContentId FROM InterruptedDownloadContent)
                """);

            tx.Commit();
        }

        var result = new ContentRepairResult(
            DataResetResult.Success,
            CheckedBlobs: blobs.Count,
            CorruptBlobs: corrupt.Count,
            RemovedVersions: removedVersions,
            RemovedOrphans: orphans);

        Log.Information(
            "Content DB check done: {Checked} blobs checked, {Corrupt} corrupt, {Versions} versions removed, {Orphans} orphaned rows removed",
            result.CheckedBlobs, result.CorruptBlobs, result.RemovedVersions, result.RemovedOrphans);

        if (result.FoundProblems)
            TryExecute(con, "VACUUM");

        return result;
    }

    private static bool IsIntegrityOk(SqliteConnection con)
    {
        try
        {
            var rows = con.Query<string>("PRAGMA integrity_check").ToList();
            if (rows is ["ok"])
                return true;

            Log.Warning("Content DB integrity check failed: {Problems}", string.Join("; ", rows.Take(20)));
            return false;
        }
        catch (SqliteException e)
        {
            Log.Warning(e, "Content DB integrity check could not run");
            return false;
        }
    }

    private static bool IsBlobValid(SqliteConnection con, long id, byte[] expectedHash, long expectedSize, ContentCompressionScheme compression)
    {
        try
        {
            using var stream = OpenBlobRaw(con, id, compression);

            CryptoGenericHashBlake2B.State state;
            _ = CryptoGenericHashBlake2B.Init(ref state, [], expectedHash.Length);

            var buffer = ArrayPool<byte>.Shared.Rent(65536);
            long length = 0;
            try
            {
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    _ = CryptoGenericHashBlake2B.Update(ref state, buffer.AsSpan(0, read));
                    length += read;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            var hash = new byte[expectedHash.Length];
            _ = CryptoGenericHashBlake2B.Final(ref state, hash);

            if (length == expectedSize && hash.AsSpan().SequenceEqual(expectedHash))
                return true;

            Log.Warning("Content blob {BlobId} is corrupt: size {Size}/{ExpectedSize}, hash mismatch: {HashMismatch}",
                id, length, expectedSize, !hash.AsSpan().SequenceEqual(expectedHash));
            return false;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Log.Warning(e, "Content blob {BlobId} could not be read", id);
            return false;
        }
    }

    private static Stream OpenBlobRaw(SqliteConnection con, long id, ContentCompressionScheme compression)
    {
        var blob = new SqliteBlob(con, "Content", "Data", id, readOnly: true);

        return compression switch
        {
            ContentCompressionScheme.None => blob,
            ContentCompressionScheme.Deflate => new DeflateStream(blob, CompressionMode.Decompress),
            ContentCompressionScheme.ZStd => new ZStdDecompressStream(blob),
            _ => throw new InvalidDataException($"Unknown compression scheme {compression}"),
        };
    }

    private void RecreateDatabase()
    {
        var path = _settings.GetSettings().PathContentDb;
        foreach (var file in new[] { path, path + "-wal", path + "-shm" })
        {
            if (File.Exists(file))
                File.Delete(file);
        }

        Initialize();
    }

    private static void TryExecute(SqliteConnection con, string sql)
    {
        try
        {
            _ = con.Execute(sql);
        }
        catch (Exception e)
        {
            Log.Warning(e, "Failed to run {Sql} on content DB", sql);
        }
    }

    public async Task<(long DbBytes, List<ForkStorageUsage> Forks)> GetStorageUsage()
        => await Task.Run(() =>
            {
                var path = _settings.GetSettings().PathContentDb;
                var dbBytes = FileSize(path) + FileSize(path + "-wal");

                if (!File.Exists(path))
                    return (dbBytes, new List<ForkStorageUsage>());

                using var con = GetSqliteConnection();

                return (dbBytes, QueryForkUsage(con));
            });

    internal static List<ForkStorageUsage> QueryForkUsage(SqliteConnection con)
    {
        var versions = con.Query<(string? ForkId, int Versions, string? LastUsed)>(
            "SELECT ForkId, COUNT(*), MAX(LastUsed) FROM ContentVersion GROUP BY ForkId");

        var blobSizes = con
            .Query<(long Id, long Bytes)>("SELECT Id, length(Data) FROM Content")
            .ToDictionary(b => b.Id, b => b.Bytes);

        var sizes = new Dictionary<string, long>();
        foreach (var (forkId, contentId) in con.Query<(string? ForkId, long ContentId)>("""
            SELECT DISTINCT v.ForkId, cm.ContentId
            FROM ContentVersion v JOIN ContentManifest cm ON cm.VersionId = v.Id
            """))
        {
            CollectionsMarshal.GetValueRefOrAddDefault(sizes, forkId ?? "", out _) += blobSizes.GetValueOrDefault(contentId);
        }

        return versions
            .Select(v => new ForkStorageUsage(
                v.ForkId,
                v.Versions,
                sizes.GetValueOrDefault(v.ForkId ?? ""),
                ParseDbDate(v.LastUsed)))
            .OrderByDescending(f => f.Bytes)
            .ToList();
    }

    private static DateTime ParseDbDate(string? value)
        => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date)
            ? date
            : DateTime.MinValue;

    private static long FileSize(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        catch (IOException)
        {
            return 0;
        }
    }
}
