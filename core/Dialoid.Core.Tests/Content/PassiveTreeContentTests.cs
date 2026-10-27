using Dialoid.Core.Character;
using Dialoid.Core.Effects;
using Dialoid.Core.Passives;
using Dialoid.Core.Stats;
using Attr = Dialoid.Core.Character.Attribute;

namespace Dialoid.Core.Tests.Content;

/// <summary>Spec 7 (shared passive tree) and 10.1–10.2 (class frames) over the real data.</summary>
public class PassiveTreeContentTests
{
    private static PassiveTree Tree => ContentHarness.Data.Passives;

    private static readonly Dictionary<string, string> KeystoneMechanics = new()
    {
        ["keystone_blood_pact"] = "blood_pact",
        ["keystone_titan_will"] = "titan_will",
        ["keystone_inevitability"] = "inevitability",
        ["keystone_avatar_of_fire"] = "avatar_of_fire",
        ["keystone_blade_dance"] = "blade_dance",
        ["keystone_stone_skin"] = "stone_skin",
    };

    private static readonly string[] Regions = { "str", "int", "will", "dex" };

    private static IEnumerable<PassiveNode> OfKind(PassiveKind k) => Tree.Nodes.Values.Where(n => n.Kind == k);

    /// <summary>Shortest edge counts from <paramref name="from"/>; other start nodes are not walked through (they cannot be allocated).</summary>
    private static Dictionary<string, int> Distances(string from)
    {
        var dist = new Dictionary<string, int> { [from] = 0 };
        var queue = new Queue<string>();
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            foreach (var next in Tree.Edges[cur])
            {
                if (dist.ContainsKey(next) || !Tree.Nodes.ContainsKey(next)) continue;
                dist[next] = dist[cur] + 1;
                if (Tree.Nodes[next].Kind != PassiveKind.Start) queue.Enqueue(next);
            }
        }
        return dist;
    }

    [Fact]
    public void Node_count_between_140_and_200()
    {
        Assert.InRange(Tree.Nodes.Count, 140, 200);
    }

    [Fact]
    public void Kind_shares_match_spec()
    {
        double total = Tree.Nodes.Count;
        double Share(PassiveKind k) => OfKind(k).Count() / total * 100;
        Assert.InRange(Share(PassiveKind.Small), 55, 65);
        Assert.InRange(Share(PassiveKind.Attribute), 10, 20);
        Assert.InRange(Share(PassiveKind.Notable), 15, 25);
        Assert.Equal(6, OfKind(PassiveKind.Keystone).Count());
        Assert.Equal(2, OfKind(PassiveKind.Start).Count());
    }

    [Fact]
    public void Non_start_nodes_belong_to_the_four_attribute_regions()
    {
        foreach (var n in Tree.Nodes.Values.Where(n => n.Kind != PassiveKind.Start))
            Assert.True(Regions.Contains(n.Region), $"{n.Id}: region «{n.Region}»");
        foreach (var r in Regions)
            Assert.True(Tree.Nodes.Values.Count(n => n.Region == r) >= 30, $"region {r} is too small");
    }

    [Fact]
    public void Attribute_nodes_have_no_effects_and_others_do()
    {
        foreach (var n in Tree.Nodes.Values)
        {
            if (n.Kind is PassiveKind.Attribute or PassiveKind.Start) Assert.Empty(n.Effects);
            else Assert.NotEmpty(n.Effects);
        }
        foreach (var n in OfKind(PassiveKind.Small)) Assert.InRange(n.Effects.Count, 1, 2);
        foreach (var n in OfKind(PassiveKind.Notable)) Assert.InRange(n.Effects.Count, 2, 3);
    }

    [Fact]
    public void Six_keystones_each_one_mechanic_effect()
    {
        var keystones = OfKind(PassiveKind.Keystone).ToList();
        Assert.Equal(KeystoneMechanics.Keys.OrderBy(x => x), keystones.Select(k => k.Id).OrderBy(x => x));
        foreach (var k in keystones)
        {
            var e = Assert.IsType<MechanicEffect>(Assert.Single(k.Effects));
            Assert.Equal(KeystoneMechanics[k.Id], e.MechanicId);
            Assert.IsType<Scope.Global>(e.Scope);
        }
    }

    [Fact]
    public void Every_keystone_is_a_leaf()
    {
        foreach (var k in OfKind(PassiveKind.Keystone))
            Assert.True(Tree.Edges[k.Id].Count == 1, $"{k.Id} has {Tree.Edges[k.Id].Count} links");
    }

    [Fact]
    public void Starts_sit_between_their_regions()
    {
        void Check(string startId, string classId, params string[] regions)
        {
            var start = Tree.Nodes[startId];
            Assert.Equal(PassiveKind.Start, start.Kind);
            Assert.Equal(classId, start.StartForClassId);
            var neighbourRegions = Tree.Edges[startId].Select(id => Tree.Nodes[id].Region).Distinct().OrderBy(x => x);
            Assert.Equal(regions.OrderBy(x => x), neighbourRegions);
        }
        Check("start_warrior", "warrior", "str", "will");
        Check("start_sorcerer", "sorcerer", "int", "will");
    }

    [Fact]
    public void Dex_region_reachable_from_both_starts()
    {
        foreach (var start in new[] { "start_warrior", "start_sorcerer" })
        {
            var dist = Distances(start);
            var dexNodes = Tree.Nodes.Values.Where(n => n.Region == "dex").ToList();
            Assert.NotEmpty(dexNodes);
            Assert.All(dexNodes, n => Assert.True(dist.ContainsKey(n.Id), $"{n.Id} unreachable from {start}"));
        }
    }

    [Fact]
    public void Keystones_are_at_least_8_steps_from_any_start()
    {
        var fromStarts = OfKind(PassiveKind.Start).Select(s => Distances(s.Id)).ToList();
        foreach (var k in OfKind(PassiveKind.Keystone))
        {
            var nearest = fromStarts.Min(d => d[k.Id]);
            Assert.True(nearest >= 8, $"{k.Id} is {nearest} steps from the nearest start");
        }
    }

    [Fact]
    public void Every_notable_has_two_to_four_small_neighbours_of_its_region()
    {
        foreach (var n in OfKind(PassiveKind.Notable))
        {
            var smalls = Tree.Edges[n.Id].Select(id => Tree.Nodes[id])
                .Count(m => m.Kind == PassiveKind.Small && m.Region == n.Region);
            Assert.True(smalls is >= 2 and <= 4, $"{n.Id} has {smalls} small neighbours of region {n.Region}");
        }
    }

    [Fact]
    public void No_two_nodes_share_coordinates()
    {
        var nodes = Tree.Nodes.Values.ToList();
        for (int i = 0; i < nodes.Count; i++)
            for (int j = i + 1; j < nodes.Count; j++)
            {
                var d = Math.Sqrt(Math.Pow(nodes[i].X - nodes[j].X, 2) + Math.Pow(nodes[i].Y - nodes[j].Y, 2));
                Assert.True(d >= 1, $"{nodes[i].Id} and {nodes[j].Id} are {d:0.##} apart");
            }
    }

    /// <summary>Names of skills and skill/ascendancy tree nodes from spec 10.1–10.5; passive nodes must not reuse them.</summary>
    private static readonly string[] ReservedNames =
    {
        // skills (10.1, 10.2, 10.5)
        "Рассекающий удар", "Сокрушение", "Боевой клич", "Вихрь", "Рывок", "Метательный топор",
        "Искра", "Огненный шар", "Ледяное копье", "Шаровая молния", "Метеор", "Мерцание", "Ядовитый туман",
        // Fireball tree (10.3)
        "Жар", "Широкий взрыв", "Раскаленное ядро", "Вечное пламя", "Остаточный жар", "Пламенное кольцо", "Разделение",
        "Сжатие", "Морозный шар", "Грозовой шар", "Концентрация маны", "Отдача", "Летучий шар", "Метка пламени",
        "Перегрев", "Взрывная волна", "Пламя души",
        // Cleave tree (10.3)
        "Тяжелая рука", "Рваные раны", "Глубокий порез", "Кровопускание", "Жажда", "Неистовство", "Широкий замах",
        "Раскол", "Точный удар", "Раскаленный клинок", "Пылающие раны", "Удар грома", "Эхо", "Стальная кожа",
        "Сокрушительная сила", "Кромка",
        // named nodes of the other skill trees (10.3)
        "Кровавая жатва", "Устрашение", "Перегрузка", "Ледяная глыба", "Солнечный шар",
        // ascendancies (10.4)
        "Кровавое безумие", "Неутолимый", "Кровавый пир", "Без оглядки", "Вихрь ярости", "Рассечение плоти", "Боевой транс",
        "Грозовое оружие", "Разряд", "Цепной удар", "Статический заряд", "Громовой топор", "Заземление", "Буря в жилах",
        "Пожар", "Распространение", "Пепел", "Вечный огонь", "Метеоритный дождь", "Огненная броня", "Жар души",
        "Вечная мерзлота", "Ледяная броня", "Хрупкость", "Северное сияние", "Ледник",
    };

    [Fact]
    public void Passive_names_do_not_reuse_skill_or_ascendancy_node_names()
    {
        var reserved = new HashSet<string>(ReservedNames, StringComparer.OrdinalIgnoreCase);
        var clashes = Tree.Nodes.Values.Where(n => reserved.Contains(n.Name)).Select(n => $"{n.Id} «{n.Name}»").ToList();
        Assert.True(clashes.Count == 0, string.Join(", ", clashes));
    }

    private static (double Chance, double Damage) CritOf(PassiveNode n, TagSet context)
    {
        double c = 0, d = 0;
        foreach (var e in n.Effects)
        {
            if (e is not ModifierEffect { Scope: Scope.Global } me) continue;
            var m = me.Modifier;
            if (m.Kind != ModKind.Flat || m.Conditions.Count != 0 || m.Per != null) continue;
            if (!m.RequiredTags.Items.All(context.Contains)) continue;
            if (m.Stat == "crit_chance") c += m.Value;
            else if (m.Stat == "crit_damage") d += m.Value;
        }
        return (c, d);
    }

    /// <summary>
    /// Greedy lower bound on the crit (chance, damage) the tree gives a skill with <paramref name="context"/> tags when
    /// at most <paramref name="budget"/> points are spent from <paramref name="startId"/>: each step takes the shortest
    /// path to the node whose path best raises c·d per point. Unconditional flat modifiers only.
    /// </summary>
    private static (double Chance, double Damage) BestCrit(string startId, TagSet context, double baseChance, double baseDamage, int budget)
    {
        var taken = new HashSet<string> { startId };
        double c = baseChance, d = baseDamage;
        int spent = 0;
        while (true)
        {
            var parent = new Dictionary<string, string?>();
            var queue = new Queue<string>();
            foreach (var t in taken) { parent[t] = null; queue.Enqueue(t); }
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                foreach (var next in Tree.Edges[cur])
                    if (!parent.ContainsKey(next) && Tree.Nodes[next].Kind != PassiveKind.Start)
                    {
                        parent[next] = cur;
                        queue.Enqueue(next);
                    }
            }
            double bestScore = 0;
            List<string>? bestPath = null;
            (double C, double D) bestTotals = default;
            foreach (var target in parent.Keys.Where(k => !taken.Contains(k)))
            {
                var path = new List<string>();
                for (var at = target; at != null && !taken.Contains(at); at = parent[at]) path.Add(at);
                if (spent + path.Count > budget) continue;
                double nc = c, nd = d;
                foreach (var id in path)
                {
                    var (dc, dd) = CritOf(Tree.Nodes[id], context);
                    nc += dc; nd += dd;
                }
                var score = (nc * nd - c * d) / path.Count;
                if (score > bestScore) { bestScore = score; bestPath = path; bestTotals = (nc, nd); }
            }
            if (bestPath == null) return (c, d);
            foreach (var id in bestPath) taken.Add(id);
            spent += bestPath.Count;
            (c, d) = bestTotals;
        }
    }

    /// <summary>
    /// Spec 7.4: a crit build must be able to beat Inevitability (+18% More on hits). Expected crit multiplier is
    /// 1 + c·d (c = crit chance, d = crit_damage as fractions; base crit_damage 50 from stats.json).
    /// Base crit chance 5 (weapon for attacks, skill for spells). Class dex bonus at L60: 0.04 × (base dex + 59) for the
    /// class's own hit kind — Warrior 0.04 × 69 = 2.76 for attacks, Sorcerer 0.04 × 67 = 2.68 for spells.
    /// Contexts: a fire-converted Cleave (attack, melee, area, physical, fire, hit) and Fireball (spell, projectile, area, fire, hit).
    /// Within 12 points the tree alone must push 1 + c·d above 1.18 (20 before Task 10 raised the Strength crit cluster: Warrior
    /// crit cost ~20 points against the Sorcerer's ~11; both now need 10).
    /// </summary>
    [Theory]
    [InlineData("warrior", "attack,melee,area,physical,fire,hit")]
    [InlineData("sorcerer", "spell,projectile,area,fire,hit")]
    public void Crit_builds_beat_inevitability_within_12_points(string classId, string tags)
    {
        var cls = ContentHarness.Data.Classes[classId];
        var context = TagSet.Of(tags.Split(','));
        var dexBonus = cls.ClassBonuses
            .Where(m => m.Stat == "crit_chance" && m.Per?.Stat == "dex" && m.RequiredTags.Items.All(context.Contains))
            .Sum(m => m.Value * (cls.BaseAttributes[Attr.Dex] + 59) / m.Per!.Every);
        Assert.True(dexBonus > 2.5, $"class dex crit bonus {dexBonus}");
        var baseDamage = ContentHarness.Data.Stats["crit_damage"].Default;

        var (c, d) = BestCrit(cls.PassiveStartNodeId, context, 5 + dexBonus, baseDamage, 12);
        var gain = 1 + c / 100 * d / 100;
        Assert.True(gain > 1.18, $"{classId}: crit {c:0.##}%, crit damage {d:0.##}% → ×{gain:0.###}");
    }

    [Fact]
    public void Warrior_reaches_an_attack_crit_notable_within_8_points()
    {
        var dist = Distances("start_warrior");
        var attack = TagSet.Of("attack", "melee", "physical");
        Assert.Contains(OfKind(PassiveKind.Notable), n => dist[n.Id] <= 8 && CritOf(n, attack).Chance >= 4 && CritOf(n, attack).Damage >= 20);
    }

    [Fact]
    public void Classes_match_spec_10()
    {
        var classes = ContentHarness.Data.Classes;
        Assert.Equal(new[] { "sorcerer", "warrior" }, classes.Keys.OrderBy(x => x));

        void Bonus(ClassDefinition c, string stat, ModKind kind, double value, Attr per, string? tag)
        {
            Assert.Contains(c.ClassBonuses, m => m.Stat == stat && m.Kind == kind && m.Value == value
                && m.Per != null && m.Per.Stat == per.ToString().ToLowerInvariant() && m.Per.Every == 1 && m.Per.Cap == null
                && m.Conditions.Count == 0
                && (tag == null ? m.RequiredTags.Items.Count == 0 : m.RequiredTags.Items.Count == 1 && m.RequiredTags.Contains(tag)));
        }

        var w = classes["warrior"];
        Assert.Equal(Attr.Str, w.Primary);
        Assert.Equal((15, 7, 10, 10), (w.BaseAttributes[Attr.Str], w.BaseAttributes[Attr.Int], w.BaseAttributes[Attr.Will], w.BaseAttributes[Attr.Dex]));
        Assert.Equal(1.2, w.LifeMultiplier);
        Assert.Equal(("fury", 100.0, 0.0, 5.0, 3.0), (w.Resource.Id, w.Resource.BaseMax, w.Resource.RegenPerSec, w.Resource.DecayPerSec, w.Resource.DecayDelaySec));
        Assert.Null(w.Resource.MaxPer);
        Assert.Equal(2, w.ClassBonuses.Count);
        Bonus(w, "resource_gen", ModKind.Increased, 0.2, Attr.Will, null);
        Bonus(w, "crit_chance", ModKind.Flat, 0.04, Attr.Dex, "attack");
        Assert.Equal("start_warrior", w.PassiveStartNodeId);

        var s = classes["sorcerer"];
        Assert.Equal(Attr.Int, s.Primary);
        Assert.Equal((7, 15, 10, 8), (s.BaseAttributes[Attr.Str], s.BaseAttributes[Attr.Int], s.BaseAttributes[Attr.Will], s.BaseAttributes[Attr.Dex]));
        Assert.Equal(1.0, s.LifeMultiplier);
        Assert.Equal(("mana", 100.0, 5.0, 0.0), (s.Resource.Id, s.Resource.BaseMax, s.Resource.RegenPerSec, s.Resource.DecayPerSec));
        Assert.NotNull(s.Resource.MaxPer);
        Assert.Equal(("int", 2.0, (double?)null), (s.Resource.MaxPer!.Stat, s.Resource.MaxPer.Every, s.Resource.MaxPer.Cap));
        Assert.Equal(2, s.ClassBonuses.Count);
        Bonus(s, "resource_regen", ModKind.Increased, 0.2, Attr.Will, null);
        Bonus(s, "crit_chance", ModKind.Flat, 0.04, Attr.Dex, "spell");
        Assert.Equal("start_sorcerer", s.PassiveStartNodeId);
    }
}
