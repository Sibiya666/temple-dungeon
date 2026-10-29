using Dialoid.Core.Character;
using Dialoid.Core.Combat;
using Dialoid.Core.Explain;
using Dialoid.Core.Tests.Content;
using Dialoid.Core.Tests.Fixtures;

namespace Dialoid.Core.Tests.Explain;

public class SkillSheetTests
{
    private static IReadOnlyList<NumberLine> Sheet(Build build, string skillId, bool withWeapon = true)
    {
        var data = ContentHarness.Data;
        var ch = new CharacterCompiler(data).Compile(build);
        var weapon = withWeapon && build.WeaponId != null ? data.Weapons[build.WeaponId] : null;
        return SkillSheet.Describe(ch, ch.Skills.Single(s => s.SkillId == skillId), data, weapon);
    }

    private static NumberLine Line(IReadOnlyList<NumberLine> lines, string key) => lines.Single(l => l.Key == key);

    [Fact]
    public void Fireball_sheet_shows_fire_hit_range_and_uses_per_second()
    {
        var lines = Sheet(ContentHarness.LoadBuild("sorcerer_pyromancer_fireball"), "fireball");

        var avg = Line(lines, "hit_avg_fire").Value;
        Assert.True(avg > 0);
        Assert.InRange(avg, Line(lines, "hit_min_fire").Value, Line(lines, "hit_max_fire").Value);
        Assert.True(Line(lines, "uses_per_sec").Value > 0);
        Assert.Null(Line(lines, "hit_avg_fire").Breakdown);
        Assert.Contains(lines, l => l.Key.StartsWith("hit_mult_fire", StringComparison.Ordinal) && l.Breakdown != null);
        Assert.Contains(lines, l => l.Key == "penetration_fire" && l.Value > 0); // int_incineration and int_flame_6
        Assert.True(Line(lines, "hit_avg_with_crit").Value > avg);
        Assert.Contains(lines, l => l.Key == "crit_chance" && l.Breakdown != null);
        Assert.Contains(lines, l => l.Key == "resource_cost");
        Assert.Contains(lines, l => l.Key == "ailment_chance_ignite" && l.Value > 0);
        Assert.Contains(lines, l => l.Key == "projectile_count");

        Assert.Equal(lines.Count, lines.Select(l => l.Key).Distinct().Count());
        foreach (var line in lines.Where(l => l.Breakdown == null))
            Assert.False(string.IsNullOrWhiteSpace(line.Note), $"derived line {line.Key} must list its parts");
    }

    [Fact]
    public void Taking_a_damage_node_changes_the_sheet_and_its_breakdown_names_the_node()
    {
        const string node = "int_incineration"; // notable: +25% increased fire damage
        var build = ContentHarness.LoadBuild("sorcerer_pyromancer_fireball");
        Assert.Contains(node, build.PassiveNodes);
        var without = ContentHarness.Clone(build);
        without.PassiveNodes.Remove(node);

        var full = Line(Sheet(build, "fireball"), "hit_avg_fire");
        var reduced = Line(Sheet(without, "fireball"), "hit_avg_fire");

        Assert.True(reduced.Value < full.Value, $"{reduced.Value} should be below {full.Value}");
        static IEnumerable<Dialoid.Core.Stats.BreakdownLine> FireMultLines(IReadOnlyList<NumberLine> sheet) => sheet
            .Where(l => l.Key.StartsWith("hit_mult_fire", StringComparison.Ordinal)).SelectMany(l => l.Breakdown!.Lines);
        Assert.Contains(FireMultLines(Sheet(build, "fireball")), l => l.Source.Id == node);
        Assert.DoesNotContain(FireMultLines(Sheet(without, "fireball")), l => l.Source.Id == node);
    }

    public static IEnumerable<object[]> ReferenceBuilds() => CharacterSheetTests.ReferenceBuilds();

    [Theory]
    [MemberData(nameof(ReferenceBuilds))]
    public void Every_skill_line_with_breakdown_matches_its_value(string buildPath)
    {
        var build = ContentHarness.LoadBuild(buildPath);
        foreach (var slot in build.Slots)
        {
            var lines = Sheet(build, slot.SkillId);
            Assert.Equal(lines.Count, lines.Select(l => l.Key).Distinct().Count());
            foreach (var line in lines)
            {
                if (line.Breakdown is { } b) Assert.Equal(b.Value, line.Value);
                else Assert.False(string.IsNullOrWhiteSpace(line.Note), $"{slot.SkillId}: derived line {line.Key} must list its parts");
            }
        }
    }

    /// <summary>Every reference build file of the main, l20 and parity groups (and any other folder under data/builds).</summary>
    public static IEnumerable<object[]> AllReferenceBuilds()
    {
        var root = Path.Combine(FixturePaths.RealData, "builds");
        return Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f)[..^".json".Length].Replace('\\', '/'))
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => new object[] { f });
    }

    /// <summary>NumberLine keys (hit_mult_*, hit_avg_*, penetration_* and the rest) never repeat within one skill sheet.</summary>
    [Theory]
    [MemberData(nameof(AllReferenceBuilds))]
    public void Skill_sheet_keys_are_unique_for_every_slotted_skill(string buildPath)
    {
        var build = ContentHarness.LoadBuild(buildPath);
        var data = ContentHarness.Data;
        var ch = new CharacterCompiler(data).Compile(build);
        var weapon = build.WeaponId != null ? data.Weapons[build.WeaponId] : null;
        Assert.All(build.Slots, slot => Assert.Contains(ch.Skills, s => s.SkillId == slot.SkillId));
        foreach (var skill in ch.Skills)
        {
            var keys = SkillSheet.Describe(ch, skill, data, weapon).Select(l => l.Key).ToList();
            var repeated = keys.GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Assert.True(repeated.Count == 0, $"{buildPath}/{skill.SkillId}: repeated keys {string.Join(", ", repeated)}");
        }
    }

    /// <summary>Labels and notes use the Russian decimal comma and the Unicode minus (no «22.8», no ASCII «-3»).</summary>
    [Theory]
    [MemberData(nameof(ReferenceBuilds))]
    public void Sheet_texts_use_decimal_comma(string buildPath)
    {
        var build = ContentHarness.LoadBuild(buildPath);
        var data = ContentHarness.Data;
        var lines = CharacterSheet.Describe(new CharacterCompiler(data).Compile(build), data)
            .Concat(build.Slots.SelectMany(slot => Sheet(build, slot.SkillId)));
        var invariant = new System.Text.RegularExpressions.Regex(@"\d\.\d|(^|[\s(×/])-\d");
        foreach (var line in lines)
        {
            Assert.DoesNotMatch(invariant, line.Label);
            if (line.Note != null) Assert.DoesNotMatch(invariant, line.Note);
        }
    }

    /// <summary>Every average hit is Σ over its portions of avg(base) × the portion's multiplier line × the penetration factor:
    /// the sheet's own lines add up to the number (no hidden factor).</summary>
    [Theory]
    [MemberData(nameof(ReferenceBuilds))]
    public void Hit_lines_are_the_product_of_base_multiplier_and_penetration(string buildPath)
    {
        var data = ContentHarness.Data;
        var build = ContentHarness.LoadBuild(buildPath);
        var ch = new CharacterCompiler(data).Compile(build);
        var weapon = build.WeaponId != null ? data.Weapons[build.WeaponId] : null;
        var checkedLines = 0;
        foreach (var skill in ch.Skills)
        {
            var lines = SkillSheet.Describe(ch, skill, data, weapon);
            var ev = ch.Evaluator.WithExtra(skill.Modifiers);
            var portions = ConversionResolver.Apply(BaseDamage.Compute(skill, build.Level, weapon, ev, skill.Tags), skill.Conversions)
                .Where(p => !skill.NullifiedDamageTypes.Contains(p.Type)).ToList();
            foreach (var avg in lines.Where(l => l.Key.StartsWith("hit_avg_", StringComparison.Ordinal)
                                                 && l.Key is not "hit_avg_total" and not "hit_avg_with_crit"))
            {
                var name = avg.Key["hit_avg_".Length..];
                var pen = lines.SingleOrDefault(l => l.Key == "penetration_" + name)?.Value ?? 0;
                var penFactor = Mitigation.ResistMultiplier(0, pen);
                var expected = portions.Where(p => p.Type.ToString().ToLowerInvariant() == name).Sum(p =>
                {
                    var origins = p.Lineage.Where(t => t != p.Type).OrderBy(t => t).Select(t => t.ToString().ToLowerInvariant()).ToList();
                    var key = origins.Count == 0 ? $"hit_mult_{name}" : $"hit_mult_{name}_from_{string.Join("_", origins)}";
                    return (p.Min + p.Max) / 2 * Math.Max(0, Line(lines, key).Value) * penFactor;
                });
                Assert.Equal(expected, avg.Value, 6);
                checkedLines++;
            }
        }
        Assert.True(checkedLines > 0);
    }

    [Fact]
    public void Attack_sheet_uses_weapon()
    {
        var build = ContentHarness.LoadBuild("warrior_fire_cleave_crit");
        var withWeapon = Line(Sheet(build, "cleave"), "hit_avg_with_crit").Value;
        var bare = Line(Sheet(build, "cleave", withWeapon: false), "hit_avg_with_crit").Value;

        Assert.True(withWeapon > 0);
        Assert.NotEqual(withWeapon, bare);
    }
}
