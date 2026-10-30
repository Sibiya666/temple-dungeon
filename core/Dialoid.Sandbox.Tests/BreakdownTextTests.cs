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
        Assert.Equal("Итог: 178,2", lines[^1]); // not capped: no «до капа → после капа»
    }

    [Fact]
    public void Integral_total_is_whole_like_the_shown_number()
    {
        var r = new StatResult("life", 0, 912, 912, new[] { new BreakdownLine(Src("Уровень"), ModKind.Flat, 912) }, null);
        Assert.Equal("Итог: 912", BreakdownText.Lines(r)[^1]); // the same text as «Здоровье: 912»
    }

    [Fact]
    public void Value_is_whole_when_integral()
    {
        Assert.Equal("912", BreakdownText.Value(912));
        Assert.Equal("0", BreakdownText.Value(-0.0));
        Assert.Equal("\u221212", BreakdownText.Value(-12));
        Assert.Equal("22,8", BreakdownText.Value(22.8));
        Assert.Equal("0,5", BreakdownText.Value(0.5));
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
        Assert.Equal("Итог (до капа → после капа): 90,0 → 75,0", BreakdownText.Lines(capped)[^1]);
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

    [Theory]
    [InlineData("use", "применение")]
    [InlineData("hit", "попадание")]
    [InlineData("crit", "крит")]
    [InlineData("ailment", "недуг")]
    [InlineData("kill", "убийство")]
    [InlineData("no_resource", "нет ресурса")]
    [InlineData("dummy_attack", "атака манекена")]
    [InlineData("dodge", "уклонение")]
    [InlineData("death", "смерть")]
    [InlineData("proc", "прок")]
    [InlineData("proc_blocked", "прок заблокирован")]
    [InlineData("mechanic", "механика")]
    [InlineData("log_truncated", "лог обрезан")]
    [InlineData("something_new", "something_new")]
    public void Event_kinds_are_russian_with_raw_fallback(string kind, string expected) =>
        Assert.Equal(expected, BreakdownText.EventKindName(kind));

    [Fact]
    public async Task Every_event_kind_of_reference_runs_has_a_russian_name()
    {
        var dir = DataLocator.Find(AppContext.BaseDirectory)!;
        var kinds = new HashSet<string>();
        foreach (var info in ReferenceBuilds.List(dir).Where(i => i.Group == "main"))
        {
            var s = SandboxSession.Create(TestData.Data, "warrior");
            Assert.True(ReferenceBuilds.Load(s, info).Ok);
            var attacking = TestData.Data.Dummies.Values.First(d => d.Attack != null).Id;
            foreach (var request in new[] { new SimRequest("training", SimMode.Roll, 1, null, false), new SimRequest(attacking, SimMode.Roll, 1, null, true) })
                foreach (var e in (await new SimulationRunner(s).RunAsync(request)).Result.Log) kinds.Add(e.Kind);
        }
        Assert.Contains("use", kinds);
        foreach (var kind in kinds) Assert.Matches("^[а-яё ]+$", BreakdownText.EventKindName(kind));
    }

    [Fact]
    public void No_time_to_death_text_tells_apart_no_attack_and_survival()
    {
        var plain = new SimResult(0, 0, new Dictionary<string, double>(), new Dictionary<Dialoid.Core.Combat.DamageType, double>(),
            0, 0, null, null, Array.Empty<SimEvent>(), Array.Empty<Dialoid.Core.Character.RuleViolation>());
        Assert.Equal("Время до смерти: манекен не бьет", BreakdownText.NoTimeToDeath(false, plain));
        Assert.Equal("Время до смерти: этот манекен не атакует", BreakdownText.NoTimeToDeath(true, plain));
        var defended = plain with { Defense = new DefenseTotals(1, 0, new Dictionary<Dialoid.Core.Combat.DamageType, double>(), 0, 0, 0) };
        Assert.Equal("Время до смерти: персонаж выжил", BreakdownText.NoTimeToDeath(true, defended));
    }

    [Fact]
    public void Dark_type_names()
    {
        Assert.Equal("тьма", Texts.DamageTypeName(Dialoid.Core.Combat.DamageType.Dark));
        Assert.Equal("тьмой", Texts.DamageTypeInstrumental(Dialoid.Core.Combat.DamageType.Dark));
        Assert.Equal("Сопротивление тьме", Texts.StatTitle("resist_dark"));
    }
}
