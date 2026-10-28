using Dialoid.Core.Character;
using Dialoid.Core.Explain;
using Dialoid.Core.Tests.Content;

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
        Assert.NotNull(Line(lines, "hit_avg_fire").Breakdown);
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
        Assert.Contains(full.Breakdown!.Lines, l => l.Source.Id == node);
        Assert.DoesNotContain(reduced.Breakdown!.Lines, l => l.Source.Id == node);
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
