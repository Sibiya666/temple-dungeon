using Dialoid.Core.Character;
using Dialoid.Core.Combat;
using Dialoid.Core.Simulation;
using Dialoid.Core.Effects;
using Dialoid.Core.Skills;
using Dialoid.Core.Stats;
using Dialoid.Core.Passives;
using Attr = Dialoid.Core.Character.Attribute;

namespace Dialoid.Core.Tests.Content;

/// <summary>Spec 8.4 (skill tree rules), 10.1 (Warrior skills) and 10.3 (Cleave tree) over the real data.</summary>
public class SkillContentTests
{
    private static IReadOnlyDictionary<string, SkillDefinition> Skills => ContentHarness.Data.Skills;

    /// <summary>Every skill a player can slot: class skills and loot skills. Triggered-only mini-skills have no tree.</summary>
    public static IEnumerable<object[]> TreeSkills() =>
        ContentHarness.Data.Skills.Values
            .Where(s => !s.IsTriggeredOnly && !s.Tags.Contains("triggered"))
            .OrderBy(s => s.Id, StringComparer.Ordinal)
            .Select(s => new object[] { s.Id });

    /// <summary>Spec 8.4: «only +% damage» — every effect is a damage Increased/More modifier without conditions.</summary>
    private static bool IsPureDamageNode(SkillTreeNode n) =>
        n.Effects.Count > 0 && n.Effects.All(e => e is ModifierEffect
        {
            Modifier: { Stat: "damage", Kind: ModKind.Increased or ModKind.More, Conditions.Count: 0 },
        });

    /// <summary>Effects that change how the skill works rather than its numbers (spec 8.4: type, form, role, ailment base, procs).</summary>
    private static bool ChangesMechanic(Effect e) => e is AddTagEffect or RemoveTagEffect or SetDamageTypeEffect or SetResourceEffect
        or GrantProcEffect or MechanicEffect or AddConversionEffect or SetBehaviorEffect;

    [Theory]
    [MemberData(nameof(TreeSkills))]
    public void Skill_tree_follows_design_rules(string skillId)
    {
        var tree = Skills[skillId].Tree;
        Assert.InRange(tree.Count, 15, 22);
        Assert.True(tree.Sum(n => n.MaxPoints) >= 30, $"{skillId}: Σmax_points = {tree.Sum(n => n.MaxPoints)}");
        var (reachable, worstPick) = WorstReachablePoints(tree);
        var floor = SpecFixedReachableFloor.GetValueOrDefault(skillId, 28);
        Assert.True(reachable >= floor, $"{skillId}: only {reachable} points reachable when picking {worstPick} (need {floor})");
        Assert.True(tree.Where(n => n.ExclusiveGroup != null).GroupBy(n => n.ExclusiveGroup).Any(g => g.Count() >= 2),
            $"{skillId}: no exclusive group with at least two nodes");
        var pure = tree.Where(IsPureDamageNode).Select(n => n.Id).ToList();
        Assert.True(pure.Count <= tree.Count * 0.25, $"{skillId}: {pure.Count}/{tree.Count} pure damage nodes ({string.Join(", ", pure)})");
        Assert.True(tree.Count(n => n.Effects.Any(ChangesMechanic)) >= 3, $"{skillId}: fewer than 3 mechanic-changing nodes");
        Assert.All(tree, n => Assert.NotEmpty(n.Effects));
        Assert.All(tree, n => Assert.False(string.IsNullOrWhiteSpace(n.Description), $"{skillId}/{n.Id}: empty description"));
        Assert.Equal(tree.Count, tree.Select(n => n.Name).Distinct().Count());
        var keystoneNames = ContentHarness.Data.Passives.Nodes.Values.Where(n => n.Kind == PassiveKind.Keystone)
            .Select(n => n.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.All(tree, n => Assert.False(keystoneNames.Contains(n.Name), $"{skillId}/{n.Id}: «{n.Name}» is a keystone name"));
    }

    /// <summary>
    /// Trees fixed by spec 10.3 keep the spec layout even when its forks leave fewer points (Cleave: 25, logged in BALANCE_NOTES).
    /// Every other tree must leave at least 28 points reachable whatever the player picks in its exclusive groups.
    /// </summary>
    private static readonly Dictionary<string, int> SpecFixedReachableFloor = new() { ["cleave"] = 25 };

    /// <summary>
    /// The fewest points a player can still spend after picking one reachable node in every exclusive group: the other nodes of
    /// each group are lost, and so is every node whose requirements can no longer be met. Returns the total and the worst picks.
    /// </summary>
    internal static (int Points, string Picks) WorstReachablePoints(IReadOnlyList<SkillTreeNode> tree)
    {
        var groups = tree.Where(n => n.ExclusiveGroup != null).GroupBy(n => n.ExclusiveGroup!).Select(g => g.ToList()).ToList();
        var worst = (Points: int.MaxValue, Picks: "");
        void Walk(int gi, List<SkillTreeNode> picks)
        {
            if (gi == groups.Count)
            {
                var banned = groups.SelectMany(g => g).Where(n => !picks.Contains(n)).Select(n => n.Id).ToHashSet();
                var open = new HashSet<string>();
                bool grew = true;
                while (grew)
                {
                    grew = false;
                    foreach (var n in tree)
                        if (!banned.Contains(n.Id) && !open.Contains(n.Id) && n.Requires.All(r => open.Contains(r.NodeId)))
                        {
                            open.Add(n.Id);
                            grew = true;
                        }
                }
                if (picks.Any(p => !open.Contains(p.Id))) return; // a pick the player cannot reach is no choice at all
                var pts = tree.Where(n => open.Contains(n.Id)).Sum(n => n.MaxPoints);
                if (pts < worst.Points) worst = (pts, string.Join(", ", picks.Select(p => p.Id)));
                return;
            }
            foreach (var n in groups[gi])
            {
                picks.Add(n);
                Walk(gi + 1, picks);
                picks.RemoveAt(picks.Count - 1);
            }
        }
        Walk(0, new List<SkillTreeNode>());
        return worst;
    }

    [Fact]
    public void Worst_reachable_points_respects_forks_and_requirements()
    {
        // Cleave: picking «Раскол» loses «Точный удар» + «Кромка» (3), picking «Удар грома» loses «Раскаленный клинок» + «Пылающие раны» (4).
        var (pts, picks) = WorstReachablePoints(Skills["cleave"].Tree);
        Assert.Equal(32 - 7, pts);
        Assert.Contains("split", picks);
        Assert.Contains("thunder_strike", picks);
    }

    [Fact]
    public void Tree_skills_exist()
    {
        Assert.Contains(TreeSkills(), row => (string)row[0] == "cleave");
    }

    private sealed record SkillRow(string Id, int Unlock, string[] Tags, string? Role);

    // Spec 10.1 table: id, unlock level, tags, role (generator / spender / null).
    private static readonly SkillRow[] WarriorTable =
    {
        new("cleave", 1, new[] { "attack", "melee", "physical", "generator" }, "generator"),
        new("crush", 1, new[] { "attack", "melee", "area", "physical", "spender" }, "spender"),
        new("battle_cry", 4, new[] { "shout", "buff" }, null),
        new("whirlwind", 8, new[] { "attack", "melee", "area", "channel", "physical", "spender" }, "spender"),
        new("charge", 12, new[] { "attack", "melee", "movement", "physical" }, null),
        new("throwing_axe", 16, new[] { "attack", "projectile", "physical", "generator" }, "generator"),
    };

    [Fact]
    public void Warrior_has_six_skills_from_spec_table()
    {
        var warrior = ContentHarness.Data.Classes["warrior"];
        Assert.Equal(WarriorTable.Select(r => r.Id).OrderBy(x => x), warrior.SkillIds.OrderBy(x => x));

        foreach (var row in WarriorTable)
        {
            var s = Skills[row.Id];
            Assert.Equal("warrior", s.ClassId);
            Assert.Equal(row.Unlock, s.UnlockLevel);
            Assert.False(s.IsTriggeredOnly);
            foreach (var t in row.Tags) Assert.True(s.Tags.Contains(t), $"{row.Id}: missing tag {t}");
            Assert.Equal(row.Role == "generator", s.Tags.Contains("generator"));
            Assert.Equal(row.Role == "spender", s.Tags.Contains("spender"));
            if (row.Role == "generator") Assert.True(s.Base.ResourceGain > 0 && s.Base.ResourceCost == 0, row.Id);
            if (row.Role == "spender") Assert.True(s.Base.ResourceCost > 0 && s.Base.ResourceGain == 0, row.Id);
            if (s.Tags.Contains("attack"))
            {
                Assert.True(s.IsAttack, $"{row.Id}: attack without weapon_effectiveness");
                Assert.Equal(new[] { "one_hand", "two_hand" }, s.Base.WeaponTypes.OrderBy(x => x));
            }
            else Assert.False(s.IsAttack);
        }

        Assert.Equal(12, Skills["cleave"].Base.ResourceGain);
        Assert.Equal(35, Skills["crush"].Base.ResourceCost);
        Assert.Equal(20, Skills["battle_cry"].Base.Cooldown);
        var ww = Skills["whirlwind"];
        Assert.Equal(10, ww.Base.ResourceCost);
        Assert.Equal(0.4, ww.Base.Behavior["channel_interval"]);
        Assert.Equal(8, Skills["charge"].Base.Cooldown);
        Assert.Equal(8, Skills["throwing_axe"].Base.ResourceGain);
        Assert.True(Skills["throwing_axe"].Base.Behavior.GetValueOrDefault("projectile_count") >= 1);
    }

    private sealed record NodeRow(string Id, string Name, int MaxPoints, string? Group, params (string Node, int Points)[] Requires);

    // Spec 10.3 «Рассекающий удар» + «Раскатистое эхо» (Task 4 brief). Deviations are logged in data/BALANCE_NOTES.md:
    // «Эхо» 2 → 1 point (+ «Раскатистое эхо» 1 point), «Стальная кожа» 2 → 1 point (a buff proc cannot scale per point).
    private static readonly NodeRow[] CleaveTree =
    {
        new("heavy_hand", "Тяжелая рука", 4, null),
        new("lacerate", "Рваные раны", 3, null),
        new("deep_cut", "Глубокий порез", 2, null, ("lacerate", 3)),
        new("bloodletting", "Кровопускание", 1, null, ("deep_cut", 2)),
        new("thirst", "Жажда", 3, null),
        new("frenzy", "Неистовство", 1, null, ("thirst", 3)),
        new("wide_swing", "Широкий замах", 3, null, ("heavy_hand", 2)),
        new("split", "Раскол", 1, "cleave_form", ("wide_swing", 3)),
        new("precise_strike", "Точный удар", 1, "cleave_form", ("wide_swing", 3)),
        new("searing_blade", "Раскаленный клинок", 3, "cleave_element", ("heavy_hand", 2)),
        new("burning_wounds", "Пылающие раны", 1, null, ("searing_blade", 3)),
        new("thunder_strike", "Удар грома", 1, "cleave_element", ("heavy_hand", 4)),
        new("echo", "Эхо", 1, null, ("thirst", 3)),
        new("rolling_echo", "Раскатистое эхо", 1, null, ("echo", 1)),
        new("steel_skin", "Стальная кожа", 1, null),
        new("crushing_force", "Сокрушительная сила", 3, null, ("heavy_hand", 4)),
        new("edge", "Кромка", 2, null, ("precise_strike", 1)),
    };

    [Fact]
    public void Cleave_tree_matches_spec_10_3()
    {
        var cleave = Skills["cleave"];
        Assert.Equal(110, cleave.Base.WeaponEffectiveness);
        Assert.Equal(1.2, cleave.Base.Behavior["area_radius"]);
        Assert.Equal(12, cleave.Base.ResourceGain);
        Assert.Equal(10, cleave.Base.AilmentChances["bleed"]);
        Assert.True(cleave.Tags.Contains("area"));

        var nodes = cleave.Tree.ToDictionary(n => n.Id);
        Assert.Equal(CleaveTree.Select(r => r.Id).OrderBy(x => x), nodes.Keys.OrderBy(x => x));
        foreach (var row in CleaveTree)
        {
            var n = nodes[row.Id];
            Assert.Equal(row.Name, n.Name);
            Assert.Equal(row.MaxPoints, n.MaxPoints);
            Assert.Equal(row.Group, n.ExclusiveGroup);
            Assert.Equal(row.Requires.OrderBy(r => r.Node), n.Requires.OrderBy(r => r.NodeId));
        }

        // Key effects whose literal form matters for the spec's mechanics.
        T Single<T>(string node) where T : Effect => Assert.Single(nodes[node].Effects.OfType<T>());

        var split = Single<GrantProcEffect>("split");
        Assert.IsType<Trigger.OnUse>(split.Trigger);
        Assert.Equal(new ProcAction.CastSkill("cleave", 50), split.Action);

        foreach (var id in new[] { "echo", "rolling_echo" })
        {
            var echo = Single<GrantProcEffect>(id);
            Assert.Equal(new Trigger.EveryNUses(4), echo.Trigger);
            Assert.Equal(new ProcAction.CastSkill("crush", 30), echo.Action);
        }

        var searing = Single<AddConversionEffect>("searing_blade");
        Assert.Equal((DamageType.Physical, DamageType.Fire, 20.0, true), (searing.From, searing.To, searing.Percent, searing.PerPoint));
        var thunder = Single<AddConversionEffect>("thunder_strike");
        Assert.Equal((DamageType.Physical, DamageType.Lightning, 50.0), (thunder.From, thunder.To, thunder.Percent));

        Assert.Contains(nodes["precise_strike"].Effects, e => e is RemoveTagEffect { Tag: "area" });
        var frenzy = Single<GrantProcEffect>("frenzy");
        Assert.Equal(new Trigger.EveryNUses(3), frenzy.Trigger);
    }

    [Fact]
    public void Battle_cry_buff_is_twenty_percent_for_eight_seconds()
    {
        var cry = Skills["battle_cry"];
        Assert.Equal(20, cry.Base.Cooldown);
        Assert.Null(cry.Base.WeaponEffectiveness);
        Assert.Empty(cry.Base.SpellDamage);

        var buffs = cry.Tree.SelectMany(n => n.Effects).OfType<GrantProcEffect>()
            .Where(p => p.Trigger is Trigger.OnUse && p.Action is ProcAction.ApplyBuff { BuffId: "battle_cry" })
            .Select(p => (ProcAction.ApplyBuff)p.Action).ToList();
        Assert.Contains(buffs, b => b.DurationSec == 8 && b.Mods.Count == 1
            && b.Mods[0] is { Stat: "damage", Kind: ModKind.Increased, Value: 20, Conditions.Count: 0 } && b.Mods[0].RequiredTags.Items.Count == 0);

        // In the simulator: Cleave hits under the cry deal exactly 20% more than after it ends (no other Increased damage).
        var build = new Build
        {
            ClassId = "warrior", Level = 4, WeaponId = "greataxe",
            Slots =
            {
                new SkillSlot { SkillId = "battle_cry", Level = 1, NodePoints = { ["war_roar"] = 1 } },
                new SkillSlot { SkillId = "cleave", Level = 1 },
            },
        };
        var r = ContentHarness.Run(build, "training");
        Assert.Empty(r.Violations);
        var cryUses = r.Log.Where(e => e is { Kind: "use", Text: "battle_cry" }).Select(e => e.Time).ToList();
        Assert.Equal(0, cryUses[0]);
        Assert.True(cryUses[1] >= 20 - 1e-9);
        double Amount(SimEvent e) => double.Parse(e.Text[(e.Text.IndexOf(": ", StringComparison.Ordinal) + 2)..],
            System.Globalization.CultureInfo.InvariantCulture);
        var hits = r.Log.Where(e => e.Kind == "hit" && e.Text.StartsWith("cleave", StringComparison.Ordinal)).ToList();
        var buffed = hits.Where(e => e.Time < 8 - 1e-9).Select(Amount).ToList();
        var plain = hits.Where(e => e.Time > 8 + 1e-9 && e.Time < 20 - 1e-9).Select(Amount).ToList();
        Assert.NotEmpty(buffed);
        Assert.NotEmpty(plain);
        Assert.Equal(1.2, buffed.Average() / plain.Average(), 2);
    }

    private static Build WarriorWith(string skillId, int level, params (string Node, int Points)[] nodes)
    {
        var slot = new SkillSlot { SkillId = skillId, Level = level };
        foreach (var (n, p) in nodes) slot.NodePoints[n] = p;
        return new Build { ClassId = "warrior", Level = 20, WeaponId = "greataxe", Slots = { slot } };
    }

    private static double SkillDamage(Build b, string skillId)
    {
        var r = ContentHarness.Run(b, "training");
        Assert.Empty(r.Violations);
        return r.DamageBySkill.GetValueOrDefault(skillId);
    }

    [Fact]
    public void Echo_casts_crush_at_thirty_percent_and_rolling_echo_doubles_it()
    {
        Assert.Equal(0, SkillDamage(WarriorWith("cleave", 3, ("thirst", 3)), "crush"));
        var echo = SkillDamage(WarriorWith("cleave", 5, ("thirst", 3), ("echo", 1), ("lacerate", 1)), "crush"); // same level: an unslotted copy takes the source level
        var rolling = SkillDamage(WarriorWith("cleave", 5, ("thirst", 3), ("echo", 1), ("rolling_echo", 1)), "crush");
        Assert.True(echo > 0);
        Assert.Equal(2.0, rolling / echo, 2);
    }

    [Fact]
    public void Split_adds_a_half_strength_second_strike()
    {
        var plain = SkillDamage(WarriorWith("cleave", 6, ("heavy_hand", 2), ("wide_swing", 3)), "cleave");
        var split = SkillDamage(WarriorWith("cleave", 6, ("heavy_hand", 2), ("wide_swing", 3), ("split", 1)), "cleave");
        Assert.Equal(1.5, split / plain, 1);
    }

    [Fact]
    public void Precise_strike_turns_cleave_into_a_single_target_hit()
    {
        var b = WarriorWith("cleave", 6, ("heavy_hand", 2), ("wide_swing", 3), ("precise_strike", 1));
        var compiled = new CharacterCompiler(ContentHarness.Data, MechanicRegistry.Default).Compile(b);
        var cleave = compiled.Skills.Single(s => s.SkillId == "cleave");
        Assert.False(cleave.Tags.Contains("area"));
        var swarm = ContentHarness.Run(b, "swarm");
        var wide = ContentHarness.Run(WarriorWith("cleave", 6, ("heavy_hand", 2), ("wide_swing", 3), ("split", 1)), "swarm");
        Assert.True(swarm.DpsPack < wide.DpsPack);
    }

    [Fact]
    public void Whirlwind_generator_node_flips_its_role()
    {
        var ww = Skills["whirlwind"];
        var flip = ww.Tree.Single(n => n.Effects.Any(e => e is AddTagEffect { Tag: "generator" }));
        Assert.Contains(flip.Effects, e => e is RemoveTagEffect { Tag: "spender" });
        Assert.Contains(flip.Effects, e => e is SetResourceEffect { Field: ResourceField.Cost, Value: 0 });
        Assert.Contains(flip.Effects, e => e is SetResourceEffect { Field: ResourceField.Gain, Value: > 0 });
        Assert.NotNull(flip.ExclusiveGroup);
    }

    [Fact]
    public void Spender_loops_out_damage_generator_spam_without_dwarfing_it()
    {
        // Starting numbers (Task 10 tunes): a generator + spender rotation should beat generator spam, but by less than 2x.
        Build Two(string spender) => new()
        {
            ClassId = "warrior", Level = 20, WeaponId = "greataxe",
            Slots = { new SkillSlot { SkillId = spender, Level = 1 }, new SkillSlot { SkillId = "cleave", Level = 1 } },
        };
        var cleaveOnly = ContentHarness.TrainingDps(WarriorWith("cleave", 1));
        foreach (var spender in new[] { "crush", "whirlwind" })
        {
            var loop = ContentHarness.TrainingDps(Two(spender));
            Assert.InRange(loop / cleaveOnly, 1.1, 2.0);
        }
    }

    [Fact]
    public void Crush_never_sustains_itself_even_at_maximum_generation_and_minimum_cost()
    {
        // Upper bound on Fury one Crush use can return vs the lowest cost it can reach (fix round 1, blocker 1).
        var data = ContentHarness.Data;
        var crush = Skills["crush"];
        var tags = crush.Tags.With("skill:crush");
        bool Applies(Modifier m) => m.RequiredTags.Items.All(t => tags.Contains(t) || t == "skill:crush");
        var passiveMods = data.Passives.Nodes.Values.SelectMany(n => n.Effects).OfType<ModifierEffect>().Select(e => e.Modifier).ToList();
        var treeMods = crush.Tree.SelectMany(n => n.Effects.OfType<ModifierEffect>().Select(e => e.Modifier with { Value = e.Modifier.Value * n.MaxPoints })).ToList();
        var buffMods = data.Skills.Values.SelectMany(s => s.Tree).SelectMany(n => n.Effects).OfType<GrantProcEffect>()
            .Select(p => p.Action).OfType<ProcAction.ApplyBuff>().SelectMany(b => b.Mods).ToList();
        var all = passiveMods.Concat(treeMods).Concat(buffMods).Where(Applies).ToList();
        double Sum(string stat, ModKind kind, Func<double, bool> sign) => all.Where(m => m.Stat == stat && m.Kind == kind && sign(m.Value)).Sum(m => m.Value);

        // Will at 60: class base + 59 levels + every attribute node of the tree put into Will (unreachable together: a generous bound).
        var warrior = data.Classes["warrior"];
        var attributeNodes = data.Passives.Nodes.Values.Count(n => n.Kind == PassiveKind.Attribute);
        var will = warrior.BaseAttributes[Attr.Will] + 59 + 5.0 * attributeNodes;
        var willGen = data.AttributeRules.Bonuses.Where(b => b.Attribute == Attr.Will && b.Stat == "resource_gen").Sum(b => b.ValuePerPoint)
                      + warrior.ClassBonuses.Where(m => m.Stat == "resource_gen" && m.Per?.Stat == "will").Sum(m => m.Value / m.Per!.Every);
        const double battleTranceFlat = 5; // Berserker «Боевой транс»: +5 Fury per attack while the cry is up
        var maxGain = (crush.Base.ResourceGain + Sum("resource_gen", ModKind.Flat, v => v > 0) + battleTranceFlat)
                      * (1 + (Sum("resource_gen", ModKind.Increased, v => v > 0) + willGen * will) / 100);
        var minCost = (crush.Base.ResourceCost + Sum("resource_cost", ModKind.Flat, v => v < 0))
                      * (1 + Sum("resource_cost", ModKind.Increased, v => v < 0) / 100);
        Assert.True(maxGain < minCost * 0.95, $"Crush returns up to {maxGain:0.#} Fury for a minimum cost of {minCost:0.#}");
    }

    private static Build Axe(string fork, int level = 1)
    {
        var slot = new SkillSlot { SkillId = "throwing_axe", Level = level, NodePoints = { [fork] = 1 } };
        return new Build { ClassId = "warrior", Level = 20, WeaponId = "sword", Slots = { slot } };
    }

    /// <summary>Fury per second of a lone generator: gain per use × uses per second (attack speed and time multiplier).</summary>
    private static double FuryPerSecond(Build b, string skillId)
    {
        var ch = new CharacterCompiler(ContentHarness.Data, MechanicRegistry.Default).Compile(b);
        var sk = ch.Skills.Single(s => s.SkillId == skillId);
        var ev = ch.Evaluator.WithExtra(sk.Modifiers);
        var gain = ev.Evaluate("resource_gen", sk.Tags, null, sk.ResourceGain).Value;
        var speed = ev.Evaluate("attack_speed", sk.Tags, null, 1).Value;
        return gain * ContentHarness.Data.Weapons[b.WeaponId!].AttacksPerSecond * speed / sk.Base.AttackTimeMultiplier;
    }

    [Fact]
    public void Each_axe_flight_option_wins_its_own_scenario()
    {
        string[] forks = { "returning", "axe_fan", "boomerang" };
        string Best(Func<string, double> score) => forks.OrderByDescending(score).First();

        Assert.Equal("returning", Best(f => ContentHarness.TrainingDps(Axe(f))));                 // single target
        Assert.Equal("axe_fan", Best(f => ContentHarness.Run(Axe(f), "swarm").DpsPack));          // pack of 5
        Assert.Equal("boomerang", Best(f => FuryPerSecond(Axe(f), "throwing_axe")));             // Fury per second
        // The returning axe hits only the primary target even with extra axes and pierce on the main throw.
        var multi = new Build { ClassId = "warrior", Level = 20, WeaponId = "sword", Slots =
        {
            new SkillSlot { SkillId = "throwing_axe", Level = 9, NodePoints = { ["returning"] = 1, ["whetstone"] = 2, ["spare_axe"] = 2, ["piercing_throw"] = 3, ["catch"] = 1 } },
        } };
        var r = ContentHarness.Run(multi, "swarm");
        var copyHits = r.Log.Count(e => e.Kind == "hit" && e.Text.StartsWith("returning_axe", StringComparison.Ordinal));
        var uses = r.Log.Count(e => e is { Kind: "use", Text: "throwing_axe" });
        Assert.Equal(uses, copyHits);
    }

    [Fact]
    public void Whirlwind_ramp_pays_off_in_a_spender_rotation()
    {
        Build Ww(params (string, int)[] nodes)
        {
            var slot = new SkillSlot { SkillId = "whirlwind", Level = 4 };
            foreach (var (n, p) in nodes) slot.NodePoints[n] = p;
            return new Build { ClassId = "warrior", Level = 20, WeaponId = "greataxe",
                Slots = { slot, new SkillSlot { SkillId = "cleave", Level = 1 } } };
        }
        var plain = SkillDamage(Ww(("wide_circle", 3), ("steel_cocoon", 1)), "whirlwind");
        var ramp = SkillDamage(Ww(("spin_up", 3), ("blade_hurricane", 1)), "whirlwind");
        Assert.True(ramp / plain > 1.15, $"ramp ×{ramp / plain:0.###}");
    }

    // ---------------- Sorcerer (spec 10.2, 10.3) ----------------

    // Spec 10.2 table: id, unlock level, tags, role. The spec's «основной спендер» rows carry the spender tag like the Warrior's.
    private static readonly SkillRow[] SorcererTable =
    {
        new("spark", 1, new[] { "spell", "projectile", "lightning", "generator" }, "generator"),
        new("fireball", 1, new[] { "spell", "projectile", "area", "fire", "spender" }, "spender"),
        new("ice_lance", 4, new[] { "spell", "projectile", "cold", "spender" }, "spender"),
        new("ball_lightning", 8, new[] { "spell", "projectile", "area", "lightning", "spender" }, "spender"),
        new("meteor", 12, new[] { "spell", "area", "fire", "spender" }, "spender"),
        new("blink", 16, new[] { "spell", "movement" }, null),
    };

    [Fact]
    public void Sorcerer_has_six_skills_from_spec_table()
    {
        var sorcerer = ContentHarness.Data.Classes["sorcerer"];
        Assert.Equal(SorcererTable.Select(r => r.Id).OrderBy(x => x), sorcerer.SkillIds.OrderBy(x => x));

        foreach (var row in SorcererTable)
        {
            var s = Skills[row.Id];
            Assert.Equal("sorcerer", s.ClassId);
            Assert.Equal(row.Unlock, s.UnlockLevel);
            Assert.False(s.IsTriggeredOnly);
            Assert.False(s.IsAttack, $"{row.Id}: sorcerer skills are spells");
            Assert.Equal(row.Tags.OrderBy(x => x), s.Tags.Items.OrderBy(x => x));
            if (row.Role == "generator") Assert.True(s.Base.ResourceGain > 0 && s.Base.ResourceCost == 0, row.Id);
            if (row.Role == "spender") Assert.True(s.Base.ResourceCost > 0 && s.Base.ResourceGain == 0, row.Id);
            if (row.Id != "blink")
            {
                Assert.NotEmpty(s.Base.SpellDamage);
                Assert.True(s.Base.CastTime > 0, row.Id);
            }
        }

        var spark = Skills["spark"];
        Assert.Equal(3, spark.Base.ResourceGain);
        Assert.Equal(2, spark.Base.Behavior["chain_count"]);
        Assert.Equal(12, Skills["fireball"].Base.ResourceCost);
        Assert.Equal(10, Skills["ice_lance"].Base.ResourceCost);
        Assert.Equal(3, Skills["ice_lance"].Base.Behavior["pierce_count"]);
        var ball = Skills["ball_lightning"];
        Assert.Equal(20, ball.Base.ResourceCost);
        Assert.Equal(10, ball.Base.HitsPerUse);
        Assert.Equal(35, Skills["meteor"].Base.ResourceCost);
        var blink = Skills["blink"];
        Assert.Equal(6, blink.Base.Cooldown);
        Assert.Empty(blink.Base.SpellDamage);
        Assert.Equal(0, blink.Base.ResourceCost);
        foreach (var id in new[] { "spark", "fireball", "ice_lance", "ball_lightning", "meteor" }) Assert.Null(Skills[id].Base.Cooldown);
    }

    // Spec 10.3 «Огненный шар»: id, name, points, group, requires. Deviations (BALANCE_NOTES): «Отдача» is an average refund with a cap,
    // «Пламенное кольцо» does not add the dot tag to the skill.
    private static readonly NodeRow[] FireballTree =
    {
        new("heat", "Жар", 4, null),
        new("wide_blast", "Широкий взрыв", 3, null, ("heat", 2)),
        new("molten_core", "Раскаленное ядро", 3, null, ("heat", 2)),
        new("eternal_flame", "Вечное пламя", 1, null, ("molten_core", 3)),
        new("lingering_heat", "Остаточный жар", 3, null, ("eternal_flame", 1)),
        new("ring_of_fire", "Пламенное кольцо", 1, null, ("wide_blast", 3)),
        new("scatter", "Разделение", 1, "fireball_form", ("heat", 4)),
        new("compress", "Сжатие", 1, "fireball_form", ("heat", 4)),
        new("frost_orb", "Морозный шар", 1, "fireball_element", ("heat", 2)),
        new("storm_orb", "Грозовой шар", 1, "fireball_element", ("heat", 2)),
        new("mana_focus", "Концентрация маны", 3, null),
        new("backlash", "Отдача", 2, null, ("mana_focus", 3)),
        new("swift_orb", "Летучий шар", 2, null, ("mana_focus", 1)),
        new("flame_mark", "Метка пламени", 1, null, ("molten_core", 1)),
        new("overheat", "Перегрев", 3, null, ("heat", 4)),
        new("blast_wave", "Взрывная волна", 1, null, ("overheat", 3)),
        new("soul_flame", "Пламя души", 2, null, ("flame_mark", 1), ("eternal_flame", 1)),
    };

    [Fact]
    public void Fireball_tree_matches_spec_10_3()
    {
        var fb = Skills["fireball"];
        Assert.Equal((14.0, 20.0), fb.Base.SpellDamage[DamageType.Fire]);
        Assert.Single(fb.Base.SpellDamage);
        Assert.Equal(0.75, fb.Base.CastTime);
        Assert.Equal(12, fb.Base.ResourceCost);
        Assert.Equal(1, fb.Base.Behavior["projectile_count"]);
        Assert.Equal(1.5, fb.Base.Behavior["explosion_radius"]);
        Assert.Equal(20, fb.Base.AilmentChances["ignite"]);
        Assert.Equal(6, fb.Base.CritChance);
        Assert.Equal(120, fb.Base.AddedDamageEffectiveness);

        var nodes = fb.Tree.ToDictionary(n => n.Id);
        Assert.Equal(FireballTree.Select(r => r.Id).OrderBy(x => x), nodes.Keys.OrderBy(x => x));
        foreach (var row in FireballTree)
        {
            var n = nodes[row.Id];
            Assert.Equal(row.Name, n.Name);
            Assert.Equal(row.MaxPoints, n.MaxPoints);
            Assert.Equal(row.Group, n.ExclusiveGroup);
            Assert.Equal(row.Requires.OrderBy(r => r.Node), n.Requires.OrderBy(r => r.NodeId));
        }

        Modifier Mod(string node, string stat) => Assert.Single(nodes[node].Effects.OfType<ModifierEffect>(), e => e.Modifier.Stat == stat).Modifier;
        Assert.Equal((ModKind.Increased, 8.0, 0), (Mod("heat", "damage").Kind, Mod("heat", "damage").Value, Mod("heat", "damage").RequiredTags.Items.Count));
        Assert.Equal((ModKind.Increased, 15.0), (Mod("wide_blast", "area").Kind, Mod("wide_blast", "area").Value));
        Assert.Equal((ModKind.Flat, 10.0), (Mod("molten_core", "ignite_chance").Kind, Mod("molten_core", "ignite_chance").Value));
        Assert.Equal((ModKind.Flat, 2.0), (Mod("eternal_flame", "ignite_max_stacks").Kind, Mod("eternal_flame", "ignite_max_stacks").Value));
        Assert.Equal((15.0, "ailment_ignite"), (Mod("lingering_heat", "damage").Value, Mod("lingering_heat", "damage").RequiredTags.Items.Single()));
        Assert.Equal((ModKind.Flat, -1.0), (Mod("mana_focus", "resource_cost").Kind, Mod("mana_focus", "resource_cost").Value));
        Assert.Equal((ModKind.Increased, 8.0), (Mod("swift_orb", "cast_speed").Kind, Mod("swift_orb", "cast_speed").Value));
        Assert.Equal((ModKind.More, 30.0, "target_ignited"), (Mod("flame_mark", "damage").Kind, Mod("flame_mark", "damage").Value, Mod("flame_mark", "damage").Conditions.Single().Id));
        Assert.Equal((ModKind.Flat, 2.0), (Mod("overheat", "crit_chance").Kind, Mod("overheat", "crit_chance").Value));
        Assert.Equal((ModKind.Flat, 1.0), (Mod("soul_flame", "ignite_max_stacks").Kind, Mod("soul_flame", "ignite_max_stacks").Value));
        var refund = Mod("backlash", "resource_cost");
        Assert.True(refund is { Kind: ModKind.Flat, Value: < 0, Per: { Stat: "crit_chance", Cap: > 0 } }, "Отдача: refund scales with crit chance and is capped");

        // Fork A: multi or sniper.
        Assert.Contains(nodes["scatter"].Effects, e => e is AddBehaviorEffect { Param: "projectile_count", Delta: 2 });
        Assert.Equal((ModKind.More, -25.0), (Mod("scatter", "damage").Kind, Mod("scatter", "damage").Value));
        Assert.Contains(nodes["compress"].Effects, e => e is RemoveTagEffect { Tag: "area" });
        Assert.Contains(nodes["compress"].Effects, e => e is AddBehaviorEffect { Param: "pierce_count", Delta: 1 });
        Assert.Equal((ModKind.More, 60.0), (Mod("compress", "damage").Kind, Mod("compress", "damage").Value));

        // Fork B: element change. «Морозный шар» keeps the id frost_orb (the Cryomancer grants it).
        Assert.Contains(nodes["frost_orb"].Effects, e => e is SetDamageTypeEffect { To: DamageType.Cold });
        Assert.Equal(10.0, Mod("frost_orb", "freeze_chance").Value);
        Assert.Contains(nodes["storm_orb"].Effects, e => e is SetDamageTypeEffect { To: DamageType.Lightning });
        Assert.Equal(25.0, Mod("storm_orb", "shock_chance").Value);
        Assert.Contains(nodes["storm_orb"].Effects, e => e is AddBehaviorEffect { Param: "chain_count", Delta: 1 });

        // Crit explosions: a triggered mini-skill «Взрыв» at 50%, with no ignite chance of its own.
        var wave = Assert.Single(nodes["blast_wave"].Effects.OfType<GrantProcEffect>());
        Assert.IsType<Trigger.OnCrit>(wave.Trigger);
        var cast = Assert.IsType<ProcAction.CastSkill>(wave.Action);
        Assert.Equal(50, cast.DamagePercent);
        var blast = Skills[cast.SkillId];
        Assert.True(blast.IsTriggeredOnly);
        Assert.True(blast.Tags.Contains("triggered"));
        Assert.Empty(blast.Base.AilmentChances);
        Assert.Equal(fb.Base.SpellDamage[DamageType.Fire], blast.Base.SpellDamage[DamageType.Fire]);
    }

    [Fact]
    public void Fireball_ring_of_fire_creates_ground_zone()
    {
        var ring = Skills["fireball"].Tree.Single(n => n.Id == "ring_of_fire");
        Assert.Contains(ring.Effects, e => e is SetBehaviorEffect { Param: "ground_duration", Value: 2 });
        Assert.Contains(ring.Effects, e => e is SetBehaviorEffect { Param: "ground_fraction", Value: 0.25 });

        // In the simulator the burning ground adds damage over time (25% of the hit's ailment base per second on the hit targets).
        var plain = Sorcerer(Slot("fireball", 5, ("heat", 2), ("wide_blast", 3)), Slot("spark", 1));
        var ring5 = Sorcerer(Slot("fireball", 6, ("heat", 2), ("wide_blast", 3), ("ring_of_fire", 1)), Slot("spark", 1));
        var a = ContentHarness.Run(plain, "training");
        var b = ContentHarness.Run(ring5, "training");
        Assert.Empty(b.Violations);
        Assert.True(b.DotDamage > a.DotDamage * 1.5, $"dot {a.DotDamage:0} -> {b.DotDamage:0}");
    }

    private static SkillSlot Slot(string skillId, int level, params (string Node, int Points)[] nodes)
    {
        var slot = new SkillSlot { SkillId = skillId, Level = level };
        foreach (var (n, p) in nodes) slot.NodePoints[n] = p;
        return slot;
    }

    private static Build Sorcerer(params SkillSlot[] slots)
    {
        var b = new Build { ClassId = "sorcerer", Level = 20, WeaponId = "staff" };
        b.Slots.AddRange(slots);
        return b;
    }

    private static double Single(Build b)
    {
        var r = ContentHarness.Run(b, "training");
        Assert.Empty(r.Violations);
        return r.DpsSingleTarget;
    }

    private static double Pack(Build b) => ContentHarness.Run(b, "swarm").DpsPack;

    private static double Survival(Build b)
    {
        var r = ContentHarness.Survival(b);
        Assert.Empty(r.Violations);
        return r.TimeToDeath ?? double.MaxValue;
    }

    /// <summary>Asserts that <paramref name="expected"/> scores strictly best among the fork options.</summary>
    private static void Wins(string expected, IEnumerable<string> options, Func<string, double> score, string scenario)
    {
        var scores = options.ToDictionary(o => o, score);
        var best = scores.OrderByDescending(kv => kv.Value).First();
        var runnerUp = scores.Where(kv => kv.Key != expected).Max(kv => kv.Value);
        Assert.True(best.Key == expected && scores[expected] > runnerUp * 1.01,
            $"{scenario}: expected {expected} to win, got {string.Join(", ", scores.Select(kv => $"{kv.Key}={kv.Value:0.##}"))}");
    }

    [Fact]
    public void Each_fireball_fork_option_wins_its_own_scenario()
    {
        // Fork A (Разделение / Сжатие) in a Spark loop.
        Build A(string fork) => Sorcerer(Slot("fireball", 5, ("heat", 4), (fork, 1)), Slot("spark", 1));
        string[] a = { "scatter", "compress" };
        Wins("compress", a, f => Single(A(f)), "single target");
        Wins("scatter", a, f => Pack(A(f)), "pack of 5");

        // Fork B (Морозный / Грозовой шар): lightning chains through the pack, the frost orb freezes the attacking boss.
        Build B(string fork) => Sorcerer(Slot("fireball", 3, ("heat", 2), (fork, 1)), Slot("spark", 1));
        string[] b = { "frost_orb", "storm_orb" };
        Wins("storm_orb", b, f => Pack(B(f)), "pack of 5");
        Wins("frost_orb", b, f => Survival(B(f)), "survival");
    }

    /// <summary>Mana per second of a lone generator: gain per use × uses per second.</summary>
    private static double ManaPerSecond(Build b, string skillId)
    {
        var ch = new CharacterCompiler(ContentHarness.Data, MechanicRegistry.Default).Compile(b);
        var sk = ch.Skills.Single(s => s.SkillId == skillId);
        var ev = ch.Evaluator.WithExtra(sk.Modifiers);
        var gain = ev.Evaluate("resource_gen", sk.Tags, null, sk.ResourceGain).Value;
        var speed = ev.Evaluate("cast_speed", sk.Tags, null, 1).Value;
        return gain * speed / sk.Base.CastTime;
    }

    [Fact]
    public void Each_spark_flow_option_wins_its_own_scenario()
    {
        Build S(string fork) => Sorcerer(Slot("spark", 3, ("crackle", 2), (fork, 1)));
        string[] forks = { "spark_fan", "focused_arc", "mana_siphon" };
        Wins("focused_arc", forks, f => Single(S(f)), "single target");
        Wins("spark_fan", forks, f => Pack(S(f)), "pack of 5");
        Wins("mana_siphon", forks, f => ManaPerSecond(S(f), "spark"), "mana per second");
    }

    [Fact]
    public void Each_ice_lance_form_wins_its_own_scenario()
    {
        Build L(string fork) => Sorcerer(Slot("ice_lance", 3, ("frost_edge", 2), (fork, 1)), Slot("spark", 1));
        string[] forks = { "glacial_block", "piercing_cold", "binding_lance" };
        Wins("glacial_block", forks, f => Single(L(f)), "single target");
        Wins("piercing_cold", forks, f => Pack(L(f)), "pack of 5");
        Wins("binding_lance", forks, f => Survival(L(f)), "survival");
    }

    [Fact]
    public void Each_ball_lightning_form_wins_its_own_scenario()
    {
        Build B(string fork) => Sorcerer(Slot("ball_lightning", 3, ("storm_core", 2), (fork, 1)), Slot("spark", 1));
        string[] forks = { "static_orb", "dense_orb" };
        Wins("dense_orb", forks, f => Single(B(f)), "single target");
        Wins("static_orb", forks, f => Pack(B(f)), "pack of 5");
    }

    [Fact]
    public void Each_meteor_form_wins_its_own_scenario()
    {
        Build M(string fork) => Sorcerer(Slot("meteor", 3, ("sky_fire", 2), (fork, 1)), Slot("spark", 1));
        string[] forks = { "comet_core", "starfall" };
        Wins("comet_core", forks, f => Single(M(f)), "single target");
        Wins("starfall", forks, f => Pack(M(f)), "pack of 5");
    }

    [Fact]
    public void Each_blink_burst_wins_its_own_scenario()
    {
        Build B(string fork) => Sorcerer(Slot("fireball", 1), Slot("spark", 1), Slot("blink", 1, (fork, 1)));
        string[] forks = { "arrival_flare", "frost_wake" };
        Wins("arrival_flare", forks, f => Pack(B(f)), "pack of 5");
        Wins("frost_wake", forks, f => Survival(B(f)), "survival");
        // Without a tree Blink deals no damage: the burst nodes are what make it worth a slot.
        var bare = Single(Sorcerer(Slot("fireball", 1), Slot("spark", 1), Slot("blink", 1)));
        Assert.True(Single(B("arrival_flare")) > bare);
    }

    [Fact]
    public void Blink_storm_step_shocks_and_amplifies_the_rotation()
    {
        var bare = Single(Sorcerer(Slot("fireball", 1), Slot("spark", 1), Slot("blink", 1, ("short_step", 1))));
        var storm = Single(Sorcerer(Slot("fireball", 1), Slot("spark", 1), Slot("blink", 1, ("storm_step", 1))));
        Assert.True(storm / bare > 1.05, $"storm step ×{storm / bare:0.###}");
    }

    [Fact]
    public void Sorcerer_spender_loops_beat_spark_spam_without_dwarfing_it()
    {
        // Starting numbers (Task 10 tunes): a spender + Spark rotation beats Spark alone, but by less than 2x.
        var sparkOnly = ContentHarness.TrainingDps(Sorcerer(Slot("spark", 1)));
        foreach (var spender in new[] { "fireball", "ice_lance", "ball_lightning", "meteor" })
        {
            var loop = ContentHarness.TrainingDps(Sorcerer(Slot(spender, 1), Slot("spark", 1)));
            Assert.True(loop / sparkOnly is >= 1.1 and <= 2.0, $"{spender}: loop ×{loop / sparkOnly:0.###} (Spark alone {sparkOnly:0.#})");
        }
    }

    /// <summary>
    /// No sorcerer spender pays for itself (fix-round rule from the Warrior review): at the lowest cost per second its tree, the
    /// passives and buffs of skills a sorcerer can slot can reach, chain-casting it still drains more mana than the highest regeneration
    /// the passives allow. Refunds (Отдача) are capped cost reductions, so they enter at their cap.
    /// </summary>
    [Fact]
    public void Sorcerer_spenders_never_sustain_on_maximum_regeneration()
    {
        var data = ContentHarness.Data;
        var sorcerer = data.Classes["sorcerer"];
        var slottable = sorcerer.SkillIds.Concat(data.Items.Values.Select(i => i.GrantsSkill)).Distinct().Select(id => data.Skills[id]).ToList();
        var passiveMods = data.Passives.Nodes.Values.SelectMany(n => n.Effects).OfType<ModifierEffect>().Select(e => e.Modifier).ToList();
        var buffMods = slottable.SelectMany(s => s.Tree).SelectMany(n => n.Effects).OfType<GrantProcEffect>()
            .Select(p => p.Action).OfType<ProcAction.ApplyBuff>().SelectMany(b => b.Mods).ToList();

        // Regeneration ignores skill tags: only untagged modifiers apply. Will: base + 59 levels + every attribute node (generous bound).
        var attributeNodes = data.Passives.Nodes.Values.Count(n => n.Kind == PassiveKind.Attribute);
        var will = sorcerer.BaseAttributes[Attr.Will] + 59 + 5.0 * attributeNodes;
        var regenMods = passiveMods.Concat(buffMods).Where(m => m.Stat == "resource_regen" && m.RequiredTags.Items.Count == 0).ToList();
        var willRegen = data.AttributeRules.Bonuses.Where(b => b.Attribute == Attr.Will && b.Stat == "resource_regen").Sum(b => b.ValuePerPoint)
                        + sorcerer.ClassBonuses.Where(m => m.Stat == "resource_regen" && m.Per?.Stat == "will").Sum(m => m.Value / m.Per!.Every);
        var maxRegen = (sorcerer.Resource.RegenPerSec + regenMods.Where(m => m.Kind == ModKind.Flat && m.Value > 0).Sum(m => m.Value))
                       * (1 + (regenMods.Where(m => m.Kind == ModKind.Increased && m.Value > 0).Sum(m => m.Value) + willRegen * will) / 100);

        foreach (var skill in slottable.Where(s => s.Tags.Contains("spender")))
        {
            var tags = skill.Tags.With("skill:" + skill.Id);
            bool Applies(Modifier m) => m.RequiredTags.Items.All(t => tags.Contains(t) || t == "skill:" + skill.Id);
            var outside = passiveMods.Concat(buffMods).Where(Applies).ToList();
            foreach (var picks in ForkPicks(skill.Tree))
            {
                var nodes = skill.Tree.Where(n => n.ExclusiveGroup == null || picks.Contains(n)).ToList();
                var treeMods = nodes.SelectMany(n => n.Effects.OfType<ModifierEffect>()
                    .Select(e => e.Modifier with { Value = e.Modifier.Value * (e.PerPoint ? n.MaxPoints : 1) })).ToList();
                var all = treeMods.Concat(outside).ToList();
                double CostCut(Modifier m)
                {
                    if (m.Per == null) return m.Value;
                    Assert.True(m.Per.Cap != null, $"{skill.Id}: a cost reduction scaled by {m.Per.Stat} needs a cap");
                    return -m.Per.Cap!.Value;
                }
                var baseCost = nodes.SelectMany(n => n.Effects).OfType<SetResourceEffect>().Where(e => e.Field == ResourceField.Cost)
                    .Select(e => e.Value).DefaultIfEmpty(skill.Base.ResourceCost).Last();
                var flat = all.Where(m => m.Stat == "resource_cost" && m.Kind == ModKind.Flat && m.Value < 0).Sum(CostCut);
                var inc = all.Where(m => m.Stat == "resource_cost" && m.Kind == ModKind.Increased && m.Value < 0).Sum(m => m.Value);
                var minCost = (baseCost + flat) * (1 + inc / 100);
                var slowest = 1 + all.Where(m => m.Stat == "cast_speed" && m.Kind == ModKind.Increased && m.Value < 0).Sum(m => m.Value) / 100;
                var useTime = skill.Base.CastTime / slowest;
                var picked = string.Join(", ", picks.Select(p => p.Id));
                Assert.True(maxRegen < minCost / useTime * 0.95,
                    $"{skill.Id} [{picked}]: {minCost:0.##} mana per {useTime:0.##} s vs regeneration up to {maxRegen:0.##}/s");
            }
        }
    }

    /// <summary>Every way to pick one node in each exclusive group of a tree.</summary>
    private static IEnumerable<List<SkillTreeNode>> ForkPicks(IReadOnlyList<SkillTreeNode> tree)
    {
        var groups = tree.Where(n => n.ExclusiveGroup != null).GroupBy(n => n.ExclusiveGroup).Select(g => g.ToList()).ToList();
        IEnumerable<List<SkillTreeNode>> Walk(int gi)
        {
            if (gi == groups.Count) { yield return new List<SkillTreeNode>(); yield break; }
            foreach (var rest in Walk(gi + 1))
                foreach (var n in groups[gi])
                    yield return rest.Append(n).ToList();
        }
        return Walk(0);
    }

    [Fact]
    public void Damage_type_changes_warn_about_what_they_disable()
    {
        // A node that replaces the skill's damage type silently kills ailment and type nodes built on the old type: it must say so.
        foreach (var skill in Skills.Values)
            foreach (var n in skill.Tree.Where(n => n.Effects.Any(e => e is SetDamageTypeEffect)))
                Assert.True(n.Description.Contains("не работа", StringComparison.Ordinal), $"{skill.Id}/{n.Id}: no warning in «{n.Description}»");
    }

    [Fact]
    public void Node_names_are_unique_across_all_skill_trees()
    {
        var dupes = Skills.Values.SelectMany(s => s.Tree.Select(n => (Skill: s.Id, n.Name))).GroupBy(x => x.Name)
            .Where(g => g.Count() > 1).Select(g => $"{g.Key}: {string.Join(", ", g.Select(x => x.Skill))}").ToList();
        Assert.True(dupes.Count == 0, string.Join("; ", dupes));
    }
}
