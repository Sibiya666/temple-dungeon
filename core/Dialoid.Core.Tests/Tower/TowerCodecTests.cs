using Dialoid.Core.Simulation;
using Dialoid.Core.Tests.Fixtures;
using Dialoid.Core.Tower;

namespace Dialoid.Core.Tests.Tower;

public class TowerCodecTests
{
    private static readonly TowerData D = TowerDataLoader.Load(FixturePaths.TowerFixture).Data!;

    private static TowerSpec Sample()
    {
        var a = new TowerAssembly(D, 4, RandomSources.Create(SimMode.Roll, 11));
        a.Shuffle(new Wallet(D));
        var t = a.Spec;
        int slot = Enumerable.Range(0, 5).First(i => t.Floors[i].TypeId != "bosses");
        a.SetFloorMods(slot, new[] { new ModInstance("monster_life", 2, 33.123456789), new ModInstance("no_healing", 1, 0) });
        a.SetTowerMods(new[] { new ModInstance("tower_gate_life", 2, 12.5) });
        a.SetUberBoss("baal");
        return a.Spec;
    }

    [Theory]
    [InlineData("escort", "escort_hunters")]
    [InlineData("hold", "hold_decay")]
    [InlineData("ritual", "ritual_extra_point")]
    [InlineData("levers", "levers_more_needed")]
    public void Legacy_trial_preserves_its_objective_biome_and_rolled_mod(string objective, string modId)
    {
        var data = TowerDataLoader.Load(FixturePaths.RealTowerData).Data!;
        var tier = data.Mod(modId).Tiers[0];
        var mod = new ModInstance(modId, tier.Tier, tier.Max);
        var tower = new TowerAssembly(data, 5, RandomSources.Create(SimMode.Roll, 1)).Spec;
        var floors = tower.Floors.ToArray();
        floors[0] = new FloorSpec("trial", objective, "crypt", new[] { mod });
        var legacy = TowerCodec.ToJson(tower with { Floors = floors });
        Assert.True(TowerCodec.TryFromJson(legacy, data, out var migrated, out var error), error);
        var floor = migrated!.Floors[0];
        Assert.Equal(objective, floor.TypeId);
        Assert.Null(floor.Variant);
        Assert.Equal("crypt", floor.BiomeId);
        Assert.Equal(mod, Assert.Single(floor.Mods));
        Assert.DoesNotContain("\"trial\"", TowerCodec.ToJson(migrated));
    }

    private static string? Error(string json)
    {
        Assert.False(TowerCodec.TryFromJson(json, D, out var spec, out var error));
        Assert.Null(spec);
        Assert.False(string.IsNullOrWhiteSpace(error));
        return error;
    }

    private static string Replace(string json, string from, string to)
    {
        Assert.Contains(from, json);
        return json.Replace(from, to);
    }

    [Fact]
    public void Codec_round_trips_and_rejects_unknown_ids()
    {
        var spec = Sample();
        string json = TowerCodec.ToJson(spec);
        Assert.Contains("\"uber_boss_id\"", json);
        Assert.Contains("\"tower_mods\"", json);

        Assert.True(TowerCodec.TryFromJson(json, D, out var back, out var err), err);
        Assert.Null(err);
        Assert.Equal(json, TowerCodec.ToJson(back!));
        Assert.Equal(spec.Floors.Select(f => f.Mods.Count), back!.Floors.Select(f => f.Mods.Count));
        Assert.Equal(33.123456789, back.Floors.SelectMany(f => f.Mods).First().Value);

        var t0 = spec.Floors[0];
        Error(Replace(json, $"\"type_id\":\"{t0.TypeId}\"", "\"type_id\":\"nope\""));
        Error(Replace(json, $"\"biome_id\":\"{t0.BiomeId}\"", "\"biome_id\":\"moon\""));
        Error(Replace(json, "\"mod_id\":\"monster_life\"", "\"mod_id\":\"nope\""));
        Error(Replace(json, "\"mod_id\":\"tower_gate_life\"", "\"mod_id\":\"monster_life\""));
        Error(Replace(json, "\"mod_id\":\"tower_gate_life\",\"tier\":2", "\"mod_id\":\"tower_gate_life\",\"tier\":6"));
        Error(Replace(json, "\"rank\":4", "\"rank\":4,\"extra\":1"));
        Error(Replace(json, "\"rank\":4", "\"rank\":16"));
        Error("{ not json");
        Error("[]");
    }

    [Fact]
    public void Codec_checks_variants()
    {
        var floors = new[]
        {
            new FloorSpec("trial", "escort", "crypt", Array.Empty<ModInstance>()),
            new FloorSpec("clear", null, "swamp", Array.Empty<ModInstance>()),
            new FloorSpec(null, null, null, Array.Empty<ModInstance>()),
            new FloorSpec(null, null, null, Array.Empty<ModInstance>()),
            new FloorSpec(null, null, null, Array.Empty<ModInstance>()),
        };
        string json = TowerCodec.ToJson(new TowerSpec(2, floors, Array.Empty<ModInstance>(), null));
        Assert.True(TowerCodec.TryFromJson(json, D, out var back, out _));
        Assert.Equal("escort", back!.Floors[0].Variant);
        Assert.Null(back.UberBossId);

        Error(Replace(json, "\"variant\":\"escort\"", "\"variant\":\"dance\""));
        Error(Replace(json, "\"variant\":\"escort\"", "\"variant\":null"));
        Error(Replace(json, "\"type_id\":\"clear\",\"variant\":null", "\"type_id\":\"clear\",\"variant\":\"escort\""));
    }

    [Fact]
    public void Codec_requires_all_floors()
    {
        var floors = new[] { new FloorSpec("clear", null, "swamp", Array.Empty<ModInstance>()) };
        Error(TowerCodec.ToJson(new TowerSpec(2, floors, Array.Empty<ModInstance>(), null)));
    }
}
