using Dialoid.Core.Combat;
using Dialoid.Core.Data;
using Dialoid.Core.Tests.Fixtures;

namespace Dialoid.Core.Tests.Data;

public class DataLoaderTests
{
    [Fact]
    public void Loads_tiny_fixture_without_errors()
    {
        var r = DataLoader.Load(FixturePaths.Tiny);
        Assert.Empty(r.Errors);
        Assert.True(r.Ok);
        Assert.Equal(6, r.Data!.Ailments.Count);
        Assert.Contains("ailment_bleed", r.Data.Tags);
        Assert.Equal(8, r.Data.Stats["bleed_max_stacks"].Default);
    }

    [Fact]
    public void Real_data_loads_without_errors()
    {
        var r = DataLoader.Load(FixturePaths.RealData);
        Assert.Empty(r.Errors);
        Assert.Equal(100, r.Data!.Stats["crit_chance"].Max);
        Assert.Equal(-100, r.Data.Stats["resist_fire"].Min);
        Assert.Equal(75, r.Data.Stats["resist_fire_max"].Default);
        Assert.Equal(40, r.Data.Stats["shock_effect"].Max);
    }

    [Fact]
    public void Missing_file_reports_error_with_file_name()
    {
        var dir = FixturePaths.CopyTiny();
        File.Delete(Path.Combine(dir, "tags.json"));
        var r = DataLoader.Load(dir);
        Assert.False(r.Ok);
        var e = Assert.Single(r.Errors);
        Assert.Equal("tags.json", e.File);
        Assert.Contains("не найден", e.Message);
    }

    [Fact]
    public void Malformed_json_reports_error_not_exception()
    {
        var dir = FixturePaths.CopyTiny();
        File.WriteAllText(Path.Combine(dir, "stats.json"), "[ { \"id\": ");
        var r = DataLoader.Load(dir);
        Assert.False(r.Ok);
        Assert.Contains(r.Errors, e => e.File == "stats.json" && e.Message.StartsWith("Некорректный JSON"));
    }

    [Fact]
    public void Duplicate_id_reports_error_with_id()
    {
        var dir = FixturePaths.CopyTiny();
        File.WriteAllText(Path.Combine(dir, "tags.json"), "[{\"id\":\"fire\"},{\"id\":\"fire\"}]");
        var r = DataLoader.Load(dir);
        Assert.Contains(r.Errors, e => e.File == "tags.json" && e.Id == "fire" && e.Message.Contains("Повторяющийся id"));
    }

    [Fact]
    public void Unknown_stat_reference_in_ailment_is_reported()
    {
        var dir = FixturePaths.CopyTiny();
        var p = Path.Combine(dir, "ailments.json");
        File.WriteAllText(p, File.ReadAllText(p).Replace("bleed_chance", "nope_chance"));
        var r = DataLoader.Load(dir);
        Assert.Contains(r.Errors, e => e.Id == "bleed" && e.Message.Contains("nope_chance"));
    }

    [Theory]
    [InlineData("tiny")]
    [InlineData("real")]
    public void Ailments_table_matches_spec_defaults(string which)
    {
        var d = DataLoader.Load(which == "tiny" ? FixturePaths.Tiny : FixturePaths.RealData).Data!;
        void Check(string id, DamageType t, string chance, double frac, double dur, string stacks, StackRule rule)
        {
            var a = d.Ailments[id];
            Assert.Equal(new AilmentDefinition(id, t, chance, frac, dur, stacks, rule, "ailment_" + id), a);
        }
        Check("bleed", DamageType.Physical, "bleed_chance", 0.70, 5, "bleed_max_stacks", StackRule.ReplaceWeakest);
        Check("ignite", DamageType.Fire, "ignite_chance", 0.90, 4, "ignite_max_stacks", StackRule.ReplaceWeakest);
        Check("poison", DamageType.Poison, "poison_chance", 0.30, 3, "poison_max_stacks", StackRule.Unlimited);
        Check("shock", DamageType.Lightning, "shock_chance", 0, 3, "shock_max_stacks", StackRule.StrongestOnly);
        Check("chill", DamageType.Cold, "chill_chance", 0, 2, "chill_max_stacks", StackRule.StrongestOnly);
        Check("freeze", DamageType.Cold, "freeze_chance", 0, 1, "freeze_max_stacks", StackRule.StrongestOnly);
        Assert.Equal(100, d.Stats["chill_chance"].Default);
    }
}
