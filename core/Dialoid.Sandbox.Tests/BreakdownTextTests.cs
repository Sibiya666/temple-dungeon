using Dialoid.Core.Simulation;
using Dialoid.Core.Stats;
using Dialoid.Sandbox;

namespace Dialoid.Sandbox.Tests;

public class BreakdownTextTests
{
    private static SourceRef Src(string name) => new("passive", name.ToLowerInvariant(), name);

    [Fact]
    public void Breakdown_lines_follow_formula_order_and_name_sources()
    {
        var r = new StatResult("life", 100, 0, 0, new[]
        {
            new BreakdownLine(Src("Бонус"), ModKind.More, 50),
            new BreakdownLine(Src("Рост"), ModKind.Increased, 20),
            new BreakdownLine(Src("Плюс"), ModKind.Flat, 10),
            new BreakdownLine(Src("Меньше"), ModKind.More, -10),
        }, null);
        r = r with { Uncapped = (100 + 10) * 1.2 * 1.5 * 0.9, Value = (100 + 10) * 1.2 * 1.5 * 0.9 };

        var lines = BreakdownText.Lines(r);

        int Idx(string part) => lines.ToList().FindIndex(l => l.Contains(part));
        Assert.StartsWith("База", lines[0]);
        Assert.True(Idx("Плюс") > 0);
        Assert.True(Idx("Увеличение") > Idx("Плюс"));
        Assert.True(Idx("Рост") > Idx("Увеличение"));
        Assert.True(Idx("Больше") > Idx("Рост"));
        Assert.True(Idx("Бонус") > 0);
        Assert.True(Idx("Меньше: ×0,9") > Idx("Больше"));
        Assert.Contains("+20,0%", lines[Idx("Рост")]);
        Assert.Contains("×1,5", lines[Idx("Больше")]);
        Assert.Contains("178,2", lines[^1]);
        Assert.StartsWith("Итог", lines[^1]);
    }

    [Fact]
    public void Override_and_cap_are_shown()
    {
        var ov = new BreakdownLine(Src("Ключ"), ModKind.Override, 75);
        var r = new StatResult("dodge_chance", 0, 120, 75, Array.Empty<BreakdownLine>(), ov);
        var lines = BreakdownText.Lines(r);
        Assert.Contains(lines, l => l.StartsWith("Переопределение") && l.Contains("Ключ") && l.Contains("75,0"));
        Assert.Contains("120,0 → 75,0", lines[^1]);

        var capped = new StatResult("resist_fire", 0, 90, 75, new[] { new BreakdownLine(Src("Огонь"), ModKind.Flat, 90) }, null);
        Assert.Contains("90,0 → 75,0", BreakdownText.Lines(capped)[^1]);
    }

    [Fact]
    public void Unknown_text_key_falls_back_to_key()
    {
        Assert.Equal("no_such_key", BreakdownText.KeyTitle("no_such_key"));
        Assert.Equal("no_such_stat", BreakdownText.Title("no_such_stat"));
        Assert.Equal("Здоровье", BreakdownText.Title("life"));
        Assert.Equal("Применений в секунду", BreakdownText.KeyTitle("uses_per_sec"));
        Assert.Equal("no_dummy", BreakdownText.DummyName(TestData.Data, "no_dummy"));
        Assert.Equal(TestData.Data.Dummies["training"].Name, BreakdownText.DummyName(TestData.Data, "training"));
        Assert.Equal("среднее", BreakdownText.ModeName(SimMode.Average));
        Assert.Equal("бросок", BreakdownText.ModeName(SimMode.Roll));
    }

    [Fact]
    public void Numbers_use_fixed_formats_with_decimal_comma()
    {
        Assert.Equal("12,5", BreakdownText.Number(12.5));
        Assert.Equal("1235", BreakdownText.Whole(1234.6));
        Assert.Equal("3,0", BreakdownText.Seconds(3));
    }
}
