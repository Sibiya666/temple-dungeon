using System.Text.Json;
using System.Text.Json.Nodes;
using Dialoid.Core.Character;
using Dialoid.Core.Combat;
using Dialoid.Core.Data;
using Dialoid.Core.Effects;
using Dialoid.Core.Skills;
using Dialoid.Core.Stats;
using Dialoid.Core.Tests.Character;
using Dialoid.Core.Tests.Fixtures;

namespace Dialoid.Core.Tests.Skills;

public class SkillCompilerTests
{
    private static readonly GameData Data = DataLoader.Load(FixturePaths.Tiny).Data!;
    private static readonly Scope.Global G = new();

    private static IReadOnlyList<SourcedEffect> Src(IReadOnlyList<Effect>? e, string kind) =>
        (e ?? Array.Empty<Effect>()).Select(x => new SourcedEffect(x, new SourceRef(kind, kind + "_node", "N"))).ToList();

    private static CompilationContext Ctx(IReadOnlyList<Effect>? asc = null, IReadOnlyList<Effect>? key = null,
        IReadOnlyList<Effect>? passive = null, IReadOnlyList<(string, string)>? granted = null, string? classId = "test_mage") =>
        new(Data, Src(asc, "ascendancy"), Src(key, "keystone"), Src(passive, "passive"),
            granted ?? Array.Empty<(string, string)>(), classId);

    private static SkillTreeNode Node(string id, int max, string? group, params Effect[] effects) =>
        new(id, id, "", max, Array.Empty<(string, int)>(), group, effects);

    private static SkillDefinition Skill(string id, TagSet tags, double cost = 10, double gain = 0,
        Dictionary<string, double>? behavior = null, params SkillTreeNode[] tree)
    {
        var b = new SkillBase(new Dictionary<DamageType, (double, double)> { [DamageType.Fire] = (10, 20) }, null, 100, 1, 1, null,
            cost, gain, 5, 1, behavior ?? new Dictionary<string, double>(), new Dictionary<string, double>(), Array.Empty<string>());
        return new SkillDefinition(id, id, "", "test_mage", 1, tags, b, tree, false);
    }

    private static SkillSlot Slot(string id, int level, params (string, int)[] pts)
    {
        var s = new SkillSlot { SkillId = id, Level = level };
        foreach (var (n, p) in pts) s.NodePoints[n] = p;
        return s;
    }

    private static Modifier Mod(double v, string stat = "damage", ModKind k = ModKind.Increased) =>
        new(stat, k, v, TagSet.Empty, Array.Empty<Condition>(), null, new SourceRef("skill_node", "n", "n"));

    private static Scope Own(string id) => new Scope.Skill(id);

    [Fact]
    public void Effects_apply_in_order_tree_then_ascendancy_then_keystone()
    {
        var def = Skill("s", TagSet.Of("spell", "fire"), tree: Node("n", 1, null, new SetDamageTypeEffect(DamageType.Cold, Own("s"))));
        var slot = Slot("s", 5, ("n", 1));
        var asc = new Effect[] { new SetDamageTypeEffect(DamageType.Lightning, Own("s")) };
        Assert.Equal(DamageType.Lightning, SkillCompiler.Compile(slot, def, Ctx(asc: asc)).DamageTypeOverride);
        var key = new Effect[] { new SetDamageTypeEffect(DamageType.Poison, G) };
        Assert.Equal(DamageType.Poison, SkillCompiler.Compile(slot, def, Ctx(asc: asc, key: key)).DamageTypeOverride);
        // passive comes before ascendancy
        var pas = new Effect[] { new SetDamageTypeEffect(DamageType.Physical, Own("s")) };
        Assert.Equal(DamageType.Lightning, SkillCompiler.Compile(slot, def, Ctx(asc: asc, passive: pas)).DamageTypeOverride);
    }

    [Fact]
    public void SetDamageType_replaces_base_type_and_tag()
    {
        var bolt = Data.Skills["test_bolt"];
        var plain = SkillCompiler.Compile(Slot("test_bolt", 5), bolt, Ctx());
        Assert.Null(plain.DamageTypeOverride);
        Assert.True(plain.Tags.Contains("fire"));
        var cold = new Effect[] { new SetDamageTypeEffect(DamageType.Cold, Own("test_bolt")) };
        var c = SkillCompiler.Compile(Slot("test_bolt", 5), bolt, Ctx(asc: cold));
        Assert.Equal(DamageType.Cold, c.DamageTypeOverride);
        Assert.True(c.Tags.Contains("cold"));
        Assert.False(c.Tags.Contains("fire"));
        Assert.True(c.Tags.Contains("spell"));
    }

    [Fact]
    public void PerPoint_effects_scale_with_points()
    {
        var def = Skill("s", TagSet.Of("spell"), tree: Node("n", 3, null,
            new ModifierEffect(Mod(8), Own("s"), true),
            new AddBehaviorEffect("chain", 1, Own("s"), true),
            new AddConversionEffect(DamageType.Physical, DamageType.Fire, 10, Own("s"), true),
            new ModifierEffect(Mod(5, "crit_chance", ModKind.Flat), Own("s"), false)));
        var c = SkillCompiler.Compile(Slot("s", 5, ("n", 3)), def, Ctx());
        Assert.Equal(24, c.Modifiers.Single(m => m.Stat == "damage").Value);
        Assert.Equal(5, c.Modifiers.Single(m => m.Stat == "crit_chance").Value);
        Assert.Equal(3, c.Behavior["chain"]);
        Assert.Equal(30, c.Conversions.Single().Percent);
        Assert.Equal("n", c.Conversions.Single().Source.Id);
        Assert.True(c.Modifiers.All(m => m.RequiredTags.Contains("skill:s")));
    }

    [Fact]
    public void Tag_scoped_passive_reaches_matching_skills_only()
    {
        var eff = new Effect[] { new ModifierEffect(Mod(30), new Scope.Tags(TagSet.Of("spell", "fire")), false) };
        var bolt = SkillCompiler.Compile(Slot("test_bolt", 5), Data.Skills["test_bolt"], Ctx(passive: eff));
        var spark = SkillCompiler.Compile(Slot("test_spark", 5), Data.Skills["test_spark"], Ctx(passive: eff));
        var m = Assert.Single(bolt.Modifiers);
        Assert.True(m.RequiredTags.ContainsAll(TagSet.Of("spell", "fire")));
        Assert.Empty(spark.Modifiers);
        // skill-scoped effect from a passive only hits the named skill
        var one = new Effect[] { new AddBehaviorEffect("chain", 2, Own("test_spark")) };
        Assert.False(SkillCompiler.Compile(Slot("test_bolt", 5), Data.Skills["test_bolt"], Ctx(passive: one)).Behavior.ContainsKey("chain"));
        Assert.Equal(2, SkillCompiler.Compile(Slot("test_spark", 5), Data.Skills["test_spark"], Ctx(passive: one)).Behavior["chain"]);
    }

    [Fact]
    public void Tag_scope_uses_tags_changed_by_the_tree()
    {
        var def = Skill("s", TagSet.Of("spell"), tree: Node("n", 1, null, new AddTagEffect("fire", Own("s"))));
        var eff = new Effect[] { new ModifierEffect(Mod(30), new Scope.Tags(TagSet.Of("fire")), false) };
        Assert.Single(SkillCompiler.Compile(Slot("s", 5, ("n", 1)), def, Ctx(passive: eff)).Modifiers);
        Assert.Empty(SkillCompiler.Compile(Slot("s", 5), def, Ctx(passive: eff)).Modifiers);
    }

    [Fact]
    public void Granted_node_takes_exclusive_group_and_refunds_other()
    {
        var def = Skill("s", TagSet.Of("spell"), tree: new[]
        {
            Node("a", 3, null),
            Node("b1", 1, "B", new ModifierEffect(Mod(7), Own("s"), true)),
            Node("b2", 1, "B", new ModifierEffect(Mod(9, "crit_chance", ModKind.Flat), Own("s"), true)),
        });
        var slot = Slot("s", 5, ("a", 2), ("b2", 1));
        var before = SkillCompiler.Compile(slot, def, Ctx());
        Assert.Equal(2, before.FreePoints);
        var c = SkillCompiler.Compile(slot, def, Ctx(granted: new[] { ("s", "b1") }));
        Assert.Equal(1, c.EffectiveNodePoints["b1"]);
        Assert.False(c.EffectiveNodePoints.ContainsKey("b2"));
        Assert.Equal(2, c.EffectiveNodePoints["a"]);
        Assert.Equal(3, c.FreePoints);
        Assert.Contains(c.Modifiers, m => m.Value == 7);
        Assert.DoesNotContain(c.Modifiers, m => m.Stat == "crit_chance");
        // a grant for another skill is ignored
        Assert.Equal(2, SkillCompiler.Compile(slot, def, Ctx(granted: new[] { ("other", "b1") })).FreePoints);
    }

    [Fact]
    public void RemoveTag_area_removes_area_behaviour()
    {
        var def = Skill("s", TagSet.Of("spell", "area"), behavior: new() { ["area_radius"] = 4, ["projectile_count"] = 1 },
            tree: Node("n", 1, null, new RemoveTagEffect("area", Own("s"))));
        var c = SkillCompiler.Compile(Slot("s", 5, ("n", 1)), def, Ctx());
        Assert.False(c.Tags.Contains("area"));
        Assert.False(c.Behavior.ContainsKey("area_radius"));
        Assert.Equal(1, c.Behavior["projectile_count"]);
        var cloud = SkillCompiler.Compile(Slot("test_cloud", 5), Data.Skills["test_cloud"],
            Ctx(asc: new Effect[] { new RemoveTagEffect("area", Own("test_cloud")) }, classId: "test_mage"));
        Assert.False(cloud.Behavior.ContainsKey("area_radius"));
        Assert.True(cloud.Behavior.ContainsKey("duration"));
    }

    [Fact]
    public void Every_skill_has_skill_id_tag()
    {
        foreach (var def in Data.Skills.Values)
            Assert.True(SkillCompiler.Compile(Slot(def.Id, 3), def, Ctx()).Tags.Contains("skill:" + def.Id));
    }

    [Fact]
    public void Set_resource_replaces_cost_and_gain_and_loot_flag_follows_class()
    {
        var def = Skill("s", TagSet.Of("spell"), cost: 10, gain: 2, tree: Node("n", 1, null, new SetResourceEffect(ResourceField.Cost, 0, Own("s"))));
        var c = SkillCompiler.Compile(Slot("s", 5, ("n", 1)), def, Ctx(key: new Effect[] { new SetResourceEffect(ResourceField.Gain, 7, Own("s")) }));
        Assert.Equal(0, c.ResourceCost);
        Assert.Equal(7, c.ResourceGain);
        Assert.False(SkillCompiler.Compile(Slot("test_bolt", 1), Data.Skills["test_bolt"], Ctx()).IsLoot);
        Assert.True(SkillCompiler.Compile(Slot("test_cloud", 1), Data.Skills["test_cloud"], Ctx()).IsLoot);
    }

    [Fact]
    public void Procs_and_mechanics_land_on_skill_and_grant_node_mechanic_is_consumed()
    {
        var proc = new GrantProcEffect(new Trigger.OnHit(), new ProcAction.ApplyAilment("ignite"), Own("s"));
        var mech = new MechanicEffect("some_mech", new Dictionary<string, JsonElement>(), Own("s"));
        var def = Skill("s", TagSet.Of("spell"), tree: Node("n", 1, null, proc, mech));
        var c = SkillCompiler.Compile(Slot("s", 5, ("n", 1)), def, Ctx());
        Assert.Equal(proc, Assert.Single(c.Procs));
        Assert.Equal(mech, Assert.Single(c.Mechanics));
    }

    private sealed class GrantStub : IMechanic { public string Id => "grant_skill_node"; }

    [Fact]
    public void CharacterCompiler_fills_skills_and_collects_granted_nodes()
    {
        var dir = FixturePaths.CopyTiny();
        var path = Path.Combine(dir, "ascendancies.json");
        var arr = JsonNode.Parse(File.ReadAllText(path))!.AsArray();
        arr[0]!["notables"]!.AsArray()[0]!["effects"] = JsonNode.Parse(
            "[{\"type\":\"mechanic\",\"id\":\"grant_skill_node\",\"params\":{\"skill\":\"test_slash\",\"node\":\"slash_rend\"}}]");
        File.WriteAllText(path, arr.ToJsonString());
        var reg = new MechanicRegistry();
        reg.Register(new GrantStub());
        var res = DataLoader.Load(dir, reg);
        Assert.NotNull(res.Data);
        var build = TestBuilds.Warrior(level: 30, weapon: "test_sword", slots: new[]
        {
            Slot("test_slash", 6, ("slash_depth", 2), ("slash_fury", 1)),
            Slot("test_cloud", 5),
        });
        build.AscendancyId = "test_asc";
        build.AscendancyNodes.Add("asc_n1");
        var ch = new CharacterCompiler(res.Data!).Compile(build);
        Assert.Equal(new[] { "test_slash", "test_cloud" }, ch.Skills.Select(s => s.SkillId));
        var slash = ch.Skills[0];
        Assert.False(slash.IsLoot);
        Assert.True(ch.Skills[1].IsLoot);
        Assert.Equal(1, slash.EffectiveNodePoints["slash_rend"]);
        Assert.False(slash.EffectiveNodePoints.ContainsKey("slash_fury"));
        Assert.Equal(4, slash.FreePoints);
        // asc_start's global damage modifier stays in the stat pool, not duplicated on the skill
        Assert.DoesNotContain(slash.Modifiers, m => m.Source.Kind == "ascendancy");
    }

    [Fact]
    public void Outside_conversion_and_modifier_keep_real_source()
    {
        var conv = new Effect[] { new AddConversionEffect(DamageType.Physical, DamageType.Cold, 25, Own("test_bolt"), false) };
        var c = SkillCompiler.Compile(Slot("test_bolt", 5), Data.Skills["test_bolt"], Ctx(passive: conv, asc: conv));
        Assert.Equal(new[] { "passive_node", "ascendancy_node" }, c.Conversions.Select(x => x.Source.Id));
        Assert.All(c.Conversions, x => Assert.Equal("N", x.Source.Name));
    }

    [Fact]
    public void Scoped_mechanics_from_outside_land_on_matching_skill()
    {
        var m = new MechanicEffect("some_mech", new Dictionary<string, JsonElement>(), new Scope.Tags(TagSet.Of("fire")));
        var bolt = SkillCompiler.Compile(Slot("test_bolt", 5), Data.Skills["test_bolt"], Ctx(asc: new Effect[] { m }));
        var spark = SkillCompiler.Compile(Slot("test_spark", 5), Data.Skills["test_spark"], Ctx(asc: new Effect[] { m }));
        Assert.Single(bolt.Mechanics);
        Assert.Empty(spark.Mechanics);
    }

    private static GameData LoadWithEffects(string ascEffectsJson, string passiveEffectsJson)
    {
        var dir = FixturePaths.CopyTiny();
        var ap = Path.Combine(dir, "ascendancies.json");
        var arr = JsonNode.Parse(File.ReadAllText(ap))!.AsArray();
        arr[0]!["notables"]!.AsArray()[0]!["effects"] = JsonNode.Parse(ascEffectsJson);
        File.WriteAllText(ap, arr.ToJsonString());
        var pp = Path.Combine(dir, "passives.json");
        var pj = JsonNode.Parse(File.ReadAllText(pp))!;
        var nodes = pj is JsonArray pa ? pa : pj["nodes"]!.AsArray();
        var target = nodes.First(n => n!["kind"]?.GetValue<string>() == "notable" || n!["kind"]?.GetValue<string>() == "small")!;
        target["effects"] = JsonNode.Parse(passiveEffectsJson);
        File.WriteAllText(pp, pj.ToJsonString());
        var res = DataLoader.Load(dir);
        Assert.NotNull(res.Data);
        return res.Data!;
    }

    [Fact]
    public void Deferred_effects_flow_through_CharacterCompiler_to_matching_skills_only()
    {
        var data = LoadWithEffects(
            "[{\"type\":\"set_damage_type\",\"to\":\"cold\",\"scope\":\"skill:test_slash\"}]",
            "[{\"type\":\"add_conversion\",\"from\":\"physical\",\"to\":\"fire\",\"percent\":30,\"scope\":{\"tags\":[\"melee\"]}}]");
        var passiveId = data.Passives.Nodes.Values.First(n => n.Effects.OfType<AddConversionEffect>().Any()).Id;
        var build = TestBuilds.Warrior(level: 30, weapon: "test_sword", slots: new[] { Slot("test_slash", 5), Slot("test_slam", 5), Slot("test_cloud", 5) });
        build.AscendancyId = "test_asc";
        build.AscendancyNodes.Add("asc_n1");
        build.PassiveNodes.Add(passiveId);
        var ch = new CharacterCompiler(data).Compile(build);
        var slash = ch.Skills[0];
        Assert.Equal(DamageType.Cold, slash.DamageTypeOverride);
        Assert.True(slash.Tags.Contains("cold") && !slash.Tags.Contains("physical"));
        var conv = Assert.Single(slash.Conversions);
        Assert.Equal(passiveId, conv.Source.Id);
        Assert.Equal("passive", conv.Source.Kind);
        Assert.Equal(30, conv.Percent);
        Assert.Null(ch.Skills[1].DamageTypeOverride);
        Assert.Empty(ch.Skills[1].Conversions);
        Assert.Empty(ch.Skills[2].Conversions); // test_cloud has no melee tag
    }
}
