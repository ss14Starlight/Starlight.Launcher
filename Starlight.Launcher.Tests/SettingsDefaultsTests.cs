using System.Text.Json.Nodes;
using Starlight.Launcher.Services.Settings;
using Starlight.Launcher.WebUI.Models.LocalServer;
using Starlight.Launcher.WebUI.Models.Settings;

namespace Starlight.Launcher.Tests;

public sealed class SettingsDefaultsTests
{
    private static readonly Dictionary<string, JsonNode?[]> _noLegacy = [];
    private static readonly string[] _cdnHubAuth = ["Hubs", "AuthServerUrls", "RobustCdns"];

    private static JsonObject Obj(string json) => JsonNode.Parse(json)!.AsObject();

    [Fact]
    public void UnchangedDefault_NothingToDo()
    {
        var plan = SettingsDefaults.Reconcile(Obj("""{"A":1}"""), Obj("""{"A":1}"""), Obj("""{"A":1}"""), _noLegacy);

        Assert.Empty(plan.ApplySilently);
        Assert.Empty(plan.Conflicts);
        Assert.Equal(1, (int)plan.Baseline["A"]!);
    }

    [Fact]
    public void OwnValue_DefaultUnchanged_KeptWithoutAsking()
    {
        var plan = SettingsDefaults.Reconcile(Obj("""{"A":5}"""), Obj("""{"A":1}"""), Obj("""{"A":1}"""), _noLegacy);

        Assert.Empty(plan.ApplySilently);
        Assert.Empty(plan.Conflicts);
    }

    [Fact]
    public void OldDefault_DefaultChanged_AppliedSilently()
    {
        var plan = SettingsDefaults.Reconcile(Obj("""{"A":1}"""), Obj("""{"A":2}"""), Obj("""{"A":1}"""), _noLegacy);

        Assert.Equal(["A"], plan.ApplySilently);
        Assert.Empty(plan.Conflicts);
        Assert.Equal(2, (int)plan.Baseline["A"]!);
    }

    [Fact]
    public void OwnValue_DefaultChanged_Asked_AndBaselineKeptUntilDecided()
    {
        var plan = SettingsDefaults.Reconcile(Obj("""{"A":5}"""), Obj("""{"A":2}"""), Obj("""{"A":1}"""), _noLegacy);

        Assert.Empty(plan.ApplySilently);
        var conflict = Assert.Single(plan.Conflicts);
        Assert.Equal("A", conflict.Key);
        Assert.Equal("5", conflict.CurrentValue);
        Assert.Equal("2", conflict.NewDefault);
        Assert.Equal("1", conflict.OldDefault);
        Assert.Equal(1, (int)plan.Baseline["A"]!);
    }

    [Fact]
    public void NoBaseline_PastDefault_AppliedSilently()
    {
        var legacy = new Dictionary<string, JsonNode?[]> { ["A"] = [JsonValue.Create(1)] };
        var plan = SettingsDefaults.Reconcile(Obj("""{"A":1}"""), Obj("""{"A":2}"""), null, legacy);

        Assert.Equal(["A"], plan.ApplySilently);
    }

    [Fact]
    public void NoBaseline_OwnValueOfChangedSetting_AskedWithUnknownOldDefault()
    {
        var legacy = new Dictionary<string, JsonNode?[]> { ["A"] = [JsonValue.Create(1)] };
        var plan = SettingsDefaults.Reconcile(Obj("""{"A":5,"B":7}"""), Obj("""{"A":2,"B":3}"""), null, legacy);

        var conflict = Assert.Single(plan.Conflicts);
        Assert.Equal("A", conflict.Key);
        Assert.Null(conflict.OldDefault);
        Assert.False(plan.Baseline.ContainsKey("A"));
        // B never changed default, so 7 is the user's own choice.
        Assert.Equal(3, (int)plan.Baseline["B"]!);
    }

    [Fact]
    public void FreshDefaults_HaveNoConflictsWithThemselves()
    {
        var defaults = SettingsDefaults.DefaultsSnapshot();
        var plan = SettingsDefaults.Reconcile(SettingsDefaults.Snapshot(new AppSettings()), defaults, null, SettingsDefaults.LegacyDefaults);

        Assert.Empty(plan.ApplySilently);
        Assert.Empty(plan.Conflicts);
    }

    [Fact]
    public void StateSettings_AreNotTracked()
    {
        var keys = SettingsDefaults.TrackedKeys.ToHashSet();

        Assert.Contains(nameof(AppSettings.Hubs), keys);
        Assert.Contains(nameof(AppSettings.RobustCdns), keys);
        Assert.DoesNotContain(nameof(AppSettings.DirLauncherData), keys);
        Assert.DoesNotContain(nameof(AppSettings.LastSeenChangelogVersion), keys);
        Assert.DoesNotContain(nameof(AppSettings.PathContentDb), keys);
    }

    [Fact]
    public void OldPlaySs14Settings_UpdatedSilently()
    {
        var old = new AppSettings
        {
            Hubs = [new Hub { HubUri = new Uri("https://hub.playss14.com/"), Priority = 0 }],
            AuthServerUrls = ["https://auth.playss14.com/"],
            RobustCdns =
            [
                AppSettings.DefaultRobustCdns[0],
                new() { Name = "PlaySS14", Urls = ["https://robust-builds.playss14.com/"], PublicKey = AppSettings.DefaultRobustCdns[1].PublicKey },
            ],
        };

        var plan = SettingsDefaults.Reconcile(SettingsDefaults.Snapshot(old), SettingsDefaults.DefaultsSnapshot(), null, SettingsDefaults.LegacyDefaults);

        Assert.Empty(plan.Conflicts);
        Assert.Equivalent(_cdnHubAuth, plan.ApplySilently);

        var updated = SettingsDefaults.ApplyDefaults(old, plan.ApplySilently);
        Assert.Null(updated.RobustCdns);
        Assert.Equal("https://hub.spacestation14.com/", updated.Hubs[0].HubUri.ToString());
        Assert.Equal(["https://auth.spacestation14.com/"], updated.AuthServerUrls);
    }

    [Fact]
    public void CustomizedCdns_WithOldDefault_Asked()
    {
        var old = new AppSettings
        {
            RobustCdns =
            [
                AppSettings.DefaultRobustCdns[0],
                new() { Name = "PlaySS14", Urls = ["https://robust-builds.playss14.com/"], PublicKey = AppSettings.DefaultRobustCdns[1].PublicKey, Enabled = false },
                new() { Name = "Mine", Urls = ["https://example.com/"] },
            ],
        };

        var plan = SettingsDefaults.Reconcile(SettingsDefaults.Snapshot(old), SettingsDefaults.DefaultsSnapshot(), null, SettingsDefaults.LegacyDefaults);

        var conflict = Assert.Single(plan.Conflicts);
        Assert.Equal(nameof(AppSettings.RobustCdns), conflict.Key);
        Assert.Contains("cdn.spacestation14.com", conflict.NewDefault);
        Assert.Contains("example.com", conflict.CurrentValue);
    }

    [Fact]
    public void ApplyDefaults_KeepsOtherSettings()
    {
        var settings = new AppSettings { Theme = AppTheme.Midnight, AuthServerUrls = ["https://x/"] };

        var updated = SettingsDefaults.ApplyDefaults(settings, [nameof(AppSettings.AuthServerUrls)]);

        Assert.Equal(AppTheme.Midnight, updated.Theme);
        Assert.Equal(new AppSettings().AuthServerUrls, updated.AuthServerUrls);
        Assert.Equal(["https://x/"], settings.AuthServerUrls);
    }

    private static AppSettings Accept(AppSettings settings, DefaultSettingChange change)
        => SettingsDefaults.ApplyValues(settings, new Dictionary<string, JsonNode?> { [change.Key] = JsonNode.Parse(change.MergedValue) });

    private static LocalServerSourceConfig StarlightWithCVars() => new()
    {
        Name = "Starlight",
        Url = "https://cdn.starlight.network/fork/starlight/manifest",
        CVarOverrides = [new("log", "enabled", ServerCVarType.Bool, "false")],
    };

    [Fact]
    public void LocalSources_NoBaseline_AddsNewSourceAndKeepsOwnCVars()
    {
        var mine = new AppSettings { LocalServerSources = [StarlightWithCVars()] };

        var plan = SettingsDefaults.Reconcile(SettingsDefaults.Snapshot(mine), SettingsDefaults.DefaultsSnapshot(), null, SettingsDefaults.LegacyDefaults);

        var change = Assert.Single(plan.Conflicts);
        var updated = Accept(mine, change).LocalServerSources;
        Assert.Equal(["Starlight", "Wizdens"], updated.Select(s => s.Name));
        Assert.Single(updated[0].CVarOverrides);
    }

    [Fact]
    public void LocalSources_ChangedUrl_OnlyUrlChanges()
    {
        var oldDefault = new AppSettings
        {
            LocalServerSources = [new() { Name = "Starlight", Url = "https://cdn.starlight.network/fork/starlight/manifest" }, new() { Name = "Wizdens", Url = "https://old.example/manifest" }],
        };
        var mine = oldDefault with { LocalServerSources = [StarlightWithCVars(), oldDefault.LocalServerSources[1]] };
        var baseline = SettingsDefaults.DefaultsSnapshot();
        baseline[nameof(AppSettings.LocalServerSources)] = SettingsDefaults.Snapshot(oldDefault)[nameof(AppSettings.LocalServerSources)]!.DeepClone();

        var plan = SettingsDefaults.Reconcile(SettingsDefaults.Snapshot(mine), SettingsDefaults.DefaultsSnapshot(), baseline, _noLegacy);

        var change = Assert.Single(plan.Conflicts);
        var updated = Accept(mine, change).LocalServerSources;
        Assert.Equal("https://wizards.cdn.spacestation14.com/fork/wizards/manifest", updated[1].Url);
        Assert.Single(updated[0].CVarOverrides);
    }

    [Fact]
    public void Cdns_RenamedDefault_ReplacedInPlace_OwnCdnKept()
    {
        var mine = new AppSettings
        {
            RobustCdns =
            [
                AppSettings.DefaultRobustCdns[0],
                new() { Name = "PlaySS14", Urls = ["https://robust-builds.playss14.com/"], PublicKey = AppSettings.DefaultRobustCdns[1].PublicKey },
                new() { Name = "Mine", Urls = ["https://example.com/"] },
            ],
        };

        var plan = SettingsDefaults.Reconcile(SettingsDefaults.Snapshot(mine), SettingsDefaults.DefaultsSnapshot(), null, SettingsDefaults.LegacyDefaults);

        var updated = Accept(mine, Assert.Single(plan.Conflicts)).RobustCdns!;
        Assert.Equal(["Starlight", "Space Station 14", "Mine"], updated.Select(c => c.Name));
    }

    [Fact]
    public void Merge_UserRemovedItem_StaysRemoved_EvenIfDevChangedIt()
    {
        var b = JsonNode.Parse("""[{"Name":"a","V":1},{"Name":"b","V":1}]""");
        var m = JsonNode.Parse("""[{"Name":"a","V":1}]""");
        var t = JsonNode.Parse("""[{"Name":"a","V":1},{"Name":"b","V":2},{"Name":"c","V":1}]""");

        var merged = JsonMerge3.Merge(b, m, t);

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""[{"Name":"a","V":1},{"Name":"c","V":1}]"""), merged));
    }

    [Fact]
    public void Merge_PlainList_ReplacesRemovedAndKeepsOwn()
    {
        var merged = JsonMerge3.Merge(JsonNode.Parse("""["old"]"""), JsonNode.Parse("""["old","mine"]"""), JsonNode.Parse("""["new"]"""));

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""["new","mine"]"""), merged));
    }

    [Fact]
    public void Merge_BothChangedField_NewDefaultWins_OtherFieldsKept()
    {
        var merged = JsonMerge3.Merge(
            JsonNode.Parse("""{"Url":"a","Enabled":true}"""),
            JsonNode.Parse("""{"Url":"mine","Enabled":false}"""),
            JsonNode.Parse("""{"Url":"b","Enabled":true}"""));

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{"Url":"b","Enabled":false}"""), merged));
    }

    [Fact]
    public void ChangeNotTouchingOwnValue_NotAsked()
    {
        var plan = SettingsDefaults.Reconcile(
            Obj("""{"L":[{"Name":"a"}]}"""),
            Obj("""{"L":[{"Name":"a"},{"Name":"b","V":2}]}"""),
            Obj("""{"L":[{"Name":"a"},{"Name":"b","V":1}]}"""),
            _noLegacy);

        Assert.Empty(plan.Conflicts);
        Assert.Empty(plan.ApplySilently);
    }
}
