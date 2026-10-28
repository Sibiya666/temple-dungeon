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

    /// <summary>Numeric parameters a description states in other words, with the number the text shows instead.</summary>
    private static readonly Dictionary<(string Node, string Param), Func<double, double>> DescribedAs = new()
    {
        [("eternal_fire", "duration_mult")] = m => 4 * m, // «вдвое дольше (4 → 8 с)»: the base ignite lasts 4 s
        [("flesh_rend", "extra_stacks")] = _ => double.NaN, // «дополнительный стак»: one stack, said in words (NaN: not checked)
    };

    /// <summary>
    /// Fix round 2 (Task 10): every numeric parameter of an ascendancy node (mechanic params, modifier and behavior values) is
    /// quoted in its description, as itself or as a percent (0.3 → 30), so a balance change cannot leave a stale tooltip
    /// (Ледяная броня once said +550 with armor 250). Values the text says in words are mapped in <see cref="DescribedAs"/>.
    /// </summary>
    [Fact]
    public void Ascendancy_descriptions_quote_their_numbers()
    {
        var opts = new System.Text.Json.Nodes.JsonNodeOptions();
        var doc = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures.FixturePaths.RealData, "ascendancies.json")), opts,
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!.AsArray();
        var problems = new List<string>();
        foreach (var asc in doc)
        {
            var nodes = asc!["notables"]!.AsArray().Append(asc["start"]);
            foreach (var node in nodes)
            {
                var id = (string)node!["id"]!;
                var text = (string)node["description"]!;
                var quoted = System.Text.RegularExpressions.Regex.Matches(text, @"\d+(?:[.,]\d+)?")
                    .Select(m => double.Parse(m.Value.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture)).ToHashSet();
                foreach (var e in node["effects"]!.AsArray())
                {
                    var values = new List<(string Param, double Value)>();
                    if ((string)e!["type"]! == "mechanic" && e["params"] is System.Text.Json.Nodes.JsonObject ps)
                        values.AddRange(ps.Where(kv => kv.Value?.GetValueKind() == JsonValueKind.Number).Select(kv => (kv.Key, kv.Value!.GetValue<double>())));
                    else if (e["value"]?.GetValueKind() == JsonValueKind.Number)
                        values.Add(((string?)e["stat"] ?? (string?)e["param"] ?? "value", e["value"]!.GetValue<double>()));
                    foreach (var (param, value) in values)
                    {
                        var shown = DescribedAs.TryGetValue((id, param), out var f) ? f(value) : Math.Abs(value);
                        if (!double.IsNaN(shown) && !quoted.Contains(shown) && !quoted.Contains(Math.Round(shown * 100, 6)))
                            problems.Add($"{id}: {param} = {value} not in «{text}»");
                    }
                }
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Theory]
    [InlineData("berserker", "warrior")]
    [InlineData("thunderer", "warrior")]
    [InlineData("pyromancer", "sorcerer")]
    [InlineData("cryomancer", "sorcerer")]
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
    /// maximum-resistance cap (Буря в жилах, written so in spec 10.4) — and freeze chance/duration: freeze is crowd control (the
    /// target skips its attacks, spec 6.7), not damage (Северное сияние, written so in spec 10.4).
    /// </summary>
    private static bool ChangesRule(Effect e) => e switch
    {
        MechanicEffect or AddConversionEffect or SetBehaviorEffect or AddBehaviorEffect or SetDamageTypeEffect
            or AddTagEffect or RemoveTagEffect or SetResourceEffect or GrantProcEffect => true,
        ModifierEffect { Modifier: var m } => m.Conditions.Count > 0 || m.Per != null
            || m.Stat is "freeze_chance" or "freeze_duration"
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

    [Fact]
    public void Pyromancer_nodes_match_spec()
    {
        var asc = Asc("pyromancer");
        var stacks = Assert.Single(asc.Start.Effects.OfType<ModifierEffect>()).Modifier;
        Assert.Equal(("ignite_max_stacks", ModKind.Flat, 2.0), (stacks.Stat, stacks.Kind, stacks.Value));
        Assert.Equal(TagSet.Of("spell"), stacks.RequiredTags);

        Mech(Notable("pyromancer", "spread_ignite"), "spread_ignite");
        Mech(Notable("pyromancer", "ashes"), "ashes");
        Mech(Notable("pyromancer", "eternal_fire"), "eternal_fire");

        // Метеоритный дождь: the meteor itself at −50% (More) plus two copies on use — three falls at half power.
        var rain = Notable("pyromancer", "meteor_rain");
        Assert.All(rain.Effects, e => Assert.Equal(new Scope.Skill("meteor"), e.Scope));
        var less = Assert.Single(rain.Effects.OfType<ModifierEffect>()).Modifier;
        Assert.Equal(("damage", ModKind.More, -50.0), (less.Stat, less.Kind, less.Value));
        var copies = rain.Effects.OfType<GrantProcEffect>().ToList();
        Assert.Equal(2, copies.Count);
        Assert.All(copies, p =>
        {
            Assert.IsType<Trigger.OnUse>(p.Trigger);
            // A copy inherits the meteor's own −50%, so damage_percent 100 makes each copy a 50% fall (BALANCE_NOTES).
            var cast = Assert.IsType<ProcAction.CastSkill>(p.Action);
            Assert.Equal(("meteor", 100.0), (cast.SkillId, cast.DamagePercent));
        });

        Assert.IsType<Scope.Global>(Mech(Notable("pyromancer", "fire_armor"), "fire_armor").Scope);
        var soul = Mech(Notable("pyromancer", "soul_heat"), "soul_heat");
        Assert.Equal(new[] { "fireball", "meteor" }, soul.Params["skills"].EnumerateArray().Select(v => v.GetString()).ToArray());
    }

    [Fact]
    public void Cryomancer_nodes_match_spec()
    {
        var asc = Asc("cryomancer");
        Assert.Equal(25, Mech(asc.Start, "chill_slow").Params["slow"].GetDouble());
        var cold = Assert.Single(asc.Start.Effects.OfType<ModifierEffect>()).Modifier;
        // Spec 10.4 «увеличивает урон холодом по цели на 10%»: More 20 since Task 10 (was Increased 10; BALANCE_NOTES).
        Assert.Equal(("damage", ModKind.More, 20.0), (cold.Stat, cold.Kind, cold.Value));
        Assert.Equal(TagSet.Of("cold"), cold.RequiredTags);
        Assert.Equal("target_chilled", Assert.Single(cold.Conditions).Id);

        Assert.IsType<Scope.Global>(Mech(Notable("cryomancer", "shatter"), "shatter").Scope);
        Mech(Notable("cryomancer", "ice_armor"), "ice_armor");

        var orb = Notable("cryomancer", "frost_orb_asc");
        var grant = Mech(orb, "grant_skill_node");
        Assert.Equal(("fireball", "frost_orb"), (Param(grant, "skill"), Param(grant, "node")));
        Assert.Equal("fireball", Param(Mech(orb, "ailment_swap"), "skill"));

        Assert.IsType<Scope.Global>(Mech(Notable("cryomancer", "brittle"), "brittle").Scope);

        var aurora = Notable("cryomancer", "aurora").Effects.OfType<ModifierEffect>().Select(e => e.Modifier).ToList();
        Assert.Contains(aurora, m => m is { Stat: "freeze_chance", Kind: ModKind.Flat, Value: 10 } && m.RequiredTags.Equals(TagSet.Of("spell")));
        Assert.Contains(aurora, m => m is { Stat: "freeze_duration", Kind: ModKind.Increased, Value: 50 });

        var glacier = Notable("cryomancer", "glacier");
        Assert.All(glacier.Effects, e => Assert.Equal(new Scope.Skill("ice_lance"), e.Scope));
        var beh = glacier.Effects.OfType<SetBehaviorEffect>().ToDictionary(e => e.Param, e => e.Value);
        Assert.Equal(3, beh["ground_duration"]);
        Assert.Equal(0.3, beh["ground_fraction"]); // spec 15%, doubled in Task 10 (BALANCE_NOTES)
    }

    // ---- Temporary level-50 builds (until reference builds exist) ----

    /// <summary>The first <paramref name="count"/> warrior passives in breadth-first order from the class start, keystones
    /// excluded; attribute nodes take Strength. Breadth-first order keeps every prefix connected to the start.</summary>
    private static (List<string> Nodes, Dictionary<string, Attr> Choices) WarriorPassives(int count) => ClassPassives("warrior", count, Attr.Str);

    /// <summary>The first <paramref name="count"/> passives of the class in breadth-first order from its start, keystones excluded;
    /// attribute nodes take <paramref name="attribute"/>.</summary>
    private static (List<string> Nodes, Dictionary<string, Attr> Choices) ClassPassives(string classId, int count, Attr attribute)
    {
        var tree = ContentHarness.Data.Passives;
        var start = ContentHarness.Data.Classes[classId].PassiveStartNodeId;
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
        var choices = nodes.Where(n => tree.Nodes[n].Kind == PassiveKind.Attribute).ToDictionary(n => n, _ => attribute);
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

    private static Build Sorcerer50(string? ascendancy, params string[] notables)
    {
        var (nodes, choices) = ClassPassives("sorcerer", 49, Attr.Int);
        return new Build
        {
            ClassId = "sorcerer", Level = 50, WeaponId = "staff",
            AscendancyId = ascendancy, AscendancyNodes = notables.ToList(),
            PassiveNodes = nodes, AttributeChoices = choices,
        };
    }

    /// <summary>The Spark of the sorcerer builds: a mana-oriented generator.</summary>
    private static SkillSlot ManaSpark() => Slot("spark", ("crackle", 4), ("mana_siphon", 1), ("mana_current", 3), ("quick_spark", 3),
        ("bright_flash", 3), ("dazzle", 1), ("static", 3), ("breakdown", 2));

    /// <summary>Огненный шар на поджогах: стаки поджога, урон поджога, горящая земля и три шара.</summary>
    private static Build PyroFireball(params string[] notables)
    {
        var b = Sorcerer50("pyromancer", notables);
        b.Slots.Add(Slot("fireball", ("heat", 4), ("molten_core", 3), ("flame_mark", 1), ("eternal_flame", 1), ("soul_flame", 2),
            ("lingering_heat", 3), ("wide_blast", 3), ("ring_of_fire", 1), ("scatter", 1), ("mana_focus", 1)));
        b.Slots.Add(ManaSpark());
        return b;
    }

    /// <summary>Огненный шар на поджогах без Искры: ману дает только «Жар души» (и регенерация).</summary>
    private static Build PyroFireballSolo(params string[] notables)
    {
        var b = PyroFireball(notables);
        b.Slots.RemoveAll(s => s.SkillId == "spark");
        return b;
    }

    /// <summary>Сжатый Огненный шар на поджогах: одна цель (плюс пробитие), поэтому на паке поджог достается не всем.</summary>
    private static Build PyroCompress(params string[] notables)
    {
        var b = Sorcerer50("pyromancer", notables);
        b.Slots.Add(Slot("fireball", ("heat", 4), ("molten_core", 3), ("flame_mark", 1), ("eternal_flame", 1), ("soul_flame", 2),
            ("lingering_heat", 3), ("compress", 1), ("mana_focus", 3), ("swift_orb", 2)));
        b.Slots.Add(ManaSpark());
        return b;
    }

    /// <summary>Метеор на попаданиях: плотное ядро кометы, криты с осколком кометы, без огненного поля.</summary>
    private static Build PyroMeteor(params string[] notables)
    {
        var b = Sorcerer50("pyromancer", notables);
        b.Slots.Add(Slot("meteor", ("sky_fire", 4), ("comet_core", 1), ("falling_star", 3), ("comet_fragment", 1), ("crater", 3),
            ("star_thrift", 3), ("instant_fall", 1), ("kindling", 3), ("ash_veil", 1)));
        b.Slots.Add(ManaSpark());
        return b;
    }

    /// <summary>Ледяное копье на заморозке и критах.</summary>
    private static Build CryoLance(params string[] notables)
    {
        var b = Sorcerer50("cryomancer", notables);
        b.Slots.Add(Slot("ice_lance", ("frost_edge", 4), ("binding_lance", 1), ("bitter_cold", 3), ("deep_freeze", 3), ("ice_shards", 1),
            ("point", 3), ("ice_crown", 1), ("cold_precision", 2), ("cold_poise", 2)));
        b.Slots.Add(ManaSpark());
        return b;
    }

    /// <summary>Огненный шар Криоманта: дерево поджога (шанс поджога), крит и три шара; «Морозный шар» делает его холодным.</summary>
    private static Build CryoFireball(params string[] notables)
    {
        var b = Sorcerer50("cryomancer", notables);
        b.Slots.Add(Slot("fireball", ("heat", 4), ("molten_core", 3), ("overheat", 3), ("blast_wave", 1), ("wide_blast", 3), ("scatter", 1),
            ("mana_focus", 3), ("swift_orb", 2)));
        b.Slots.Add(ManaSpark());
        return b;
    }

    [Fact]
    public void Pyromancer_build_compiles_and_simulates()
    {
        var b = PyroFireball("spread_ignite", "ashes", "eternal_fire", "soul_heat");
        Assert.Empty(BuildRules.Validate(b, ContentHarness.Data));
        var r = ContentHarness.Run(b, "swarm");
        Assert.Empty(r.Violations);
        Assert.True(r.DpsPack > 0);
        Assert.True(r.DotDamage > 0);
    }

    [Fact]
    public void Cryomancer_build_compiles_and_simulates()
    {
        var b = CryoLance("shatter", "brittle", "aurora", "glacier");
        Assert.Empty(BuildRules.Validate(b, ContentHarness.Data));
        var r = ContentHarness.Run(b, "training");
        Assert.Empty(r.Violations);
        Assert.True(r.DpsSingleTarget > 0);
        Assert.True(r.DamageByType.GetValueOrDefault(DamageType.Cold) > 0);
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
        "taken" => 1 / DamageTakenInWindow(b, TakenWindowSec),
        "training_pack" => ContentHarness.Run(b, "training", 5).DpsPack,
        _ => throw new ArgumentException(metric),
    };

    /// <summary>
    /// Window of the «taken» metric: 20 s, the lower end of the survival corridor of spec 11.1 (45 s before Task 10 calibrated
    /// «Атакующий босс»; a calibrated build dies within 20–40 s, so a longer window would measure a dead character).
    /// </summary>
    private const double TakenWindowSec = 20;

    /// <summary>
    /// Damage the character takes from «Атакующий босс» in the first <paramref name="seconds"/> of the fight (it must survive them).
    /// A fixed window, unlike time to death (which moves in whole boss attacks of 3.5 s): armor and resistances count continuously;
    /// a freeze counts as the attacks it makes the boss skip.
    /// </summary>
    private static double DamageTakenInWindow(Build b, double seconds)
    {
        var r = new Simulator(ContentHarness.Data, MechanicRegistry.Default)
            .Run(b, new SimConfig("attacking_boss", SimMode.Average, DummyAttacks: true, SurvivalCapSec: seconds));
        Assert.Empty(r.Violations);
        Assert.True(r.TimeToDeath is null || r.TimeToDeath >= seconds - 1e-9, $"died at {r.TimeToDeath} s");
        var taken = r.DamageTaken?.Values.Sum() ?? 0;
        Assert.True(taken > 0);
        return taken;
    }

    /// <summary>Each notable is worth its 2 points in at least one reference-style build: taking it (on top of the build's
    /// other picks) raises that build's metric by more than 5%. Metrics: DPS on «Тренировочный» (training), on «Босс» (boss), on
    /// a pack of five immortal training dummies (training_pack: a long fight against a group, where ignites pile up), pack DPS on
    /// «Рой» (swarm), and the inverse of the damage taken from «Атакующий босс» in a fixed <see cref="TakenWindowSec"/> window (taken). Огненная броня
    /// joined the list in Task 10 (it was a tripwire below the bar).</summary>
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
    [InlineData("spread_ignite", "pyro_compress", "swarm", new string[0])]
    [InlineData("ashes", "pyro_compress", "training_pack", new[] { "spread_ignite" })]
    [InlineData("eternal_fire", "pyro_fireball", "training", new string[0])]
    [InlineData("meteor_rain", "pyro_meteor", "training", new string[0])]
    [InlineData("soul_heat", "pyro_fireball_solo", "swarm", new string[0])]
    [InlineData("fire_armor", "pyro_fireball", "taken", new string[0])]
    [InlineData("shatter", "cryo_lance", "training", new string[0])]
    [InlineData("ice_armor", "cryo_lance", "taken", new string[0])]
    [InlineData("frost_orb_asc", "cryo_fireball", "training", new[] { "shatter", "brittle" })]
    [InlineData("brittle", "cryo_lance", "training", new string[0])]
    [InlineData("aurora", "cryo_lance", "taken", new string[0])]
    [InlineData("glacier", "cryo_lance", "training", new string[0])]
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
            "pyro_fireball" => PyroFireball,
            "pyro_fireball_solo" => PyroFireballSolo,
            "pyro_compress" => PyroCompress,
            "pyro_meteor" => PyroMeteor,
            "cryo_lance" => CryoLance,
            "cryo_fireball" => CryoFireball,
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

    private static double SkillStat(CompiledCharacter ch, string skill, string stat)
    {
        var s = ch.Skills.Single(x => x.SkillId == skill);
        return ch.Evaluator.WithExtra(s.Modifiers).Evaluate(stat, s.Tags).Value;
    }

    private static CompiledCharacter Compile(Build b)
    {
        Assert.Empty(BuildRules.Validate(b, ContentHarness.Data));
        return new CharacterCompiler(ContentHarness.Data, MechanicRegistry.Default).Compile(b);
    }

    /// <summary>
    /// Морозный шар takes fork B of the fireball (spec 8.4, conflicts): a paid «Грозовой шар» is switched off and its point
    /// returns; the fireball deals cold damage instead of lightning.
    /// </summary>
    [Fact]
    public void Cryomancer_frost_orb_takes_fork_b()
    {
        Build Make(params string[] notables)
        {
            var b = Sorcerer50("cryomancer", notables);
            b.Slots.Add(Slot("fireball", ("heat", 2), ("storm_orb", 1)));
            return b;
        }
        var plain = Compile(Make()).Skills.Single();
        Assert.Equal(1, plain.EffectiveNodePoints["storm_orb"]);
        Assert.Equal(DamageType.Lightning, plain.DamageTypeOverride);

        var fb = Compile(Make("frost_orb_asc")).Skills.Single();
        Assert.Equal(1, fb.EffectiveNodePoints["frost_orb"]);
        Assert.False(fb.EffectiveNodePoints.ContainsKey("storm_orb"));
        Assert.Equal(plain.FreePoints + 1, fb.FreePoints);
        Assert.Equal(DamageType.Cold, fb.DamageTypeOverride);

        var r = ContentHarness.Run(Make("frost_orb_asc"), "training");
        Assert.Empty(r.Violations);
        Assert.True(r.DamageByType.GetValueOrDefault(DamageType.Cold) > 0);
        Assert.Equal(0, r.DamageByType.GetValueOrDefault(DamageType.Lightning), 6);
        Assert.Equal(0, r.DamageByType.GetValueOrDefault(DamageType.Fire), 6);
    }

    /// <summary>
    /// Морозный шар, part 2: the fireball's own ignite chance from its tree («Раскаленное ядро» 3/3, +30) becomes freeze chance;
    /// ignite chance from the character pool (passives) stays ignite chance and does nothing for a cold orb.
    /// </summary>
    [Fact]
    public void Frost_orb_moves_the_fireball_tree_ignite_chance_to_freeze()
    {
        var plain = Compile(CryoFireball());
        var orb = Compile(CryoFireball("frost_orb_asc"));
        Assert.Equal(SkillStat(plain, "fireball", "freeze_chance") + 10 + 30, SkillStat(orb, "fireball", "freeze_chance"), 6);
        Assert.Equal(SkillStat(plain, "fireball", "ignite_chance") - 30, SkillStat(orb, "fireball", "ignite_chance"), 6);
    }

    /// <summary>Метеоритный дождь: the meteor and its two copies each hit at 50% — 150% of the plain meteor's hit damage per cast.</summary>
    [Fact]
    public void Meteor_rain_drops_three_meteors_at_half_power()
    {
        Build Make(params string[] notables)
        {
            var b = Sorcerer50("pyromancer", notables);
            b.Slots.Add(Slot("meteor"));
            return b;
        }
        var plain = ContentHarness.Run(Make(), "training");
        var rain = ContentHarness.Run(Make("meteor_rain"), "training");
        Assert.Empty(rain.Violations);
        Assert.Equal(1.5, rain.HitDamage / plain.HitDamage, 2);
    }

    /// <summary>Огненная броня needs burning ground from a slotted skill: Meteor without «Огненное поле» gets nothing.</summary>
    [Fact]
    public void Fire_armor_needs_burning_ground()
    {
        Build Make(bool field, params string[] notables)
        {
            var b = Sorcerer50("pyromancer", notables);
            b.Slots.Add(field ? Slot("meteor", ("fire_field", 1)) : Slot("meteor", ("crater", 1)));
            return b;
        }
        double Armor(Build b) => Compile(b).Evaluator.Evaluate("armor", default).Value;
        Assert.Equal(Armor(Make(false)), Armor(Make(false, "fire_armor")), 6);
        Assert.True(Armor(Make(true, "fire_armor")) > Armor(Make(true)) * 1.1);
    }

    /// <summary>
    /// Spec 10.4 example: a Pyromancer Fireball with «Вечное пламя» and «Пламя души» 2/2 has 1 + 2 + 2 + 2 = 7 ignite stacks
    /// (no passives, so nothing else adds stacks), and the sim really holds 7 on a target — never more. Holding them takes a
    /// pack: «Распространение» stacks copies of each use's ignite on the other targets, «Вечный огонь» keeps them 6 s.
    /// </summary>
    [Fact]
    public void Pyromancer_fireball_holds_seven_ignite_stacks()
    {
        var b = Sorcerer50("pyromancer", "eternal_fire", "spread_ignite");
        b.PassiveNodes.Clear();
        b.AttributeChoices.Clear();
        b.Slots.Add(Slot("fireball", ("heat", 4), ("molten_core", 3), ("eternal_flame", 1), ("flame_mark", 1), ("soul_flame", 2),
            ("lingering_heat", 3), ("mana_focus", 3), ("swift_orb", 2)));
        b.Slots.Add(ManaSpark());
        var ch = Compile(b);
        var fb = ch.Skills.Single(x => x.SkillId == "fireball");
        Assert.Equal(7, ch.Evaluator.WithExtra(fb.Modifiers).Evaluate("ignite_max_stacks", fb.Tags, null, 1).Value);

        var sim = new Simulator(ContentHarness.Data, MechanicRegistry.Default);
        var held = 0;
        sim.StepObserver = s => held = Math.Max(held, s.Targets.Max(t => t.Stacks("ignite")));
        Assert.Empty(sim.Run(b, new SimConfig("training", SimMode.Average, PackSizeOverride: 5)).Violations);
        Assert.Equal(7, held);
    }

    /// <summary>
    /// Хрупкость scales with the crit share of the damage: chill lands with the first lance hit, so every later lance crit gets
    /// the notable's More (+45% since Task 10, was +15%). The lance's damage grows by more × c·m / (1 − c + c·m) (crit chance c,
    /// crit multiplier m); both reference lances share the same crit nodes, hence the same gain.
    /// </summary>
    [Fact]
    public void Brittle_gain_follows_the_crit_share()
    {
        var ch = Compile(CryoLance());
        var lance = ch.Skills.Single(x => x.SkillId == "ice_lance");
        var ev = ch.Evaluator.WithExtra(lance.Modifiers);
        var c = ev.Evaluate("crit_chance", lance.Tags, null, lance.Base.CritChance).Value / 100;
        var m = 1 + ev.Evaluate("crit_damage", lance.Tags).Value / 100;
        var more = Mech(Notable("cryomancer", "brittle"), "brittle").Params["more"].GetDouble() / 100; // 45 since Task 10 (BALANCE_NOTES)
        var expected = 1 + more * c * m / (1 - c + c * m);
        var plain = ContentHarness.Run(CryoLance(), "training").DamageBySkill["ice_lance"];
        var brittle = ContentHarness.Run(CryoLance("brittle"), "training").DamageBySkill["ice_lance"];
        Assert.Equal(expected, brittle / plain, 2);
    }
}
