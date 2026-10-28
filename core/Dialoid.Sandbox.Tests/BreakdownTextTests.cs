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
        Assert.True(Idx("Меньше: ×0,9 (−10%)") > Idx("Больше"));
        Assert.Contains("+20%", lines[Idx("Рост")]);
        Assert.Contains("×1,5 (+50%)", lines[Idx("Больше")]);
        Assert.Contains("178,2", lines[^1]);
        Assert.StartsWith("Итог", lines[^1]);
    }

    [Fact]
    public void Override_and_cap_are_shown()
    {
        var ov = new BreakdownLine(Src("Ключ"), ModKind.Override, 75);
        var r = new StatResult("dodge_chance", 0, 120, 75, Array.Empty<BreakdownLine>(), ov);
        var lines = BreakdownText.Lines(r);
        Assert.Contains(lines, l => l.StartsWith("Переопределение") && l.Contains("Ключ") && l.Contains("75"));
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

    [Fact]
    public void More_factor_keeps_precision_so_factors_multiply_to_total()
    {
        var r = new StatResult("damage", 100, 0, 0, new[]
        {
            new BreakdownLine(Src("Малый"), ModKind.More, 5),
            new BreakdownLine(Src("Средний"), ModKind.More, 15),
        }, null);
        var lines = BreakdownText.Lines(r);
        Assert.Contains(lines, l => l.Contains("×1,05 (+5%)") && l.Contains("Малый"));
        Assert.Contains(lines, l => l.Contains("×1,15 (+15%)") && l.Contains("Средний"));
    }

    [Fact]
    public void Small_flat_and_increased_parts_are_shown_exactly()
    {
        var r = new StatResult("life", 10, 0, 0, new[]
        {
            new BreakdownLine(Src("А"), ModKind.Flat, 0.25),
            new BreakdownLine(Src("Б"), ModKind.Flat, 0.25),
            new BreakdownLine(Src("В"), ModKind.Increased, 0.25),
            new BreakdownLine(Src("Г"), ModKind.Increased, 0.25),
        }, null);
        var lines = BreakdownText.Lines(r);
        Assert.Contains("Плюс: +0,5", lines.Single(l => l.StartsWith("Плюс")));
        Assert.Contains(lines, l => l.Contains("+0,25 — А"));
        Assert.Contains("Увеличение: +0,5%", lines.Single(l => l.StartsWith("Увеличение")));
        Assert.Contains(lines, l => l.Contains("+0,25% — Г"));
    }

    [Fact]
    public void Negative_values_use_unicode_minus_and_no_negative_zero()
    {
        Assert.Equal("\u22122,5", BreakdownText.Number(-2.5));
        Assert.Equal("\u22123", BreakdownText.Whole(-3));
        Assert.Equal("0,0", BreakdownText.Number(-0.04));
        Assert.Equal("0", BreakdownText.Whole(-0.2));
        var r = new StatResult("x", 0, -5, 0, Array.Empty<BreakdownLine>(), null);
        Assert.Contains("\u22125,0 → 0,0", BreakdownText.Lines(r)[^1]);
    }
}
