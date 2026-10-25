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
        Assert.Equal(10, blink.Base.ResourceCost);
        Assert.Equal(0, blink.Base.ResourceGain);
        Assert.DoesNotContain(blink.Tree.SelectMany(n => n.Effects), e => e is ModifierEffect { Modifier.Stat: "resource_gen" } || e is SetResourceEffect { Field: ResourceField.Gain });
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
        Assert.Equal((ModKind.More, -15.0), (Mod("scatter", "damage").Kind, Mod("scatter", "damage").Value));
        Assert.Contains(nodes["compress"].Effects, e => e is RemoveTagEffect { Tag: "area" });
        Assert.Contains(nodes["compress"].Effects, e => e is AddBehaviorEffect { Param: "pierce_count", Delta: 1 });
        Assert.Contains(nodes["compress"].Effects, e => e is ModifierEffect { Modifier: { Stat: "projectile_count", Kind: ModKind.Override, Value: 1 } });
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

    /// <summary>
    /// Survival against the attacking boss as seconds survived per point of damage taken: the boss hits in rare big blows, so time to
    /// death alone moves in whole attacks; dividing by the damage actually taken makes every blocked or softened blow count.
    /// </summary>
    private static double Survival(Build b)
    {
        var r = ContentHarness.Survival(b);
        Assert.Empty(r.Violations);
        var taken = r.DamageTaken?.Values.Sum() ?? 0;
        return taken > 0 ? (r.TimeToDeath ?? 180) / taken : double.MaxValue;
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

    // ---- Fork scenarios at skill level 20 (fix round 1): every option of a main fork, with the best compatible allocation of the
    // rest of its tree for the scenario and the reachable +1 projectile / +1 chain notables, wins its scenario against the other
    // options and against taking no fork at all ("none": the point goes to the rest of the tree). ----

    private const string NoFork = "none";

    private static readonly string[] SpellNotables = { "dex_volley", "will_chain_reaction" };
    private static readonly string[] FrostNotables = { "dex_volley", "will_chain_reaction", "int_frozen_heart" };

    /// <summary>The union of the shortest passive paths from the Sorcerer start to the given notables.</summary>
    private static List<string> PathTo(IEnumerable<string> goals)
    {
        var tree = ContentHarness.Data.Passives;
        const string start = "start_sorcerer";
        var taken = new HashSet<string>();
        foreach (var goal in goals)
        {
            var prev = new Dictionary<string, string> { [start] = start };
            var queue = new Queue<string>(new[] { start });
            while (queue.Count > 0 && !prev.ContainsKey(goal))
            {
                var at = queue.Dequeue();
                foreach (var next in tree.Edges[at])
                    if (prev.TryAdd(next, at)) queue.Enqueue(next);
            }
            for (var n = goal; n != start; n = prev[n]) taken.Add(n);
        }
        return taken.OrderBy(x => x, StringComparer.Ordinal).ToList();
    }

    /// <summary>A Sorcerer with the +1 projectile and +1 lightning-chain notables (attribute nodes into Intelligence), at the lowest
    /// level that pays for them and at least 20.</summary>
    private static Build FullSorcerer(params SkillSlot[] slots) => SorcererWith(SpellNotables, slots);

    /// <summary><see cref="FullSorcerer"/> plus the freeze notable «Ледяное сердце» and the freeze nodes on the way.</summary>
    private static Build FrostSorcerer(params SkillSlot[] slots) => SorcererWith(FrostNotables, slots);

    private static Build SorcererWith(IEnumerable<string> goals, SkillSlot[] slots)
    {
        var path = PathTo(goals);
        var b = new Build { ClassId = "sorcerer", Level = Math.Max(20, path.Count + 1), WeaponId = "staff" };
        b.PassiveNodes.AddRange(path);
        foreach (var id in path)
            if (ContentHarness.Data.Passives.Nodes[id].Kind == PassiveKind.Attribute) b.AttributeChoices[id] = Attr.Int;
        b.Slots.AddRange(slots);
        return b;
    }

    /// <summary>
    /// Spends <paramref name="level"/> points greedily in priority order: a node is skipped when another node of its exclusive group
    /// was taken or its requirements are not met yet. <see cref="NoFork"/> entries are skipped. Points the list leaves over go to the
    /// remaining non-fork nodes in tree order (the "none" variants spend their fork point there).
    /// </summary>
    private static SkillSlot Layout(string skillId, int level, params (string Node, int Points)[] priority)
    {
        var tree = Skills[skillId].Tree.ToDictionary(n => n.Id);
        var slot = new SkillSlot { SkillId = skillId, Level = level };
        var left = level;
        var groups = new Dictionary<string, string>();
        foreach (var (id, want) in priority)
        {
            if (id == NoFork || left == 0) continue;
            var n = tree[id];
            if (n.ExclusiveGroup != null && groups.TryGetValue(n.ExclusiveGroup, out var g) && g != id) continue;
            if (!n.Requires.All(r => slot.NodePoints.GetValueOrDefault(r.NodeId) >= r.Points)) continue;
            var have = slot.NodePoints.GetValueOrDefault(id);
            var add = Math.Min(Math.Min(want, n.MaxPoints) - have, left);
            if (add <= 0) continue;
            slot.NodePoints[id] = have + add;
            left -= add;
            if (n.ExclusiveGroup != null) groups[n.ExclusiveGroup] = id;
        }
        foreach (var n in Skills[skillId].Tree.Where(n => n.ExclusiveGroup == null))
        {
            if (left == 0) break;
            if (!n.Requires.All(r => slot.NodePoints.GetValueOrDefault(r.NodeId) >= r.Points)) continue;
            var add = Math.Min(n.MaxPoints - slot.NodePoints.GetValueOrDefault(n.Id), left);
            if (add <= 0) continue;
            slot.NodePoints[n.Id] = slot.NodePoints.GetValueOrDefault(n.Id) + add;
            left -= add;
        }
        Assert.True(left == 0, $"{skillId}: {left} points left unspent");
        return slot;
    }

    /// <summary>The Spark of the spender rotations: a mana-oriented layout, the same for every option compared.</summary>
    private static SkillSlot LoopSpark() => Layout("spark", 20, ("crackle", 4), ("mana_siphon", 1), ("mana_current", 3), ("quick_spark", 3),
        ("bright_flash", 3), ("dazzle", 1), ("static", 3), ("breakdown", 2));

    [Fact]
    public void Full_fork_builds_take_the_reachable_notables()
    {
        foreach (var b in new[] { FullSorcerer(), FrostSorcerer() })
        {
            Assert.Contains("dex_volley", b.PassiveNodes);
            Assert.Contains("will_chain_reaction", b.PassiveNodes);
            Assert.Empty(BuildRules.Validate(b, ContentHarness.Data));
        }
        Assert.Contains("int_frozen_heart", FrostSorcerer().PassiveNodes);
    }

    [Fact]
    public void Each_fireball_fork_option_wins_its_own_scenario()
    {
        // Fork A (Разделение / Сжатие).
        string[] a = { "scatter", "compress", NoFork };
        Build ASingle(string f) => FullSorcerer(Layout("fireball", 20, ("heat", 4), (f, 1), ("overheat", 3), ("blast_wave", 1), ("molten_core", 3),
            ("flame_mark", 1), ("eternal_flame", 1), ("soul_flame", 2), ("lingering_heat", 3), ("mana_focus", 3)), LoopSpark());
        Build APack(string f) => FullSorcerer(Layout("fireball", 20, ("heat", 4), (f, 1), ("wide_blast", 3), ("ring_of_fire", 1), ("molten_core", 3),
            ("flame_mark", 1), ("eternal_flame", 1), ("soul_flame", 2), ("lingering_heat", 3), ("mana_focus", 3)), LoopSpark());
        Wins("compress", a, f => Single(ASingle(f)), "fireball A, single target");
        Wins("scatter", a, f => Pack(APack(f)), "fireball A, pack of 5");

        // Fork B (Морозный / Грозовой шар). Element orbs fill the tree with crit and economy, the fire orb with ignite.
        string[] b = { "frost_orb", "storm_orb" };
        SkillSlot BTree(string f) => Layout("fireball", 20, ("heat", 4), (f, 1), ("scatter", 1), ("wide_blast", 3), ("ring_of_fire", 1),
            ("overheat", 3), ("blast_wave", 1), ("mana_focus", 3), ("backlash", 1), ("swift_orb", 2));
        Wins("storm_orb", b, f => Pack(FullSorcerer(BTree(f), LoopSpark())), "fireball B, pack of 5");
        Wins("storm_orb", b, f => Single(FullSorcerer(BTree(f), LoopSpark())), "fireball B, single target");
        // A frost build takes the freeze passives on the way to «Ледяное сердце»; fire and lightning orbs cannot freeze with them.
        Wins("frost_orb", b.Append(NoFork), f => Survival(FrostSorcerer(BTree(f), LoopSpark())), "fireball B, survival");
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
        Build S(string f) => FullSorcerer(Layout("spark", 20, ("crackle", 4), (f, 1), ("mana_current", 3), ("quick_spark", 3), ("bright_flash", 3),
            ("dazzle", 1), ("static", 3), ("breakdown", 2), ("ionize", 1)));
        string[] forks = { "spark_fan", "focused_arc", "mana_siphon", NoFork };
        Wins("focused_arc", forks, f => Single(S(f)), "spark, single target");
        Wins("spark_fan", forks, f => Pack(S(f)), "spark, pack of 5");
        Wins("mana_siphon", forks, f => ManaPerSecond(S(f), "spark"), "spark, mana per second");
    }

    [Fact]
    public void Each_ice_lance_form_wins_its_own_scenario()
    {
        string[] forks = { "glacial_block", "piercing_cold", "binding_lance", NoFork };
        Build Damage(string f) => FullSorcerer(Layout("ice_lance", 20, ("frost_edge", 4), (f, 1), ("ice_wedge", 2), ("point", 3), ("cold_precision", 2),
            ("ice_crown", 1), ("ice_sting", 3), ("swift_ice", 3), ("cold_poise", 3)), LoopSpark());
        Build Guard(string f) => FullSorcerer(Layout("ice_lance", 20, ("frost_edge", 2), (f, 1), ("bitter_cold", 3), ("deep_freeze", 3), ("ice_shards", 1),
            ("ice_carapace", 1), ("point", 3), ("ice_crown", 1), ("frost_edge", 4), ("swift_ice", 3)), LoopSpark());
        Wins("glacial_block", forks, f => Single(Damage(f)), "ice lance, single target");
        Wins("piercing_cold", forks, f => Pack(Damage(f)), "ice lance, pack of 5");
        Wins("binding_lance", forks, f => Survival(Guard(f)), "ice lance, survival");
    }

    [Fact]
    public void Each_ball_lightning_form_wins_its_own_scenario()
    {
        string[] forks = { "static_orb", "dense_orb", NoFork };
        Build Single1(string f) => FullSorcerer(Layout("ball_lightning", 20, ("storm_core", 4), (f, 1), ("long_flight", 3), ("bright_discharge", 3),
            ("flash", 1), ("static_field", 3), ("spark_cascade", 2), ("quick_charge", 3), ("twin_orb", 1)), LoopSpark());
        Build PackB(string f) => FullSorcerer(Layout("ball_lightning", 20, ("storm_core", 4), (f, 1), ("long_flight", 3), ("big_orb", 3), ("arc_discharge", 1),
            ("static_field", 3), ("spark_cascade", 2), ("quick_charge", 3), ("capacitor", 1)), LoopSpark());
        Wins("dense_orb", forks, f => Single(Single1(f)), "ball lightning, single target");
        Wins("static_orb", forks, f => Pack(PackB(f)), "ball lightning, pack of 5");
    }

    [Fact]
    public void Each_meteor_form_wins_its_own_scenario()
    {
        string[] forks = { "comet_core", "starfall", NoFork };
        Build SingleM(string f) => FullSorcerer(Layout("meteor", 20, ("sky_fire", 4), (f, 1), ("falling_star", 3), ("comet_fragment", 1), ("kindling", 3),
            ("core_heat", 2), ("star_thrift", 3), ("fire_field", 1), ("scorched_earth", 3)), LoopSpark());
        Build PackM(string f) => FullSorcerer(Layout("meteor", 20, ("sky_fire", 4), (f, 1), ("crater", 3), ("fire_field", 1), ("scorched_earth", 3),
            ("kindling", 3), ("core_heat", 2), ("star_thrift", 3)), LoopSpark());
        Wins("comet_core", forks, f => Single(SingleM(f)), "meteor, single target");
        Wins("starfall", forks, f => Pack(PackM(f)), "meteor, pack of 5");
    }

    /// <summary>Fireball + Spark at level 20, the rotation Blink is added to.</summary>
    private static SkillSlot BlinkRotationFireball() => Layout("fireball", 20, ("heat", 4), ("scatter", 1), ("wide_blast", 3), ("ring_of_fire", 1),
        ("molten_core", 3), ("flame_mark", 1), ("eternal_flame", 1), ("soul_flame", 2), ("lingering_heat", 3), ("mana_focus", 1));

    [Fact]
    public void Each_blink_burst_wins_its_own_scenario()
    {
        string[] forks = { "arrival_flare", "frost_wake", NoFork };
        // Blink first in the rotation: it is used whenever its cooldown is ready.
        Build B(string f) => FullSorcerer(Layout("blink", 20, (f, 1), ("double_flare", 1), ("deep_cold", 3), ("short_step", 5), ("light_step", 5),
            ("storm_step", 1), ("high_voltage", 5), ("afterimage", 1), ("elusive", 1), ("phase_barrier", 1)), BlinkRotationFireball(), LoopSpark());
        Wins("arrival_flare", forks, f => Pack(B(f)), "blink, pack of 5");
        Wins("arrival_flare", forks, f => Single(B(f)), "blink, single target");
        Wins("frost_wake", forks, f => Survival(B(f)), "blink, survival");
    }

    /// <summary>
    /// Blink is a utility slot, not a mandatory damage multiplier (fix round 1): at maximum attainable cooldown recovery (tree, passives,
    /// buffs of slottable skills) no buff and no shock it grants is up more than about half the time.
    /// </summary>
    [Fact]
    public void Blink_buffs_and_shock_stay_up_at_most_half_the_time()
    {
        var data = ContentHarness.Data;
        var blink = Skills["blink"];
        var tags = blink.Tags.With("skill:blink");
        var passiveMods = data.Passives.Nodes.Values.SelectMany(n => n.Effects).OfType<ModifierEffect>().Select(e => e.Modifier);
        var buffMods = data.Classes["sorcerer"].SkillIds.Select(id => Skills[id]).SelectMany(s => s.Tree).SelectMany(n => n.Effects)
            .OfType<GrantProcEffect>().Select(p => p.Action).OfType<ProcAction.ApplyBuff>().SelectMany(b => b.Mods);
        var treeMods = blink.Tree.SelectMany(n => n.Effects.OfType<ModifierEffect>().Select(e => e.Modifier with { Value = e.Modifier.Value * n.MaxPoints }));
        var cdr = treeMods.Concat(passiveMods).Concat(buffMods)
            .Where(m => m is { Stat: "cooldown_recovery", Kind: ModKind.Increased, Value: > 0 } && m.RequiredTags.Items.All(t => tags.Contains(t)))
            .Sum(m => m.Value);
        var minCooldown = blink.Base.Cooldown!.Value / (1 + cdr / 100);

        foreach (var n in blink.Tree)
            foreach (var p in n.Effects.OfType<GrantProcEffect>())
            {
                var every = p.Trigger is Trigger.EveryNUses e ? e.N : 1;
                var duration = p.Action switch
                {
                    ProcAction.ApplyBuff b => b.DurationSec,
                    ProcAction.ApplyAilment { AilmentId: "shock" or "chill" } a => data.Ailments[a.AilmentId].DurationSec,
                    _ => 0,
                };
                Assert.True(duration <= minCooldown * every * 0.5 + 1e-9,
                    $"blink/{n.Id}: {duration} s every {minCooldown * every:0.##} s at maximum cooldown recovery (+{cdr}%)");
            }
    }

    [Fact]
    public void Blink_adds_a_bounded_bonus_to_the_rotation()
    {
        // Offence-oriented Blink at level 20 (storm step, focus, fire exit, spark trail) against the same rotation without it.
        var two = Single(FullSorcerer(BlinkRotationFireball(), LoopSpark()));
        var storm = Single(FullSorcerer(Layout("blink", 20, ("storm_step", 1), ("high_voltage", 5), ("short_step", 5), ("light_step", 5),
            ("blink_focus", 1), ("lingering_focus", 1), ("impulse", 1), ("arrival_flare", 1)), BlinkRotationFireball(), LoopSpark()));
        var full = Single(FullSorcerer(Layout("blink", 20, ("storm_step", 1), ("high_voltage", 5), ("short_step", 5), ("blink_focus", 1),
            ("lingering_focus", 1), ("impulse", 1), ("arrival_flare", 1), ("double_flare", 1), ("spark_trail", 1), ("twin_spark", 1),
            ("fire_exit", 1), ("light_step", 1)), BlinkRotationFireball(), LoopSpark()));
        Assert.True(storm / two is > 1.05 and < 1.35, $"storm/focus blink ×{storm / two:0.###}");
        Assert.True(full / two < 1.4, $"offensive blink ×{full / two:0.###} (storm/focus ×{storm / two:0.###})");
    }

    [Fact]
    public void Meteor_copies_refresh_the_fire_field_without_weakening_it()
    {
        // Zones are one per skill id: the comet fragment's copy refreshes the field instead of adding a second one. Its ailment base is
        // the same (the copy's 40% is a More modifier, not a base change), so the field's damage over time does not drop.
        Build M(bool fragment) => FullSorcerer(Layout("meteor", 20, ("sky_fire", 4), ("fire_field", 1), ("scorched_earth", 3), ("falling_star", 3),
            (fragment ? "comet_fragment" : "crater", 1), ("crater", 3), ("star_thrift", 3), ("instant_fall", 1), ("kindling", 1)), LoopSpark());
        var without = ContentHarness.Run(M(false), "training");
        var with = ContentHarness.Run(M(true), "training");
        Assert.Empty(with.Violations);
        Assert.True(with.DotDamage >= without.DotDamage * 0.98, $"dot {without.DotDamage:0} -> {with.DotDamage:0}");
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
    /// passives, ascendancy nodes and buffs of skills a sorcerer can slot can reach, chain-casting it still drains more mana than the
    /// highest regeneration the passives and ascendancies allow. Refunds (Отдача) are capped cost reductions, so they enter at their cap.
    /// «Жар души» (spec 10.4) is the one explicit exception, see below.
    /// </summary>
    [Fact]
    public void Sorcerer_spenders_never_sustain_on_maximum_regeneration()
    {
        var data = ContentHarness.Data;
        var sorcerer = data.Classes["sorcerer"];
        var slottable = sorcerer.SkillIds.Concat(data.Items.Values.Select(i => i.GrantsSkill)).Distinct().Select(id => data.Skills[id]).ToList();
        // Ascendancy nodes (spec 10.4) are sources too: their modifiers (a scope becomes a tag requirement) and buffs join the pools.
        var ascEffects = sorcerer.AscendancyIds.Select(id => data.Ascendancies[id]).SelectMany(a => a.Notables.Append(a.Start))
            .SelectMany(n => n.Effects).ToList();
        static Modifier Scoped(ModifierEffect e) => e.Scope switch
        {
            Scope.Skill sk => e.Modifier with { RequiredTags = e.Modifier.RequiredTags.With("skill:" + sk.SkillId) },
            Scope.Tags t => e.Modifier with { RequiredTags = t.Required.Items.Aggregate(e.Modifier.RequiredTags, (acc, tag) => acc.With(tag)) },
            _ => e.Modifier,
        };
        // Resource-flow mechanics of the catalogue: the only one a sorcerer ascendancy may carry is «Жар души» (soul_heat). Spec 10.4
        // makes it the deliberate exception («почти бесконечная мана… осознанное решение»: Fireball and Meteor restore 1% mana per
        // ignited target hit), so it is exempt and stays out of the sums below; without it no spender may sustain.
        var resourceMechanics = new[] { "soul_heat", "insatiable", "whirl_rage", "battle_trance", "berserk_frenzy" };
        Assert.All(ascEffects.OfType<MechanicEffect>().Where(m => resourceMechanics.Contains(m.MechanicId)),
            m => Assert.Equal("soul_heat", m.MechanicId));
        Assert.Contains(ascEffects, e => e is MechanicEffect { MechanicId: "soul_heat" }); // the exemption is real: Пироман carries it
        var passiveMods = data.Passives.Nodes.Values.SelectMany(n => n.Effects).OfType<ModifierEffect>().Select(e => e.Modifier)
            .Concat(ascEffects.OfType<ModifierEffect>().Select(Scoped)).ToList();
        var buffMods = slottable.SelectMany(s => s.Tree).SelectMany(n => n.Effects).Concat(ascEffects).OfType<GrantProcEffect>()
            .Select(p => p.Action).OfType<ProcAction.ApplyBuff>().SelectMany(b => b.Mods).ToList();

        // Regeneration ignores skill tags: only untagged modifiers apply. Will: base + 59 levels + every attribute node (generous bound).
        var attributeNodes = data.Passives.Nodes.Values.Count(n => n.Kind == PassiveKind.Attribute);
        var will = sorcerer.BaseAttributes[Attr.Will] + 59 + 5.0 * attributeNodes;
        var regenMods = passiveMods.Concat(buffMods).Where(m => m.Stat == "resource_regen" && m.RequiredTags.Items.Count == 0).ToList();
        var willRegen = data.AttributeRules.Bonuses.Where(b => b.Attribute == Attr.Will && b.Stat == "resource_regen").Sum(b => b.ValuePerPoint)
                        + sorcerer.ClassBonuses.Where(m => m.Stat == "resource_regen" && m.Per?.Stat == "will").Sum(m => m.Value / m.Per!.Every);
        var maxRegen = (sorcerer.Resource.RegenPerSec + regenMods.Where(m => m.Kind == ModKind.Flat && m.Value > 0).Sum(m => m.Value))
                       * (1 + (regenMods.Where(m => m.Kind == ModKind.Increased && m.Value > 0).Sum(m => m.Value) + willRegen * will) / 100);

        // Inflow from slottable non-generators that gain mana per use (fix round 1: Blink once did): the highest gain per use over the
        // shortest attainable cooldown. A non-generator without a cooldown must not gain mana at all.
        var willGen = data.AttributeRules.Bonuses.Where(b => b.Attribute == Attr.Will && b.Stat == "resource_gen").Sum(b => b.ValuePerPoint)
                      + sorcerer.ClassBonuses.Where(m => m.Stat == "resource_gen" && m.Per?.Stat == "will").Sum(m => m.Value / m.Per!.Every);
        double inflow = 0;
        foreach (var s in slottable.Where(s => !s.Tags.Contains("generator")))
        {
            var stags = s.Tags.With("skill:" + s.Id);
            var mods = s.Tree.SelectMany(n => n.Effects.OfType<ModifierEffect>()
                    .Select(e => e.Modifier with { Value = e.Modifier.Value * (e.PerPoint ? n.MaxPoints : 1) }))
                .Concat(passiveMods).Concat(buffMods).Where(m => m.Value > 0 && m.RequiredTags.Items.All(t => stags.Contains(t))).ToList();
            var gainBase = s.Tree.SelectMany(n => n.Effects).OfType<SetResourceEffect>().Where(e => e.Field == ResourceField.Gain)
                .Select(e => e.Value).Append(s.Base.ResourceGain).Max();
            var gain = (gainBase + mods.Where(m => m is { Stat: "resource_gen", Kind: ModKind.Flat }).Sum(m => m.Value))
                       * (1 + (mods.Where(m => m is { Stat: "resource_gen", Kind: ModKind.Increased }).Sum(m => m.Value) + willGen * will) / 100);
            if (gain <= 0) continue;
            Assert.True(s.Base.Cooldown is > 0, $"{s.Id}: a non-generator without a cooldown gains {gain:0.#} mana per use");
            var cdr = mods.Where(m => m is { Stat: "cooldown_recovery", Kind: ModKind.Increased }).Sum(m => m.Value);
            inflow += gain / (s.Base.Cooldown!.Value / (1 + cdr / 100));
        }

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
                Assert.True(maxRegen + inflow < minCost / useTime * 0.95,
                    $"{skill.Id} [{picked}]: {minCost:0.##} mana per {useTime:0.##} s vs regeneration up to {maxRegen:0.##}/s + {inflow:0.##}/s from other skills");
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
