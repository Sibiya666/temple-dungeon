using System.Globalization;
using Dialoid.Core.Character;
using Dialoid.Core.Explain;
using Dialoid.Core.Stats;
using Dialoid.Core.Tests.Content;

namespace Dialoid.Core.Tests.Explain;

public class CharacterSheetTests
{
    public static IEnumerable<object[]> ReferenceBuilds() => new[]
    {
        "warrior_berserker_whirlwind", "warrior_thunderer_crush", "warrior_fire_cleave_crit",
        "sorcerer_pyromancer_fireball", "sorcerer_cryomancer_ice_lance", "sorcerer_spark_hybrid",
    }.Select(b => new object[] { b });

    private static CompiledCharacter Compile(string build) =>
        new CharacterCompiler(ContentHarness.Data).Compile(ContentHarness.LoadBuild(build));

    private static string Fmt(double v)
    {
        var text = v.ToString("0.##", CultureInfo.InvariantCulture);
        if (text.StartsWith('-')) text = text == "-0" ? "0" : "\u2212" + text[1..];
        return text.Replace('.', ',');
    }

    [Fact]
    public void Describe_lists_life_armor_resists_and_attributes()
    {
        var ch = Compile("warrior_berserker_whirlwind");
        var lines = CharacterSheet.Describe(ch, ContentHarness.Data);

        var life = lines.Single(l => l.Key == "life");
        Assert.Equal(ch.Evaluator.Evaluate("life", TagSet.Empty, NoConditions.Instance).Value, life.Value);
        Assert.NotNull(life.Breakdown);
        Assert.NotEmpty(life.Breakdown!.Lines);

        foreach (var key in new[] { "str", "int", "will", "dex", "armor", "life_regen_pct", "resource_max", "resource_regen",
                     "resource_decay", "dodge_chance", "crit_chance", "crit_damage", "primary_damage_more" })
            Assert.Contains(lines, l => l.Key == key);

        foreach (var element in new[] { "fire", "cold", "lightning" })
        {
            var line = lines.Single(l => l.Key == "resist_" + element);
            var cap = ch.Evaluator.Evaluate($"resist_{element}_max", TagSet.Empty, NoConditions.Instance).Value;
            Assert.Contains($"{Fmt(line.Value)} / кап {Fmt(cap)}", line.Label);
        }
    }

    [Theory]
    [MemberData(nameof(ReferenceBuilds))]
    public void Every_line_with_breakdown_matches_its_value(string build)
    {
        var lines = CharacterSheet.Describe(Compile(build), ContentHarness.Data);

        Assert.Equal(lines.Count, lines.Select(l => l.Key).Distinct().Count());
        foreach (var line in lines)
        {
            Assert.False(string.IsNullOrWhiteSpace(line.Label), line.Key);
            if (line.Breakdown is { } b) Assert.Equal(b.Value, line.Value);
            else Assert.False(string.IsNullOrWhiteSpace(line.Note), $"derived line {line.Key} must list its parts");
        }
    }
}
