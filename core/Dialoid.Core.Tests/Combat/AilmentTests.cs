using Dialoid.Core.Character;
using Dialoid.Core.Combat;
using Dialoid.Core.Data;
using Dialoid.Core.Effects;
using Dialoid.Core.Simulation;
using Dialoid.Core.Skills;
using Dialoid.Core.Stats;

namespace Dialoid.Core.Tests.Combat;

public class AilmentTests
{
    private static readonly Dictionary<string, StatDefinition> Defs = BuildDefs();

    private static Dictionary<string, StatDefinition> BuildDefs()
    {
        var d = new Dictionary<string, StatDefinition>();
        void Add(string id, double def, double min, double max) => d[id] = new StatDefinition(id, def, min, max, StatDisplay.Number);
        Add("damage", 0, 0, 1e9);
        Add("shock_effect", 10, 0, 40);
        Add("freeze_duration", 0, 0, 1e9);
        foreach (var t in Enum.GetValues<DamageType>())
            Add($"penetration_{t.ToString().ToLowerInvariant()}", 0, 0, 1e9);
        return d;
    }

    private static Modifier Mod(string stat, ModKind k, double v, params string[] tags) =>
        new(stat, k, v, TagSet.Of(tags), Array.Empty<Condition>(), null, new SourceRef("test", "t", "t"));

    private static CompiledCharacter Char(params Modifier[] mods) =>
        new(new Build { Level = 1 }, null!, new StatEvaluator(Defs, mods), mods, Array.Empty<CompiledSkill>(),
            Array.Empty<MechanicEffect>(), Array.Empty<SourcedEffect>());

    private static CompiledSkill Skill(params string[] tags)
    {
        var b = new SkillBase(new Dictionary<DamageType, (double, double)>(), null, 100, 1, 1, null, 0, 0, 0, 1,
            new Dictionary<string, double>(), new Dictionary<string, double>(), Array.Empty<string>());
        return new CompiledSkill("s", 1, false, TagSet.Of(tags), null, b, new Dictionary<string, double>(),
            Array.Empty<Conversion>(), 0, 0, Array.Empty<Modifier>(), Array.Empty<GrantProcEffect>(),
            Array.Empty<MechanicEffect>(), new Dictionary<string, int>());
    }

    private static readonly AilmentDefinition Ignite = new("ignite", DamageType.Fire, "ignite_chance", 0.9, 4, "ignite_max_stacks", StackRule.ReplaceWeakest, "ailment_ignite");
    private static readonly AilmentDefinition Bleed = new("bleed", DamageType.Physical, "bleed_chance", 0.7, 5, "bleed_max_stacks", StackRule.ReplaceWeakest, "ailment_bleed");
    private static readonly AilmentDefinition Poison = new("poison", DamageType.Poison, "poison_chance", 0.3, 3, "poison_max_stacks", StackRule.Unlimited, "ailment_poison");
    private static readonly AilmentDefinition Shock = new("shock", DamageType.Lightning, "shock_chance", 0, 3, "shock_max_stacks", StackRule.StrongestOnly, "ailment_shock");
    private static readonly AilmentDefinition Chill = new("chill", DamageType.Cold, "chill_chance", 0, 2, "chill_max_stacks", StackRule.StrongestOnly, "ailment_chill");
    private static readonly AilmentDefinition Freeze = new("freeze", DamageType.Cold, "freeze_chance", 0, 1, "freeze_max_stacks", StackRule.StrongestOnly, "ailment_freeze");

    private static TargetDefense Def(double armor = 0, double fire = 0, double phys = 0, double taken = 0) =>
        new(armor, new Dictionary<DamageType, double> { [DamageType.Fire] = fire, [DamageType.Physical] = phys }, taken);

    private static AilmentInstance Ig(double dps, double rem = 4) => new("ignite", DamageType.Fire, dps, rem, "s");

    [Fact]
    public void Ignite_dps_is_90_percent_over_4_seconds()
        => Assert.Equal(22.5, AilmentCalculator.DotDps(100, Ignite, Char(), Skill("fire"), Def()), 9);

    [Fact]
    public void Ailment_ignores_delivery_tag_modifiers()
    {
        var sk = Skill("spell", "fire");
        Assert.Equal(22.5, AilmentCalculator.DotDps(100, Ignite, Char(Mod("damage", ModKind.Increased, 50, "spell")), sk, Def()), 9);
        Assert.Equal(33.75, AilmentCalculator.DotDps(100, Ignite, Char(Mod("damage", ModKind.Increased, 50, "fire")), sk, Def()), 9);
    }

    [Fact]
    public void Ailment_gets_skill_tree_modifiers_and_primary_more()
    {
        var ch = Char(Mod("damage", ModKind.Increased, 20, "skill:s"), Mod("damage", ModKind.More, 10), Mod("damage", ModKind.Increased, 100, "dot"));
        Assert.Equal(22.5 * 2.2 * 1.1, AilmentCalculator.DotDps(100, Ignite, ch, Skill("fire"), Def()), 9);
    }

    [Fact]
    public void Bleed_ignores_armor_but_not_resist()
    {
        Assert.Equal(14, AilmentCalculator.DotDps(100, Bleed, Char(), Skill(), Def(armor: 10000)), 9);
        Assert.Equal(14 * 0.5, AilmentCalculator.DotDps(100, Bleed, Char(), Skill(), Def(phys: 50)), 9);
    }

    [Fact]
    public void Dot_respects_resist_penetration_and_damage_taken()
    {
        var ch = Char(Mod("penetration_fire", ModKind.Flat, 20));
        Assert.Equal(22.5 * 0.7 * 1.5, AilmentCalculator.DotDps(100, Ignite, ch, Skill("fire"), Def(fire: 50, taken: 50)), 9);
    }

    [Fact]
    public void Ignite_replaces_weakest_when_at_max_stacks()
    {
        var t = new TargetState();
        foreach (var d in new[] { 10.0, 20, 30 }) t.Apply(Ig(d), 3, StackRule.ReplaceWeakest);
        t.Apply(Ig(25), 3, StackRule.ReplaceWeakest);
        Assert.Equal(new[] { 20.0, 25, 30 }, t.Ailments.Select(a => a.DamagePerSec).OrderBy(x => x));
        t.Apply(Ig(5), 3, StackRule.ReplaceWeakest);
        Assert.Equal(new[] { 20.0, 25, 30 }, t.Ailments.Select(a => a.DamagePerSec).OrderBy(x => x));
    }

    [Fact]
    public void Bleed_stacks_to_8_poison_unlimited()
    {
        var t = new TargetState();
        for (var i = 1; i <= 12; i++)
        {
            t.Apply(new AilmentInstance("bleed", DamageType.Physical, i, 5, "s"), 8, StackRule.ReplaceWeakest);
            t.Apply(new AilmentInstance("poison", DamageType.Poison, 1, 3, "s"), 8, StackRule.Unlimited);
        }
        Assert.Equal(8, t.Ailments.Count(a => a.AilmentId == "bleed"));
        Assert.Equal(5, t.Ailments.Where(a => a.AilmentId == "bleed").Min(a => a.DamagePerSec));
        Assert.Equal(12, t.Ailments.Count(a => a.AilmentId == "poison"));
    }

    [Fact]
    public void Shock_strongest_only_and_capped_at_40()
    {
        Assert.Equal(10, AilmentCalculator.ShockMagnitude(Char(), Skill("lightning")));
        Assert.Equal(40, AilmentCalculator.ShockMagnitude(Char(Mod("shock_effect", ModKind.Flat, 500)), Skill("lightning")));
        var t = new TargetState();
        AilmentInstance Sh(double m) => new("shock", DamageType.Lightning, 0, 3, "s") { Magnitude = m };
        t.Apply(Sh(20), 1, StackRule.StrongestOnly);
        t.Apply(Sh(10), 1, StackRule.StrongestOnly);
        Assert.Single(t.Ailments);
        Assert.Equal(20, t.DamageTakenIncreased);
        t.Apply(Sh(40), 1, StackRule.StrongestOnly);
        Assert.Single(t.Ailments);
        Assert.Equal(40, t.DamageTakenIncreased);
    }

    [Fact]
    public void Freeze_then_three_second_immunity()
    {
        var t = new TargetState();
        var f = new AilmentInstance("freeze", DamageType.Cold, 0, 1, "s");
        t.Apply(f, 1, StackRule.StrongestOnly);
        Assert.True(t.IsFrozen);
        t.Apply(f, 1, StackRule.StrongestOnly);
        Assert.Equal(1, t.FreezeRemaining);
        t.Tick(1.0);
        Assert.False(t.IsFrozen);
        Assert.Equal(3, t.FreezeImmunityRemaining, 9);
        t.Apply(f, 1, StackRule.StrongestOnly);
        Assert.False(t.IsFrozen);
        t.Tick(3.0);
        Assert.Equal(0, t.FreezeImmunityRemaining, 9);
        t.Apply(f, 1, StackRule.StrongestOnly);
        Assert.True(t.IsFrozen);
    }

    [Fact]
    public void Boss_freeze_duration_halved()
    {
        var t = new TargetState { FreezeDurationMultiplier = 0.5 };
        t.Apply(new AilmentInstance("freeze", DamageType.Cold, 0, 1, "s"), 1, StackRule.StrongestOnly);
        Assert.Equal(0.5, t.FreezeRemaining, 9);
        Assert.Equal(1.5, AilmentCalculator.FreezeDuration(Freeze, Char(Mod("freeze_duration", ModKind.Increased, 50)), Skill("cold")), 9);
    }

    [Fact]
    public void Ground_zone_keeps_skill_tags_and_uses_ailment_base()
    {
        var sk = Skill("spell", "area", "fire");
        var ch = Char(Mod("damage", ModKind.Increased, 50, "spell"), Mod("damage", ModKind.Increased, 100, "dot"));
        Assert.Equal(100 * 0.25 * 2.5, AilmentCalculator.GroundZoneDps(100, 0.25, ch, sk, Def(), DamageType.Fire), 9);
        Assert.Equal(25 * 0.5, AilmentCalculator.GroundZoneDps(100, 0.25, Char(), sk, Def(fire: 50), DamageType.Fire), 9);
    }

    [Fact]
    public void Chill_always_applied_on_cold_damage()
    {
        var i = AilmentCalculator.MakeInstance(Chill, 0, Char(), Skill("cold"), Def());
        Assert.Equal("chill", i.AilmentId);
        Assert.Equal(15, i.Magnitude);
        Assert.Equal(2, i.Remaining);
        var t = new TargetState();
        t.Apply(i, 1, StackRule.StrongestOnly);
        Assert.True(t.HasAilment("chill"));
        t.Tick(2.5);
        Assert.False(t.HasAilment("chill"));
    }

    [Fact]
    public void Tick_returns_dot_damage_and_removes_expired()
    {
        var t = new TargetState();
        t.Apply(Ig(10, 1), 3, StackRule.ReplaceWeakest);
        Assert.Equal(10 * 0.5, t.Tick(0.5), 9);
        Assert.Equal(10 * 0.5, t.Tick(2), 9);
        Assert.Empty(t.Ailments);
    }
}
