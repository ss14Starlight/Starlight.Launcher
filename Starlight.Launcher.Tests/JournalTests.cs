using System.Text.Json;
using Robust.Launcher.Api.Api;
using Robust.Launcher.Api.Models.ServerStatus;
using Starlight.Launcher.Services.Journal;
using Starlight.Launcher.WebUI.Models.Journal;

namespace Starlight.Launcher.Tests;

public sealed class JournalTests
{
    [Fact]
    public void Activity_AveragesSamplesPerWeekdayHour()
    {
        var activity = new ServerActivity();
        var friday8pm = new DateTime(2026, 10, 16, 20, 15, 0);

        activity.Add(friday8pm, 40);
        activity.Add(friday8pm.AddDays(7), 60);
        activity.Add(friday8pm.AddHours(1), 10);

        Assert.Equal(50, activity.Average(DayOfWeek.Friday, 20));
        Assert.Equal(10, activity.Average(DayOfWeek.Friday, 21));
        Assert.Null(activity.Average(DayOfWeek.Monday, 20));
        Assert.Equal(3, activity.TotalSamples);
    }

    [Fact]
    public void Activity_RepairsBucketsOfWrongSize()
    {
        var activity = new ServerActivity { PlayerSums = new double[3], Samples = new int[3] };

        activity.Add(new DateTime(2026, 10, 18, 3, 0, 0), 5);

        Assert.Equal(ServerActivity.Buckets, activity.Samples.Length);
        Assert.Equal(5, activity.Average(DayOfWeek.Sunday, 3));
    }

    [Fact]
    public void Status_ReadsRoundDetails()
    {
        const string json = """
            {"name":"Test","players":12,"soft_max_players":80,"run_level":1,"round_id":4242,
             "map":"Box Station","preset":"Traitor","panic_bunker":true,"round_start_time":"2026-10-17T10:00:00.0000000Z"}
            """;

        var status = JsonSerializer.Deserialize<ServerApi.ServerStatus>(json)!;
        var data = new ServerStatusData("ss14://test");
        ServerStatusCache.ApplyStatus(data, status);

        Assert.Equal(4242, data.RoundId);
        Assert.Equal("Box Station", data.Map);
        Assert.Equal("Traitor", data.Preset);
        Assert.True(data.PanicBunker);
        Assert.Equal(GameRoundStatus.InRound, data.RoundStatus);
        Assert.NotNull(data.RoundStartTime);
    }

    [Fact]
    public void Status_LobbyClearsRoundStartTime()
    {
        var data = new ServerStatusData("ss14://test");
        ServerStatusCache.ApplyStatus(data, JsonSerializer.Deserialize<ServerApi.ServerStatus>(
            """{"players":1,"soft_max_players":10,"run_level":1,"round_start_time":"2026-10-17T10:00:00Z"}""")!);
        ServerStatusCache.ApplyStatus(data, JsonSerializer.Deserialize<ServerApi.ServerStatus>(
            """{"players":1,"soft_max_players":10,"run_level":0}""")!);

        Assert.Null(data.RoundStartTime);
        Assert.Null(data.RoundId);
        Assert.Equal(GameRoundStatus.InLobby, data.RoundStatus);
    }

    [Fact]
    public async Task FileStore_RoundTripsAndFlushes()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sl-journal-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "store.json");
        try
        {
            var store = new JsonFileStore<Doc>(path);
            store.Update(d => d.Items.Add("a"));
            await store.FlushAsync();

            var reloaded = new JsonFileStore<Doc>(path);
            Assert.Equal(["a"], reloaded.Read(d => d.Items.ToList()));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void FileStore_BacksUpCorruptFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sl-journal-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "store.json");
        try
        {
            _ = Directory.CreateDirectory(dir);
            File.WriteAllText(path, "{ not json");

            var store = new JsonFileStore<Doc>(path);

            Assert.Empty(store.Read(d => d.Items.ToList()));
            Assert.Single(Directory.GetFiles(dir, "store.json.corrupt-*.bak"));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Theory]
    [InlineData(false, false, null, false, false, true)]
    [InlineData(true, false, null, false, false, false)]
    [InlineData(false, false, 0, false, false, true)]
    [InlineData(false, false, 5, false, false, false)]
    [InlineData(false, false, null, false, true, false)]
    public void Watch_IsEmptyOnlyWithoutConditions(bool lobby, bool round, int? players, bool slot, bool auto, bool empty)
    {
        var watch = new ServerWatch("ss14://test")
        {
            OnLobby = lobby,
            OnNewRound = round,
            PlayersAtLeast = players,
            OnSlotFree = slot,
            AutoConnect = auto,
        };

        Assert.Equal(empty, watch.IsEmpty);
    }

    public sealed class Doc
    {
        public List<string> Items { get; set; } = [];
    }
}
