using Dapper;
using Microsoft.Data.Sqlite;
using Robust.Launcher.Api.Utility;
using Starlight.Launcher.Services.ContentManagement;
using Starlight.Launcher.Services.GameLaunch;
using Starlight.Launcher.WebUI.Models.Data;

namespace Starlight.Launcher.Tests;

public sealed class ContentRepairTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"content-repair-{Guid.NewGuid():N}.db");
    private readonly SqliteConnection _con;

    public ContentRepairTests()
    {
        _con = new SqliteConnection($"Data Source={_path};Pooling=False;Foreign Keys=True");
        _con.Open();
        Assert.True(Migrator.Migrate(_con, "Starlight.Launcher.Services.ContentManagement.Migrations"));
    }

    public void Dispose()
    {
        _con.Dispose();
        File.Delete(_path);
    }

    [Fact]
    public void HealthyDatabase_IsLeftAlone()
    {
        var blob = AddBlob("hello"u8.ToArray());
        var version = AddVersion("fork");
        AddFile(version, "a.txt", blob);

        var result = ContentManager.RepairContents(_con, TestContext.Current.CancellationToken);

        Assert.Equal(DataResetResult.Success, result.Status);
        Assert.Equal(1, result.CheckedBlobs);
        Assert.False(result.FoundProblems);
        Assert.Equal(1, Count("ContentVersion"));
    }

    [Fact]
    public void CorruptBlob_RemovesItAndVersionsUsingIt()
    {
        var good = AddBlob("good"u8.ToArray());
        var bad = AddBlob("bad"u8.ToArray(), hashOf: "something else"u8.ToArray());
        var shared = AddBlob("shared"u8.ToArray());

        var broken = AddVersion("fork-a");
        AddFile(broken, "bad.txt", bad);
        AddFile(broken, "shared.txt", shared);

        var intact = AddVersion("fork-b");
        AddFile(intact, "good.txt", good);
        AddFile(intact, "shared.txt", shared);

        var result = ContentManager.RepairContents(_con, TestContext.Current.CancellationToken);

        Assert.Equal(3, result.CheckedBlobs);
        Assert.Equal(1, result.CorruptBlobs);
        Assert.Equal(1, result.RemovedVersions);
        Assert.Equal([intact], _con.Query<long>("SELECT Id FROM ContentVersion"));
        Assert.Equal([good, shared], _con.Query<long>("SELECT Id FROM Content ORDER BY Id"));
    }

    [Fact]
    public void UnreferencedBlobs_AreCleanedUp()
    {
        var used = AddBlob("used"u8.ToArray());
        _ = AddBlob("orphan"u8.ToArray());
        AddFile(AddVersion("fork"), "used.txt", used);

        var result = ContentManager.RepairContents(_con, TestContext.Current.CancellationToken);

        Assert.Equal(0, result.CorruptBlobs);
        Assert.Equal(1, result.RemovedOrphans);
        Assert.Equal([used], _con.Query<long>("SELECT Id FROM Content"));
    }

    [Fact]
    public void ForkUsage_CountsEachForksBlobsOnce()
    {
        var a = AddBlob(new byte[100]);
        var b = AddBlob(new byte[50]);

        var v1 = AddVersion("fork");
        AddFile(v1, "a", a);
        var v2 = AddVersion("fork");
        AddFile(v2, "a", a);
        AddFile(v2, "b", b);

        var other = AddVersion("other");
        AddFile(other, "b", b);

        var usage = ContentManager.QueryForkUsage(_con);

        Assert.Equal(2, usage.Count);
        Assert.Equal(new ForkStorageUsage("fork", 2, 150, usage[0].LastUsed), usage[0]);
        Assert.Equal(new ForkStorageUsage("other", 1, 50, usage[1].LastUsed), usage[1]);
        Assert.True(usage[0].LastUsed > DateTime.UtcNow.AddMinutes(-5));
    }

    private long AddBlob(byte[] data, byte[]? hashOf = null)
        => _con.QuerySingle<long>(
            "INSERT INTO Content (Hash, Size, Compression, Data) VALUES (@Hash, @Size, 0, @Data) RETURNING Id",
            new { Hash = Blake2B.HashStream(new MemoryStream(hashOf ?? data), 32), Size = data.Length, Data = data });

    private long AddVersion(string fork)
        => _con.QuerySingle<long>(
            "INSERT INTO ContentVersion (Hash, ForkId, ForkVersion, LastUsed) VALUES (@Hash, @Fork, '1', datetime('now')) RETURNING Id",
            new { Hash = Guid.NewGuid().ToByteArray(), Fork = fork });

    private void AddFile(long version, string path, long blob)
        => _con.Execute(
            "INSERT INTO ContentManifest (VersionId, Path, ContentId) VALUES (@Version, @Path, @Blob)",
            new { Version = version, Path = path, Blob = blob });

    private long Count(string table) => _con.QuerySingle<long>($"SELECT COUNT(*) FROM {table}");
}
