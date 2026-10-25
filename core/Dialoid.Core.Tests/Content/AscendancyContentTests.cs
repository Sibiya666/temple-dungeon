using System.Text.Json;
using Dialoid.Core.Character;
using Dialoid.Core.Combat;
using Dialoid.Core.Effects;
using Dialoid.Core.Passives;
using Dialoid.Core.Simulation;
using Dialoid.Core.Stats;
using Attr = Dialoid.Core.Character.Attribute;

namespace Dialoid.Core.Tests.Content;

/// <summary>Spec 10.4 (ascendancies) over the real data.</summary>
public class AscendancyContentTests
{
    private static Ascendancy Asc(string id) => ContentHarness.Data.Ascendancies[id];

    private static AscendancyNode Notable(string asc, string id) => Asc(asc).Notables.Single(n => n.Id == id);

    private static bool IsCyrillic(string s) => s.Any(c => c is >= 'А' and <= 'я' or 'ё' or 'Ё');

    [Theory]
    [InlineData("berserker", "warrior")]
    [InlineData("thunderer", "warrior")]
    public void Ascendancy_has_start_and_six_two_point_notables(string id, string classId)
    {
        Assert.True(ContentHarness.Data.Ascendancies.ContainsKey(id), $"no ascendancy '{id}'");
        var asc = Asc(id);
        Assert.Equal(classId, asc.ClassId);
        Assert.Contains(id, ContentHarness.Data.Classes[classId].AscendancyIds);
        Assert.NotEmpty(asc.Start.Effects);
        Assert.Equal(6, asc.Notables.Count);
        Assert.All(asc.Notables, n => Assert.Equal(2, n.Cost));
        Assert.All(asc.Notables, n => Assert.NotEmpty(n.Effects));
        var nodes = asc.Notables.Append(asc.Start).ToList();
        Assert.Equal(nodes.Count, nodes.Select(n => n.Id).Distinct().Count());
        Assert.Equal(nodes.Count, nodes.Select(n => n.Name).Distinct().Count());
        Assert.True(IsCyrillic(asc.Name), $"{id}: name «{asc.Name}» is not Russian");
        Assert.All(nodes, n => Assert.True(IsCyrillic(n.Name) && IsCyrillic(n.Description), $"{id}/{n.Id}: name or description not Russian"));
    }

    /// <summary>
    /// Anti-goal 4 (spec 10.4): a notable changes a rule. «Bare» means every effect is an unconditional modifier of a scaling stat.
    /// Rule-changing effects: mechanic, conversion, behavior, damage type, tag, resource, proc, a conditional (or per-stat)
    /// modifier, and modifiers that move a defensive rule rather than scale damage — resistance penetration and the
    /// maximum-resistance cap (Буря в жилах, written so in spec 10.4).
    /// </summary>
    private static bool ChangesRule(Effect e) => e switch
    {
        MechanicEffect or AddConversionEffect or SetBehaviorEffect or AddBehaviorEffect or SetDamageTypeEffect
            or AddTagEffect or RemoveTagEffect or SetResourceEffect or GrantProcEffect => true,
        ModifierEffect { Modifier: var m } => m.Conditions.Count > 0 || m.Per != null
            || m.Stat.StartsWith("penetration_", StringComparison.Ordinal)
            || (m.Stat.StartsWith("resist_", StringComparison.Ordinal) && m.Stat.EndsWith("_max", StringComparison.Ordinal)),
        _ => false,
    };

    [Fact]
    public void No_notable_is_a_bare_percentage()
    {
        Assert.NotEmpty(ContentHarness.Data.Ascendancies);
        foreach (var asc in ContentHarness.Data.Ascendancies.Values)
            foreach (var n in asc.Notables)
                Assert.True(n.Effects.Any(ChangesRule), $"{asc.Id}/{n.Id}: only bare modifiers");
    }

    private static MechanicEffect Mech(AscendancyNode n, string id) =>
        n.Effects.OfType<MechanicEffect>().SingleOrDefault(m => m.MechanicId == id)
        ?? throw new Xunit.Sdk.XunitException($"{n.Id}: no mechanic '{id}'");

    private static string Param(MechanicEffect m, string name) =>
        m.Params.TryGetValue(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : throw new Xunit.Sdk.XunitException($"{m.MechanicId}: no param '{name}'");

    [Fact]
    public void Berserker_nodes_match_spec()
    {
        var asc = Asc("berserker");
        Assert.IsType<Scope.Global>(Mech(asc.Start, "berserk_frenzy").Scope);
        Mech(Notable("berserker", "insatiable"), "insatiable");
        var feast = Notable("berserker", "blood_feast");
        Assert.IsType<Scope.Global>(Mech(feast, "blood_feast").Scope);
        var stacks = feast.Effects.OfType<ModifierEffect>().Single(e => e.Modifier.Stat == "bleed_max_stacks").Modifier;
        Assert.Equal((ModKind.Flat, 7.0), (stacks.Kind, stacks.Value));
        Assert.IsType<Scope.Global>(Mech(Notable("berserker", "reckless"), "reckless").Scope);
        Assert.Equal("whirlwind", Param(Mech(Notable("berserker", "whirl_rage"), "whirl_rage"), "skill"));
        Mech(Notable("berserker", "flesh_rend"), "flesh_rend");
        var trance = Mech(Notable("berserker", "battle_trance"), "battle_trance");
        Assert.IsType<Scope.Global>(trance.Scope);
        Assert.Equal("battle_cry", Param(trance, "buff"));
    }

    [Fact]
    public void Thunderer_nodes_match_spec()
    {
        var asc = Asc("thunderer");
        var conv = Assert.Single(asc.Start.Effects.OfType<AddConversionEffect>());
        Assert.Equal((DamageType.Physical, DamageType.Lightning, 25.0), (conv.From, conv.To, conv.Percent));
        Assert.Equal(TagSet.Of("attack"), Assert.IsType<Scope.Tags>(conv.Scope).Required);

        var discharge = Notable("thunderer", "discharge");
        Mech(discharge, "ground_type");
        Assert.All(discharge.Effects, e => Assert.Equal(new Scope.Skill("crush"), e.Scope));
        var beh = discharge.Effects.OfType<SetBehaviorEffect>().ToDictionary(e => e.Param, e => e.Value);
        Assert.Equal(3, beh["ground_duration"]);
        Assert.Equal(0.2, beh["ground_fraction"]);
        var shock = discharge.Effects.OfType<ModifierEffect>().Single(e => e.Modifier.Stat == "shock_chance").Modifier;
        Assert.Equal((ModKind.Flat, 100.0), (shock.Kind, shock.Value));

        var chain = Assert.Single(Notable("thunderer", "chain_strike").Effects.OfType<AddBehaviorEffect>());
        Assert.Equal(("chain_count", 1.0), (chain.Param, chain.Delta));
        Assert.True(Assert.IsType<Scope.Tags>(chain.Scope).Required.Contains("melee"));

        Assert.IsType<Scope.Global>(Mech(Notable("thunderer", "static_charge"), "static_charge").Scope);

        // Громовой топор: the throwing axe and its triggered mini-skill (fork «Возвращение») both become lightning and chain.
        var axe = Notable("thunderer", "thunder_axe");
        foreach (var skill in new[] { "throwing_axe", "returning_axe" })
        {
            var scope = new Scope.Skill(skill);
            Assert.Contains(axe.Effects, e => e is SetDamageTypeEffect { To: DamageType.Lightning } && e.Scope == scope);
            Assert.Contains(axe.Effects, e => e is AddBehaviorEffect { Param: "chain_count", Delta: 1 } && e.Scope == scope);
        }

        Mech(Notable("thunderer", "grounding"), "grounding");
        var storm = Notable("thunderer", "storm_veins").Effects.OfType<ModifierEffect>().Select(e => e.Modifier).ToList();
        Assert.Contains(storm, m => m is { Stat: "penetration_lightning", Kind: ModKind.Flat, Value: 15 } && m.RequiredTags.Contains("attack"));
        Assert.Contains(storm, m => m is { Stat: "resist_lightning_max", Kind: ModKind.Flat, Value: 10 });
    }

    // ---- Temporary level-50 builds (until reference builds exist) ----

    /// <summary>The first <paramref name="count"/> warrior passives in breadth-first order from the class start, keystones
    /// excluded; attribute nodes take Strength. Breadth-first order keeps every prefix connected to the start.</summary>
    private static (List<string> Nodes, Dictionary<string, Attr> Choices) WarriorPassives(int count)
    {
        var tree = ContentHarness.Data.Passives;
        var start = ContentHarness.Data.Classes["warrior"].PassiveStartNodeId;
        var seen = new HashSet<string> { start };
        var queue = new Queue<string>(new[] { start });
        var nodes = new List<string>();
        while (queue.Count > 0 && nodes.Count < count)
        {
            foreach (var next in tree.Edges[queue.Dequeue()])
            {
                if (!seen.Add(next) || tree.Nodes[next].Kind is PassiveKind.Keystone or PassiveKind.Start) continue;
                queue.Enqueue(next);
                if (nodes.Count < count) nodes.Add(next);
            }
        }
        var choices = nodes.Where(n => tree.Nodes[n].Kind == PassiveKind.Attribute).ToDictionary(n => n, _ => Attr.Str);
        return (nodes, choices);
    }

    private static Build Warrior50(string? ascendancy, string weapon, params string[] notables)
    {
        var (nodes, choices) = WarriorPassives(49);
        return new Build
        {
            ClassId = "warrior", Level = 50, WeaponId = weapon,
            AscendancyId = ascendancy, AscendancyNodes = notables.ToList(),
            PassiveNodes = nodes, AttributeChoices = choices,
        };
    }

    private static SkillSlot Slot(string skill, params (string Node, int Points)[] nodes)
    {
        var slot = new SkillSlot { SkillId = skill, Level = 20 };
        foreach (var (n, p) in nodes) slot.NodePoints[n] = p;
        return slot;
    }

    /// <summary>Вихрь-берсерк: Вихрь ярости + Боевой транс поднимают Ярость до 100 и держат «Безумие» на бесплатном канале.</summary>
    private static Build WhirlBerserker(params string[] notables)
    {
        var b = Warrior50("berserker", "greataxe", notables);
        b.Slots.Add(Slot("battle_cry", ("war_roar", 1)));
        b.Slots.Add(Slot("whirlwind", ("steel_storm", 4), ("spin_up", 3), ("blade_hurricane", 1), ("cutting_wind", 3), ("thousand_cuts", 2)));
        b.Slots.Add(Slot("cleave", ("lacerate", 3), ("deep_cut", 2), ("thirst", 3), ("heavy_hand", 4)));
        return b;
    }

    /// <summary>Кровавый вихрь: долгий Боевой клич с «Кличем крови» набирает 100 Ярости в трансе; дальше Вихрь платит здоровьем
    /// (Неутолимый), и Без оглядки растет по мере потери здоровья. С Вихрем ярости — бесплатный канал на кровотечениях.</summary>
    private static Build BloodWhirl(params string[] notables)
    {
        var b = Warrior50("berserker", "greataxe", notables);
        b.Slots.Add(Slot("battle_cry", ("war_roar", 1), ("long_roar", 1), ("blood_cry", 1), ("endless_rage", 1), ("inspiration", 1)));
        b.Slots.Add(Slot("whirlwind", ("steel_storm", 4), ("spin_up", 3), ("blade_hurricane", 1), ("cutting_wind", 3), ("thousand_cuts", 2), ("fast_turns", 3)));
        b.Slots.Add(Slot("cleave", ("lacerate", 3), ("deep_cut", 2), ("thirst", 3), ("heavy_hand", 4)));
        return b;
    }

    /// <summary>Крит-кровотечение без спендеров: Точный удар, Таран и топор держат 100 Ярости; Рассечение плоти на критах.</summary>
    private static Build CritBleedBerserker(params string[] notables)
    {
        var b = Warrior50("berserker", "greataxe", notables);
        b.Slots.Add(Slot("cleave", ("lacerate", 3), ("deep_cut", 2), ("bloodletting", 1), ("heavy_hand", 4), ("wide_swing", 3), ("precise_strike", 1), ("edge", 2)));
        b.Slots.Add(Slot("charge", ("momentum", 4), ("ram", 1), ("bloody_path", 3), ("gore", 1), ("true_lunge", 3)));
        b.Slots.Add(Slot("throwing_axe", ("jagged_edge", 3), ("keen_throw", 3), ("headshot", 2), ("whetstone", 4)));
        return b;
    }

    /// <summary>Громовой молот: Сокрушение молнией с Разрядом, Статическим зарядом, Заземлением и Бурей в жилах.</summary>
    private static Build ThunderCrusher(params string[] notables)
    {
        var b = Warrior50("thunderer", "greataxe", notables);
        b.Slots.Add(Slot("crush", ("brute_force", 4), ("thunder_hammer", 1), ("static_discharge", 3), ("heavy_blow", 3), ("cold_calculation", 3), ("fury_return", 1), ("peak_fury", 1)));
        b.Slots.Add(Slot("cleave", ("heavy_hand", 4), ("thunder_strike", 1), ("thirst", 3), ("wide_swing", 3)));
        return b;
    }

    /// <summary>Точные удары: Точный удар и Таран без площади — им Цепной удар добавляет вторую цель.</summary>
    private static Build PreciseThunderer(params string[] notables)
    {
        var b = Warrior50("thunderer", "greataxe", notables);
        b.Slots.Add(Slot("crush", ("brute_force", 4), ("thunder_hammer", 1), ("static_discharge", 3), ("heavy_blow", 3), ("cold_calculation", 3), ("fury_return", 1), ("peak_fury", 1)));
        b.Slots.Add(Slot("cleave", ("heavy_hand", 4), ("wide_swing", 3), ("precise_strike", 1), ("edge", 2), ("thunder_strike", 1), ("thirst", 3)));
        b.Slots.Add(Slot("charge", ("momentum", 4), ("ram", 1), ("storm_charge", 1), ("true_lunge", 3), ("finishing_blow", 1)));
        return b;
    }

    /// <summary>Метатель: один Метательный топор с «Возвращением».</summary>
    private static Build AxeThunderer(params string[] notables)
    {
        var b = Warrior50("thunderer", "sword", notables);
        b.Slots.Add(Slot("throwing_axe", ("whetstone", 4), ("returning", 1), ("catch", 1), ("storm_edge", 1), ("keen_throw", 3), ("headshot", 2), ("quick_throw", 3), ("thrower_fury", 3)));
        return b;
    }

    [Fact]
    public void Berserker_build_compiles_and_simulates()
    {
        var b = BloodWhirl("battle_trance", "insatiable", "reckless", "blood_feast");
        Assert.Empty(BuildRules.Validate(b, ContentHarness.Data));
        var r = ContentHarness.Run(b, "training");
        Assert.Empty(r.Violations);
        Assert.True(r.DpsSingleTarget > 0);
    }

    [Fact]
    public void Thunderer_build_compiles_and_simulates()
    {
        var b = ThunderCrusher("discharge", "static_charge", "grounding", "storm_veins");
        Assert.Empty(BuildRules.Validate(b, ContentHarness.Data));
        var r = ContentHarness.Run(b, "training");
        Assert.Empty(r.Violations);
        Assert.True(r.DpsSingleTarget > 0);
        Assert.True(r.DamageByType.GetValueOrDefault(DamageType.Lightning) > 0);
    }

    private static double Metric(Build b, string metric) => metric switch
    {
        "training" => ContentHarness.Run(b, "training").DpsSingleTarget,
        "boss" => ContentHarness.Run(b, "boss").DpsSingleTarget,
        "swarm" => ContentHarness.Run(b, "swarm").DpsPack,
        _ => throw new ArgumentException(metric),
    };

    /// <summary>Each notable is worth its 2 points in at least one reference-style build: taking it (on top of the build's
    /// other picks) raises that build's metric by more than 5%.</summary>
    [Theory]
    [InlineData("whirl_rage", "whirl", "training", new[] { "battle_trance" })]
    [InlineData("battle_trance", "whirl", "training", new[] { "whirl_rage" })]
    [InlineData("insatiable", "blood_whirl", "training", new[] { "battle_trance" })]
    [InlineData("reckless", "blood_whirl", "training", new[] { "battle_trance", "insatiable" })]
    [InlineData("blood_feast", "blood_whirl", "training", new[] { "whirl_rage", "battle_trance", "flesh_rend" })]
    [InlineData("flesh_rend", "crit_bleed", "training", new string[0])]
    [InlineData("discharge", "crusher", "training", new string[0])]
    [InlineData("chain_strike", "precise", "swarm", new string[0])]
    [InlineData("static_charge", "crusher", "training", new string[0])]
    [InlineData("thunder_axe", "axe", "swarm", new string[0])]
    [InlineData("grounding", "precise", "training", new string[0])]
    [InlineData("storm_veins", "precise", "boss", new string[0])]
    public void Every_notable_pays_off_in_a_reference_build(string notable, string build, string metric, string[] others)
    {
        Func<string[], Build> make = build switch
        {
            "whirl" => WhirlBerserker,
            "blood_whirl" => BloodWhirl,
            "crit_bleed" => CritBleedBerserker,
            "crusher" => ThunderCrusher,
            "precise" => PreciseThunderer,
            "axe" => AxeThunderer,
            _ => throw new ArgumentException(build),
        };
        var without = Metric(make(others), metric);
        var with = Metric(make(others.Append(notable).ToArray()), metric);
        Assert.True(with > without * 1.05, $"{notable} in {build} ({metric}): {without:0} → {with:0}");
    }

    private static (double FullFuryShare, double MinLifeShare) Track(Build b, double seconds = 60)
    {
        var sim = new Simulator(ContentHarness.Data, MechanicRegistry.Default);
        int steps = 0, full = 0;
        double minLife = 1;
        sim.StepObserver = s =>
        {
            steps++;
            if (s.Resource >= s.ResourceMax - 1e-6) full++;
            minLife = Math.Min(minLife, s.Life / s.LifeMax);
        };
        var r = sim.Run(b, new SimConfig("training", SimMode.Average, DurationSec: seconds));
        Assert.Empty(r.Violations);
        return ((double)full / steps, minLife);
    }

    /// <summary>
    /// The priority rotation spends Fury as soon as a spender is affordable, so a crush/whirlwind rotation never waits for 100
    /// Fury. «Безумие» comes from Вихрь ярости + Боевой транс (the free channel keeps 100 Fury once the trance lifts it above
    /// 50%), or from a long trance with «Клич крови» (Кровавый вихрь), or from builds without spenders.
    /// </summary>
    [Fact]
    public void Frenzy_is_reached_through_whirl_rage_and_battle_trance()
    {
        Assert.Equal(0, Track(WhirlBerserker()).FullFuryShare, 6);
        Assert.True(Track(WhirlBerserker("whirl_rage", "battle_trance")).FullFuryShare > 0.5);
        Assert.True(Track(CritBleedBerserker()).FullFuryShare > 0.5);
    }

    /// <summary>
    /// Неутолимый turns Fury cost into life: on «Кровавый вихрь» life regeneration does not cover the payments (life falls
    /// below half in 60 s), and the attacking boss kills the character sooner.
    /// </summary>
    [Fact]
    public void Insatiable_drains_life_on_blood_whirl_and_costs_survival()
    {
        var plain = Track(BloodWhirl("battle_trance"));
        var insatiable = Track(BloodWhirl("battle_trance", "insatiable"));
        Assert.Equal(1, plain.MinLifeShare, 6);
        Assert.True(insatiable.FullFuryShare > 0.3, $"frenzy share {insatiable.FullFuryShare:0.00}");
        Assert.True(insatiable.MinLifeShare is > 0 and < 0.5, $"min life {insatiable.MinLifeShare:0.00}");

        var ttdPlain = ContentHarness.Survival(BloodWhirl("battle_trance")).TimeToDeath ?? 180;
        var ttdInsatiable = ContentHarness.Survival(BloodWhirl("battle_trance", "insatiable")).TimeToDeath ?? 180;
        Assert.True(ttdInsatiable < ttdPlain * 0.8, $"time to death {ttdPlain:0.0} → {ttdInsatiable:0.0}");
    }

    /// <summary>
    /// Allowed steady state (spec 10.4: «пока хватает здоровья и регенерации»): Железный клич + Несгибаемость + Отрывистый рев
    /// + Неумолчный клич 5/5 and Экономное вращение 3/3 regenerate more life per cry cycle than Неутолимый takes for the
    /// Whirlwind channel, so the life-paid frenzy holds indefinitely. It stays well below the free channel of Вихрь ярости.
    /// </summary>
    [Fact]
    public void Insatiable_whirlwind_is_sustained_by_regeneration_on_iron_cry()
    {
        var b = Warrior50("berserker", "greataxe", "battle_trance", "insatiable");
        b.Slots.Add(Slot("battle_cry", ("iron_cry", 1), ("unyielding", 1), ("sharp_roar", 1), ("tireless", 5)));
        b.Slots.Add(Slot("whirlwind", ("steel_storm", 4), ("spin_up", 3), ("blade_hurricane", 1), ("thrifty_spin", 3), ("cutting_wind", 3)));
        b.Slots.Add(Slot("cleave", ("lacerate", 3), ("deep_cut", 2), ("thirst", 3), ("heavy_hand", 4)));
        var (full, minLife) = Track(b, 180);
        Assert.True(full > 0.9, $"frenzy share {full:0.00}");
        Assert.True(minLife > 0.9, $"min life {minLife:0.00}");
        Assert.True(ContentHarness.TrainingDps(b) < ContentHarness.TrainingDps(WhirlBerserker("whirl_rage", "battle_trance")));
    }

    /// <summary>
    /// Неутолимый with Crush in the Вихрь-берсерк rotation is dead in the sim: Crush first spends Fury before it reaches 100;
    /// Whirlwind first keeps the free channel and Crush is never picked. Depends on the owner question (rotation option
    /// «тратить только на полном ресурсе»).
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Insatiable_does_nothing_for_crush_in_the_whirl_berserker(bool crushFirst)
    {
        Build Make(params string[] n)
        {
            var b = WhirlBerserker(n);
            b.Slots.Insert(2, Slot("crush", ("brute_force", 4), ("torn_earth", 3), ("bloody_harvest", 3), ("heavy_blow", 3), ("peak_fury", 1), ("stun", 1)));
            b.Rotation = crushFirst ? new() { "battle_cry", "crush", "whirlwind", "cleave" } : new() { "battle_cry", "whirlwind", "crush", "cleave" };
            return b;
        }
        var without = ContentHarness.TrainingDps(Make("whirl_rage", "battle_trance"));
        var with = ContentHarness.TrainingDps(Make("whirl_rage", "battle_trance", "insatiable"));
        Assert.Equal(without, with, 6);
        Assert.Equal(1, Track(Make("whirl_rage", "battle_trance", "insatiable")).MinLifeShare, 6);
    }

    /// <summary>Triggered-only mini-skills (spec 8.4) are reached only through their procs: a build cannot slot them.
    /// BuildRules rejects them as skills outside the class list (no item grants them); there is no dedicated rule.</summary>
    [Fact]
    public void Triggered_only_skills_cannot_be_slotted()
    {
        var triggered = ContentHarness.Data.Skills.Values.Where(s => s.IsTriggeredOnly || s.Tags.Contains("triggered")).ToList();
        Assert.Contains(triggered, s => s.Id == "returning_axe");
        foreach (var skill in triggered)
        {
            var classId = skill.ClassId ?? "warrior";
            var b = new Build
            {
                ClassId = classId, Level = 50, WeaponId = classId == "warrior" ? "greataxe" : "staff",
                Slots = { new SkillSlot { SkillId = skill.Id, Level = 1 } },
            };
            Assert.Contains(BuildRules.Validate(b, ContentHarness.Data), v => v.Code == "skill_not_available");
        }
    }

    /// <summary>Громовой топор also reaches the fork «Возвращение»: the returning axe hits with lightning, not physical.</summary>
    [Fact]
    public void Thunder_axe_also_makes_the_returning_axe_lightning()
    {
        var b = Warrior50("thunderer", "sword", "thunder_axe");
        b.Slots.Add(Slot("throwing_axe", ("returning", 1)));
        var r = ContentHarness.Run(b, "training");
        Assert.Empty(r.Violations);
        Assert.True(r.DamageBySkill.GetValueOrDefault("returning_axe") > 0);
        Assert.Equal(0, r.DamageByType.GetValueOrDefault(DamageType.Physical), 6);
    }
}
