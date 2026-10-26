using System.Globalization;
using System.Text;
using System.Text.Json;
using Dialoid.Core.Character;
using Dialoid.Core.Effects;
using Dialoid.Core.Passives;
using Dialoid.Core.Simulation;
using Dialoid.Core.Skills;
using Dialoid.Core.Tests.Fixtures;

namespace Dialoid.Core.Tests.Content;

/// <summary>
/// Spec 12 (reference builds in data/builds/) and their sanity checks (spec 15.5, 15.9, 15.10 inputs). The six level-60 builds are
/// the owner's demo builds and the balance regression set; parity/ holds the best build of each class at levels 1 and 20, and
/// attributes/ holds a copy of one reference build per class with its attribute nodes swapped for adjacent small nodes.
/// </summary>
public class ReferenceBuildTests
{
    /// <summary>The six level-60 reference builds (spec 12), by class.</summary>
    private static readonly (string Path, string ClassId)[] Main =
    {
        ("warrior_berserker_whirlwind", "warrior"),
        ("warrior_thunderer_crush", "warrior"),
        ("warrior_fire_cleave_crit", "warrior"),
        ("sorcerer_pyromancer_fireball", "sorcerer"),
        ("sorcerer_cryomancer_ice_lance", "sorcerer"),
        ("sorcerer_spark_hybrid", "sorcerer"),
    };

    private static readonly string[] Parity = { "parity/warrior_l1", "parity/warrior_l20", "parity/sorcerer_l1", "parity/sorcerer_l20" };

    /// <summary>Attribute-test variants (spec 15.9) and the reference build each one copies.</summary>
    private static readonly Dictionary<string, string> AttributeVariants = new()
    {
        ["attributes/warrior_no_attribute_nodes"] = "warrior_thunderer_crush",
        ["attributes/sorcerer_no_attribute_nodes"] = "sorcerer_pyromancer_fireball",
    };

    public static IEnumerable<object[]> MainBuilds() => Main.Select(m => new object[] { m.Path });

    public static IEnumerable<object[]> AllBuildFiles() =>
        Main.Select(m => m.Path).Concat(Parity).Concat(AttributeVariants.Keys).Select(p => new object[] { p });

    private static string BuildsDir => Path.Combine(FixturePaths.RealData, "builds");

    private static readonly Dictionary<string, SimResult> TrainingCache = new();

    private static SimResult Training(string path)
    {
        lock (TrainingCache)
        {
            if (!TrainingCache.TryGetValue(path, out var r))
                TrainingCache[path] = r = ContentHarness.Run(ContentHarness.LoadBuild(path), "training");
            return r;
        }
    }

    [Fact]
    public void Builds_directory_holds_exactly_the_listed_builds()
    {
        var expected = AllBuildFiles().Select(o => (string)o[0]).OrderBy(p => p, StringComparer.Ordinal).ToList();
        var actual = Directory.GetFiles(BuildsDir, "*.json", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(BuildsDir, f).Replace('\\', '/')[..^".json".Length])
            .OrderBy(p => p, StringComparer.Ordinal).ToList();
        Assert.Equal(expected, actual);
    }

    /// <summary>Review Focus 2: every build file loads through BuildCodec and breaks no build rule.</summary>
    [Theory]
    [MemberData(nameof(AllBuildFiles))]
    public void Every_reference_build_is_legal(string path)
    {
        var b = ContentHarness.LoadBuild(path);
        var violations = BuildRules.Validate(b, ContentHarness.Data);
        Assert.True(violations.Count == 0, $"{path}: " + string.Join("; ", violations.Select(v => $"{v.Code}: {v.Message}")));
        Assert.NotEmpty(b.Rotation);
        Assert.Equal(b.Slots.Select(s => s.SkillId).OrderBy(s => s), b.Rotation.OrderBy(s => s));
    }

    /// <summary>A level-60 reference build spends everything: 59 passives, an ascendancy with 4 notables, 5 native skills at level 20
    /// with all 20 tree points, a weapon.</summary>
    [Theory]
    [MemberData(nameof(MainBuilds))]
    public void Main_build_is_complete_at_level_60(string path)
    {
        var b = ContentHarness.LoadBuild(path);
        var cls = ContentHarness.Data.Classes[b.ClassId];
        Assert.Equal(Main.Single(m => m.Path == path).ClassId, b.ClassId);
        Assert.Equal(60, b.Level);
        Assert.Equal(59, b.PassiveNodes.Count);
        Assert.NotNull(b.AscendancyId);
        Assert.Equal(4, b.AscendancyNodes.Count);
        Assert.Equal(5, b.Slots.Count);
        Assert.All(b.Slots, s =>
        {
            Assert.Contains(s.SkillId, cls.SkillIds);
            Assert.Equal(20, s.Level);
            Assert.Equal(20, s.NodePoints.Values.Sum());
        });
        Assert.NotNull(b.WeaponId);
        Assert.DoesNotContain(b.PassiveNodes, n => ContentHarness.Data.Passives.Nodes[n].Kind == PassiveKind.Keystone);
    }

    [Theory]
    [InlineData("parity/warrior_l1", "warrior", 1)]
    [InlineData("parity/warrior_l20", "warrior", 20)]
    [InlineData("parity/sorcerer_l1", "sorcerer", 1)]
    [InlineData("parity/sorcerer_l20", "sorcerer", 20)]
    public void Parity_build_uses_its_whole_level(string path, string classId, int level)
    {
        var b = ContentHarness.LoadBuild(path);
        Assert.Equal((classId, level), (b.ClassId, b.Level));
        Assert.Equal(level - 1, b.PassiveNodes.Count);
        Assert.Equal(BuildRules.SlotsUnlocked(level), b.Slots.Count);
        Assert.Equal(level >= 20 ? 1 : 0, b.AscendancyNodes.Count);
        Assert.All(b.Slots, s => Assert.Equal(Math.Min(level, 20), s.Level));
        Assert.All(b.Slots, s => Assert.Equal(s.Level, s.NodePoints.Values.Sum()));
    }

    /// <summary>
    /// Spec 15.9 input: a copy of a reference build where attribute nodes are swapped for adjacent small nodes, with the same point
    /// budget. The class starts are ringed by attribute nodes, so an attribute node stays only when the tree has no other way to
    /// the build's other nodes (removing it would disconnect them); every other attribute node is gone.
    /// </summary>
    [Theory]
    [InlineData("attributes/warrior_no_attribute_nodes")]
    [InlineData("attributes/sorcerer_no_attribute_nodes")]
    public void Attribute_variant_swaps_attribute_nodes_for_small_nodes(string path)
    {
        var tree = ContentHarness.Data.Passives;
        var variant = ContentHarness.LoadBuild(path);
        var source = ContentHarness.LoadBuild(AttributeVariants[path]);
        bool IsAttr(string n) => tree.Nodes[n].Kind == PassiveKind.Attribute;

        // Same build apart from the passive nodes.
        Assert.Equal(ContentHarness.Clone(source).Slots.Count, variant.Slots.Count);
        Assert.Equal((source.ClassId, source.Level, source.AscendancyId, source.WeaponId), (variant.ClassId, variant.Level, variant.AscendancyId, variant.WeaponId));
        Assert.Equal(source.AscendancyNodes, variant.AscendancyNodes);
        Assert.Equal(source.Rotation, variant.Rotation);
        for (var i = 0; i < source.Slots.Count; i++)
        {
            Assert.Equal(source.Slots[i].SkillId, variant.Slots[i].SkillId);
            Assert.Equal(source.Slots[i].NodePoints.OrderBy(kv => kv.Key), variant.Slots[i].NodePoints.OrderBy(kv => kv.Key));
        }

        Assert.Equal(source.PassiveNodes.Count, variant.PassiveNodes.Count);
        Assert.Subset(variant.PassiveNodes.ToHashSet(), source.PassiveNodes.Where(n => !IsAttr(n)).ToHashSet());
        var added = variant.PassiveNodes.Except(source.PassiveNodes).ToList();
        Assert.All(added, n => Assert.Equal(PassiveKind.Small, tree.Nodes[n].Kind));
        var removedAttrs = source.PassiveNodes.Where(IsAttr).Except(variant.PassiveNodes).ToList();
        Assert.Equal(removedAttrs.Count, added.Count);
        Assert.NotEmpty(removedAttrs);

        var start = ContentHarness.Data.Classes[variant.ClassId].PassiveStartNodeId;
        foreach (var kept in variant.PassiveNodes.Where(IsAttr))
        {
            var rest = variant.PassiveNodes.Where(n => n != kept);
            Assert.False(tree.IsConnectedToStart(rest, start), $"{path}: attribute node {kept} is not needed for connectivity");
        }
    }

    /// <summary>Review Focus 3: the rotation of each reference build uses every skill in it at least once in 60 s on «Тренировочный».</summary>
    [Theory]
    [MemberData(nameof(MainBuilds))]
    public void Every_rotation_skill_is_used(string path)
    {
        var b = ContentHarness.LoadBuild(path);
        var r = Training(path);
        Assert.Empty(r.Violations);
        var used = r.Log.Where(e => e.Kind == "use").Select(e => e.Text).ToHashSet();
        foreach (var skill in b.Rotation) Assert.True(used.Contains(skill), $"{path}: rotation skill {skill} is never used");
    }

    [Theory]
    [MemberData(nameof(MainBuilds))]
    public void Reference_build_deals_damage_and_survives_somewhat(string path)
    {
        var b = ContentHarness.LoadBuild(path);
        Assert.True(Training(path).DpsSingleTarget > 0, $"{path}: no damage");
        var survival = ContentHarness.Survival(b);
        Assert.Empty(survival.Violations);
        Assert.True(survival.TimeToDeath >= 5, $"{path}: dies in {survival.TimeToDeath} s");
    }

    /// <summary>Spec 15.5: within a class the reference builds differ by dominant damage type or dominant skill («Тренировочный»).</summary>
    [Fact]
    public void Builds_within_a_class_differ_by_dominant_type_or_skill()
    {
        foreach (var cls in Main.GroupBy(m => m.ClassId))
        {
            var dominant = cls.Select(m =>
            {
                var r = Training(m.Path);
                return (m.Path, Skill: r.DamageBySkill.MaxBy(kv => kv.Value).Key, Type: r.DamageByType.MaxBy(kv => kv.Value).Key);
            }).ToList();
            Assert.True(dominant.Count >= 3, $"{cls.Key}: fewer than 3 reference builds");
            for (var i = 0; i < dominant.Count; i++)
                for (var j = i + 1; j < dominant.Count; j++)
                    Assert.False(dominant[i].Skill == dominant[j].Skill && dominant[i].Type == dominant[j].Type,
                        $"{dominant[i].Path} and {dominant[j].Path}: both {dominant[i].Skill} / {dominant[i].Type}");
        }
    }

    // ---- Review Focus 4: every skill tree node does something ----

    /// <summary>The baseline build for each skill: a reference build with the skill slotted (its own tree emptied). A skill no reference
    /// build slots replaces another skill of that build (loot skills come with their item, at the loot cap).</summary>
    private static readonly Dictionary<string, (string Build, string? Replaces)> Baselines = new()
    {
        ["cleave"] = ("warrior_fire_cleave_crit", null),
        // Not the thunderer: its «Разряд» sets Crush's ground zone after the tree and overrides «Трещины» (BALANCE_NOTES).
        ["crush"] = ("warrior_berserker_whirlwind", null),
        ["battle_cry"] = ("warrior_berserker_whirlwind", null),
        ["whirlwind"] = ("warrior_berserker_whirlwind", null),
        ["charge"] = ("warrior_berserker_whirlwind", null),
        ["throwing_axe"] = ("warrior_berserker_whirlwind", null),
        ["spark"] = ("sorcerer_spark_hybrid", null),
        ["fireball"] = ("sorcerer_pyromancer_fireball", null),
        ["ice_lance"] = ("sorcerer_cryomancer_ice_lance", null),
        ["ball_lightning"] = ("sorcerer_pyromancer_fireball", null),
        ["meteor"] = ("sorcerer_pyromancer_fireball", null),
        ["blink"] = ("sorcerer_spark_hybrid", null),
        ["poison_cloud"] = ("sorcerer_pyromancer_fireball", "ball_lightning"),
    };

    /// <summary>Nodes allowed to leave both the compiled skill and the simulation unchanged on their baseline, with the reason.</summary>
    private static readonly Dictionary<(string Skill, string Node), string> NoEffectExceptions = new();

    private static Build Baseline(string skill)
    {
        var (path, replaces) = Baselines[skill];
        var b = ContentHarness.Clone(ContentHarness.LoadBuild(path));
        if (replaces != null)
        {
            var def = ContentHarness.Data.Skills[skill];
            var slot = b.Slots.Single(s => s.SkillId == replaces);
            slot.SkillId = skill;
            if (def.ClassId == null)
            {
                var item = ContentHarness.Data.Items.Values.First(i => i.GrantsSkill == skill);
                b.ItemIds.Add(item.Id);
                slot.Level = BuildRules.LootSkillBaseCap + item.SkillLevelBonus;
            }
            b.Rotation[b.Rotation.IndexOf(replaces)] = skill;
        }
        b.Slots.Single(s => s.SkillId == skill).NodePoints.Clear();
        return b;
    }

    /// <summary>The node at 1 point plus the minimal points of everything it requires (recursively).</summary>
    private static Dictionary<string, int> WithRequirements(SkillDefinition def, string node, int points)
    {
        var pts = new Dictionary<string, int>();
        void Need(string id, int p)
        {
            if (pts.GetValueOrDefault(id) >= p) return;
            foreach (var (req, rp) in def.Tree.Single(n => n.Id == id).Requires) Need(req, rp);
            pts[id] = p;
        }
        Need(node, points);
        return pts;
    }

    private static string Num(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    private static string Mod(Dialoid.Core.Stats.Modifier m) =>
        $"{m.Stat}:{m.Kind}:{Num(m.Value)}:{string.Join(",", m.RequiredTags.Items.OrderBy(t => t))}:" +
        $"{string.Join(",", m.Conditions.Select(c => $"{c.Id}/{c.Param}/{c.Ref}"))}:{m.Per}:{m.Priority}";

    private static string Proc(GrantProcEffect p) => p.Action switch
    {
        ProcAction.ApplyBuff a => $"{p.Trigger}>buff:{a.BuffId}:{Num(a.DurationSec)}:{string.Join(";", a.Mods.Select(Mod))}",
        var a => $"{p.Trigger}>{a}",
    };

    /// <summary>Everything compiled about a skill except its own node points and the sources of its numbers.</summary>
    private static string Fingerprint(CompiledSkill s)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(",", s.Tags.Items.OrderBy(t => t))).Append('|').Append(s.DamageTypeOverride).Append('|');
        var b = s.Base;
        sb.Append(string.Join(",", b.SpellDamage.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}:{Num(kv.Value.Min)}-{Num(kv.Value.Max)}")))
            .Append($"|{b.WeaponEffectiveness}|{Num(b.AddedDamageEffectiveness)}|{Num(b.CastTime)}|{Num(b.AttackTimeMultiplier)}|{b.Cooldown}")
            .Append($"|{Num(b.ResourceCost)}|{Num(b.ResourceGain)}|{Num(b.CritChance)}|{b.HitsPerUse}|")
            .Append(string.Join(",", b.AilmentChances.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}:{Num(kv.Value)}"))).Append('|');
        sb.Append(string.Join(",", s.Behavior.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}:{Num(kv.Value)}"))).Append('|');
        sb.Append(string.Join(",", s.Conversions.Select(c => $"{c.From}>{c.To}:{Num(c.Percent)}"))).Append('|');
        sb.Append($"{Num(s.ResourceCost)}|{Num(s.ResourceGain)}|");
        sb.Append(string.Join(";", s.Modifiers.Select(Mod).OrderBy(x => x, StringComparer.Ordinal))).Append('|');
        sb.Append(string.Join(";", s.Procs.Select(Proc).OrderBy(x => x, StringComparer.Ordinal))).Append('|');
        sb.Append(string.Join(";", s.Mechanics.Select(m => $"{m.MechanicId}:{JsonSerializer.Serialize(m.Params.OrderBy(kv => kv.Key).ToDictionary(kv => kv.Key, kv => kv.Value.GetRawText()))}")
            .OrderBy(x => x, StringComparer.Ordinal))).Append('|');
        sb.Append($"{s.ChannelLimit}|{string.Join(",", s.NullifiedDamageTypes.OrderBy(t => t))}|{s.GroundDamageType}|{s.ChannelRamp}");
        return sb.ToString();
    }

    private sealed record Outcome(string Compiled, double Training, double Swarm, double Survival);

    private static Outcome Measure(Build b, string skill)
    {
        var violations = BuildRules.Validate(b, ContentHarness.Data);
        Assert.True(violations.Count == 0, $"{skill}: " + string.Join("; ", violations.Select(v => v.Message)));
        var compiled = new CharacterCompiler(ContentHarness.Data, MechanicRegistry.Default).Compile(b).Skills.Single(s => s.SkillId == skill);
        return new Outcome(Fingerprint(compiled), double.NaN, double.NaN, double.NaN);
    }

    private static Outcome Simulate(Build b, Outcome o) =>
        o with
        {
            Training = ContentHarness.TrainingDps(b),
            Swarm = ContentHarness.Run(b, "swarm").DpsPack,
            Survival = ContentHarness.Survival(b).TimeToDeath ?? 0,
        };

    [Fact]
    public void Every_skill_tree_node_has_an_effect()
    {
        var skills = ContentHarness.Data.Classes.Values.SelectMany(c => c.SkillIds).Append("poison_cloud").Distinct().ToList();
        Assert.Equal(skills.OrderBy(s => s), Baselines.Keys.OrderBy(s => s));
        var dead = new List<(string Skill, string Node)>();
        foreach (var skill in skills)
        {
            var def = ContentHarness.Data.Skills[skill];
            var baseline = Baseline(skill);
            var slot = baseline.Slots.FindIndex(s => s.SkillId == skill);
            var simulated = new Dictionary<string, Outcome>();
            foreach (var node in def.Tree)
            {
                var with = WithRequirements(def, node.Id, 1);
                var without = new Dictionary<string, int>(with);
                without.Remove(node.Id);
                var a = ContentHarness.Clone(baseline);
                a.Slots[slot].NodePoints = with;
                var b = ContentHarness.Clone(baseline);
                b.Slots[slot].NodePoints = without;
                var oa = Measure(a, skill);
                var ob = Measure(b, skill);
                if (oa.Compiled != ob.Compiled) continue;
                // Same compiled skill: the node may still act through the simulation (e.g. a mechanic reading the node).
                oa = Simulate(a, oa);
                var key = string.Join(",", without.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}"));
                if (!simulated.TryGetValue(key, out var cached)) simulated[key] = cached = Simulate(b, ob);
                if (Same(oa.Training, cached.Training) && Same(oa.Swarm, cached.Swarm) && Same(oa.Survival, cached.Survival))
                    dead.Add((skill, node.Id));
            }
        }
        var unexpected = dead.Where(d => !NoEffectExceptions.ContainsKey(d)).ToList();
        Assert.True(unexpected.Count == 0, "nodes without effect: " + string.Join(", ", unexpected.Select(d => $"{d.Skill}/{d.Node}")));
        var stale = NoEffectExceptions.Keys.Where(k => !dead.Contains(k)).ToList();
        Assert.True(stale.Count == 0, "exceptions that now have an effect: " + string.Join(", ", stale.Select(d => $"{d.Skill}/{d.Node}")));
    }

    private static bool Same(double x, double y) => Math.Abs(x - y) <= 1e-9 * Math.Max(1, Math.Abs(y));
}
