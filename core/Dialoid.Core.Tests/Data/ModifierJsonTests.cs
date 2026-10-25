using System.Text.Json;
using Dialoid.Core.Data.Json;
using Dialoid.Core.Stats;

namespace Dialoid.Core.Tests.Data;

public class ModifierJsonTests
{
    private static readonly SourceRef Src = new("passive", "p1", "P1");

    [Fact]
    public void Reads_short_form_with_tags_per_and_conditions()
    {
        using var doc = JsonDocument.Parse(
            "{\"stat\":\"damage\",\"kind\":\"increased\",\"value\":8,\"tags\":[\"fire\"],\"per\":{\"stat\":\"str\",\"every\":10,\"cap\":5}," +
            "\"conditions\":[\"target_ignited\",{\"id\":\"life_below_pct\",\"param\":35}]}");
        var errors = new List<string>();
        var m = ModifierJson.Read(doc.RootElement, Src, errors);
        Assert.Empty(errors);
        Assert.Equal(new Modifier("damage", ModKind.Increased, 8, TagSet.Of("fire"),
            new[] { new Condition("target_ignited"), new Condition("life_below_pct", 35) },
            new PerStat("str", 10, 5), Src, 0).Stat, m!.Stat);
        Assert.Equal(ModKind.Increased, m.Kind);
        Assert.Equal(TagSet.Of("fire"), m.RequiredTags);
        Assert.Equal(new PerStat("str", 10, 5), m.Per);
        Assert.Equal(2, m.Conditions.Count);
        Assert.Equal(35, m.Conditions[1].Param);
    }

    [Fact]
    public void Bad_kind_and_missing_stat_report_errors()
    {
        using var doc = JsonDocument.Parse("{\"kind\":\"weird\",\"value\":1}");
        var errors = new List<string>();
        Assert.Null(ModifierJson.Read(doc.RootElement, Src, errors));
        Assert.Equal(2, errors.Count);
    }

    [Theory]
    [InlineData("1.5")]
    [InlineData("1e10")]
    public void Non_integer_or_huge_priority_reports_error_instead_of_throwing(string priority)
    {
        using var doc = JsonDocument.Parse("{\"stat\":\"damage\",\"kind\":\"override\",\"value\":1,\"priority\":" + priority + "}");
        var errors = new List<string>();
        Assert.Null(ModifierJson.Read(doc.RootElement, Src, errors));
        var e = Assert.Single(errors);
        Assert.Contains("priority", e);
    }
}
