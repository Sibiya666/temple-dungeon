using Dialoid.Core.Combat;
using Dialoid.Core.Data;
using Dialoid.Core.Effects;
using Dialoid.Core.Skills;
using Dialoid.Core.Stats;
using Dialoid.Core.Tests.Fixtures;
using System.Text.Json.Nodes;

namespace Dialoid.Core.Tests.Combat;

public class ConversionTests
{
    private static DamagePortion P(DamageType t, double min, double max) => new(t, min, max, new HashSet<DamageType> { t });

    private static Conversion C(DamageType f, DamageType t, double pct) => new(f, t, pct, new SourceRef("passive", "n", "n"));

    [Fact]
    public void Conversion_moves_damage_rightwards()
    {
        var r = ConversionResolver.Apply(new[] { P(DamageType.Physical, 100, 200) }, new[] { C(DamageType.Physical, DamageType.Fire, 60) });
        var phys = r.Single(x => x.Type == DamageType.Physical);
        var fire = r.Single(x => x.Type == DamageType.Fire);
        Assert.Equal(40, phys.Min, 9);
        Assert.Equal(80, phys.Max, 9);
        Assert.Equal(60, fire.Min, 9);
        Assert.Equal(120, fire.Max, 9);
        Assert.True(fire.Lineage.SetEquals(new[] { DamageType.Physical, DamageType.Fire }));
        Assert.True(phys.Lineage.SetEquals(new[] { DamageType.Physical }));
    }

    [Fact]
    public void Conversion_over_100_scales_proportionally()
    {
        var r = ConversionResolver.Apply(new[] { P(DamageType.Physical, 100, 100) },
            new[] { C(DamageType.Physical, DamageType.Fire, 60), C(DamageType.Physical, DamageType.Lightning, 60) });
        Assert.DoesNotContain(r, x => x.Type == DamageType.Physical);
        Assert.Equal(50, r.Single(x => x.Type == DamageType.Fire).Min, 9);
        Assert.Equal(50, r.Single(x => x.Type == DamageType.Lightning).Min, 9);
    }

    [Fact]
    public void Conversion_chain_keeps_full_lineage()
    {
        var r = ConversionResolver.Apply(new[] { P(DamageType.Physical, 100, 100) },
            new[] { C(DamageType.Lightning, DamageType.Fire, 50), C(DamageType.Physical, DamageType.Lightning, 100) });
        Assert.DoesNotContain(r, x => x.Type == DamageType.Physical);
        var fire = r.Single(x => x.Type == DamageType.Fire);
        var lightning = r.Single(x => x.Type == DamageType.Lightning);
        Assert.Equal(50, fire.Min, 9);
        Assert.Equal(50, lightning.Min, 9);
        Assert.True(fire.Lineage.SetEquals(new[] { DamageType.Physical, DamageType.Lightning, DamageType.Fire }));
        Assert.True(lightning.Lineage.SetEquals(new[] { DamageType.Physical, DamageType.Lightning }));
    }

    [Fact]
    public void Native_and_converted_portions_of_same_type_stay_separate_by_lineage()
    {
        var r = ConversionResolver.Apply(new[] { P(DamageType.Physical, 100, 100), P(DamageType.Fire, 10, 10) },
            new[] { C(DamageType.Physical, DamageType.Fire, 100) });
        Assert.Equal(2, r.Count);
        Assert.Equal(110, r.Sum(x => x.Min), 9);
    }

    [Fact]
    public void No_conversions_returns_portions_unchanged()
    {
        var r = ConversionResolver.Apply(new[] { P(DamageType.Cold, 5, 6) }, Array.Empty<Conversion>());
        Assert.Equal(5, Assert.Single(r).Min);
    }

    [Fact]
    public void Resolver_throws_on_leftward_conversion() =>
        Assert.Throws<ArgumentException>(() =>
            ConversionResolver.Apply(new[] { P(DamageType.Fire, 1, 1) }, new[] { C(DamageType.Fire, DamageType.Cold, 50) }));

    [Fact]
    public void Leftward_conversion_is_rejected_by_validator()
    {
        var dir = FixturePaths.CopyTiny();
        var path = Path.Combine(dir, "skills.json");
        var arr = JsonNode.Parse(File.ReadAllText(path))!.AsArray();
        var node = arr[0]!["tree"]!.AsArray()[0]!;
        node["effects"] = JsonNode.Parse("[{\"type\":\"add_conversion\",\"from\":\"fire\",\"to\":\"cold\",\"percent\":30}]");
        File.WriteAllText(path, arr.ToJsonString());
        var res = DataLoader.Load(dir);
        Assert.Null(res.Data);
        Assert.Contains(res.Errors, e => e.File == "skills.json");
    }

    [Fact]
    public void Dark_is_last_in_conversion_order()
    {
        Assert.Equal(5, (int)DamageType.Dark);
        Assert.Equal(DamageType.Dark, Enum.GetValues<DamageType>().Last());
    }

    private static MechanicSkillDraft Draft()
    {
        var b = new SkillBase(new Dictionary<DamageType, (double, double)>(), null, 100, 1, 1, null, 0, 0, 0, 1,
            new Dictionary<string, double>(), new Dictionary<string, double>(), Array.Empty<string>());
        var skill = new CompiledSkill("s", 1, false, TagSet.Of(), null, b, new Dictionary<string, double>(),
            Array.Empty<Conversion>(), 0, 0, Array.Empty<Modifier>(), Array.Empty<GrantProcEffect>(),
            Array.Empty<MechanicEffect>(), new Dictionary<string, int>());
        var data = DataLoader.Load(FixturePaths.Tiny).Data!;
        return new MechanicSkillDraft(skill, new StatEvaluator(data.Stats, Array.Empty<Modifier>()), data);
    }

    [Fact]
    public void Any_type_converts_to_dark_and_dark_never_out()
    {
        var src = new SourceRef("passive", "n", "n");
        var draft = Draft();
        foreach (var from in new[] { DamageType.Physical, DamageType.Fire, DamageType.Poison })
            draft.AddConversion(from, DamageType.Dark, 50, src);
        Assert.Throws<ArgumentException>(() => draft.AddConversion(DamageType.Dark, DamageType.Fire, 50, src));
        Assert.Throws<ArgumentException>(() => draft.AddConversion(DamageType.Dark, DamageType.Dark, 50, src));

        var r = ConversionResolver.Apply(new[] { P(DamageType.Poison, 100, 100) }, new[] { C(DamageType.Poison, DamageType.Dark, 40) });
        Assert.Equal(40, r.Single(x => x.Type == DamageType.Dark).Min, 9);
        Assert.Equal(60, r.Single(x => x.Type == DamageType.Poison).Min, 9);
    }

    [Fact]
    public void IsElemental_only_fire_cold_lightning()
    {
        foreach (var t in Enum.GetValues<DamageType>())
            Assert.Equal(t is DamageType.Fire or DamageType.Cold or DamageType.Lightning, DamageTypes.IsElemental(t));
    }
}
