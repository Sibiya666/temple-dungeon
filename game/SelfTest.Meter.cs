using Dialoid.Core.Passives;
using Dialoid.Core.Simulation;
using Dialoid.Game.Ui;
using Dialoid.Sandbox;
using Godot;

namespace Dialoid.Game;

/// <summary>
/// Self-test steps of the combat log, the damage meter and the charts (plan 4 task 11): the meter's bars are the segment's rows
/// one to one and the log's amounts add up to the meter; the defence split adds up to the damage before defence with the
/// vulnerability bonus; the charts build non-empty series; a passive taken off in the P window gives a new segment «после правки
/// билда» with another DPS; «Сбросить» asks first; the log's filters and autoscroll; a bar click opens the skill's breakdown.
/// </summary>
public sealed partial class SelfTest
{
    private const string Thunderer = "warrior_thunderer_crush.json";
    private const string SparkHybrid = "sorcerer_spark_hybrid.json";

    private MeterWindow Meter => _main.MeterView ?? throw new Failure("нет измерителя");
    private CombatLogWindow LogWindow => _main.LogView ?? throw new Failure("нет журнала");
    private ChartsWindow Charts => _main.ChartsView ?? throw new Failure("нет окна графиков");

    private static readonly MeterMode[] MeterModes = [MeterMode.DpsBySkill, MeterMode.DamageBySkill, MeterMode.DamageByType, MeterMode.Mitigation];

    private IEnumerable<Step> MeterSteps()
    {
        yield return new("windows.charts-above-log", ChartsAboveLog);
        yield return new("meter.matches-segment-and-log", MeterMatchesSegmentAndLog);
        yield return new("meter.bar-opens-breakdown", MeterBarOpensBreakdown);
        yield return new("log.filters-and-autoscroll", LogFiltersAndAutoscroll);
        yield return new("meter.mitigation-identity", MeterMitigationIdentity);
        yield return new("charts.series", ChartsSeries);
        yield return new("meter.segment-after-build-edit", MeterSegmentAfterBuildEdit);
        yield return new("meter.reset-confirm", MeterResetConfirm);
        yield return new("meter.restore", async () =>
        {
            Meter.SetSimMode(SimMode.Roll, 1);
            Meter.SetMode(MeterMode.DpsBySkill);
            Meter.SetFloatingNumbers(true);
            _main.CloseAllWindows();
            await CombatRestore();
        });
    }

    // ---------- helpers ----------

    /// <summary>Presses every filled slot in turn, one press per 0,3 s, for <paramref name="seconds"/> of game time; then, with
    /// <paramref name="close"/>, waits until the segment closes (5 s without damage).</summary>
    private async Task Fight(double seconds, bool close)
    {
        var slots = Training.Slots.Where(s => s != null).Select(s => s!.Slot).ToList();
        Check(slots.Count > 0, "в билде нет скиллов");
        var next = 0.0;
        var turn = 0;
        await Run(seconds, t =>
        {
            if (t < next) return;
            next = t + 0.3;
            Driver.PressSlot(slots[turn++ % slots.Count]);
        });
        if (close) await Run(TrainingRoom.SegmentIdleSec + 40, until: () => Training.Current == null); // DoTs and zones tick on
        if (close) Check(Training.Current == null, $"сегмент не закрылся: последний урон в {Training.Current?.End:0.0} с, бой {Training.State?.Time:0.0} с");
    }

    /// <summary>A short closed segment of a reference next to the training dummy.</summary>
    private async Task<FightSegment> ShortSegment(string file, double seconds = 4)
    {
        await LoadForCombat(file);
        PlaceNearDummy();
        await Fight(seconds, close: true);
        Check(Training.Segments.Count > 0, "сегмента нет");
        return Training.Segments[^1];
    }

    private static bool Near(double a, double b, double tolerance) => Math.Abs(a - b) <= tolerance;

    // ---------- steps ----------

    /// <summary>
    /// «Графики» (left) and «Журнал» (bottom left) never overlap at their default places: open together on this screen, and by
    /// their default rects on 1600×1000, 1600×900, 1366×768 and 1280×720.
    /// </summary>
    private async Task ChartsAboveLog()
    {
        var windows = _main.Windows;
        UseSelfTestWindowConfig();
        windows.OpenWindow(WindowIds.Charts);
        windows.OpenWindow(WindowIds.Log);
        await Settle();
        var charts = windows.Window(WindowIds.Charts).GetGlobalRect();
        var log = windows.Window(WindowIds.Log).GetGlobalRect();
        Check(!charts.Intersects(log), $"графики {charts} и журнал {log} перекрываются на экране {_main.Size}");
        foreach (var screen in new[] { new Vector2(1600, 1000), new Vector2(1600, 900), new Vector2(1366, 768), new Vector2(1280, 720) })
        {
            var c = windows.DefaultRect(WindowHost.SpecOf(WindowIds.Charts), screen);
            var l = windows.DefaultRect(WindowHost.SpecOf(WindowIds.Log), screen);
            Check(!c.Intersects(l), $"на {screen}: графики {c} и журнал {l} перекрываются");
        }
        GD.Print($"SELFTEST NOTE графики над журналом: {charts} / {log} на {_main.Size}");
        windows.CloseAll();
    }

    /// <summary>
    /// After a short fight (under 2000 log lines) the bars of every mode are the segment's rows (key, name, value exactly), and the
    /// amounts of the segment's log lines, shown and hidden, add up to the meter's total to the unit. The name column of the log
    /// and the bars hold the longest names whole.
    /// </summary>
    private async Task MeterMatchesSegmentAndLog()
    {
        var segment = await ShortSegment(WarriorMelee);
        Check(Training.Log.Count < TrainingRoom.MaxLogLines && !segment.Trimmed, $"сегмент не короткий: строк журнала {Training.Log.Count}");
        var meter = Meter;
        _main.OpenWindow(WindowIds.Meter);
        _main.OpenWindow(WindowIds.Log);
        await Settle();
        meter.SelectSegment(null);
        foreach (var mode in MeterModes)
        {
            meter.SetMode(mode);
            meter.Refresh();
            await NextFrame();
            var shown = meter.ShownRows;
            var expected = MeterWindow.RowsOf(segment, mode);
            Check(mode != MeterMode.Mitigation || (expected[0].Key == MeterWindow.BeforeKey && expected.Skip(1).SequenceEqual(segment.Rows(mode))
                && Near(expected[0].Value, segment.MitigationBySkill().Values.Sum(p => p.BeforeDefense), 1e-6 * Math.Max(1, expected[0].Value))),
                "«Потеря на защите» не начинается с «до защиты» или полосы не те, что у сегмента");
            if (mode != MeterMode.Mitigation) Check(expected.SequenceEqual(segment.Rows(mode)), $"{mode}: полосы окна не те, что у сегмента");
            Check(shown.Count == expected.Count && shown.Count > 0, $"{mode}: полос {shown.Count}, у сегмента {expected.Count}");
            for (var i = 0; i < shown.Count; i++)
                Check(shown[i].Key == expected[i].Key && shown[i].Label == expected[i].Label && shown[i].Value == expected[i].Value && shown[i].IsBonus == expected[i].IsBonus,
                    $"{mode}: полоса {i} «{shown[i].Label}» {shown[i].Value:R}, у сегмента «{expected[i].Label}» {expected[i].Value:R}");
            for (var i = 0; i < shown.Count; i++)
                Check(meter.Bars.RowFits(i), $"{mode}: в полосе «{shown[i].Label}» имя и число не помещаются в {meter.Bars.Size.X:0} px");
        }
        meter.SetMode(MeterMode.DamageBySkill);
        meter.Refresh();
        var meterTotal = meter.ShownRows.Sum(r => r.Value);
        Check(Near(meterTotal, segment.TotalDamage, 1e-6 * Math.Max(1, segment.TotalDamage)), $"сумма полос {meterTotal:R}, урон сегмента {segment.TotalDamage:R}");

        var log = LogWindow;
        log.Refresh();
        Check(!log.VisibleKinds.Contains(LogKind.DotTick), "тики недугов не скрыты по умолчанию");
        var hiddenKinds = Enum.GetValues<LogKind>().Where(k => !log.VisibleKinds.Contains(k)).ToHashSet();
        var shownSum = log.ShownEntries.Where(e => e.Segment == segment.Index).Sum(e => e.Amount ?? 0);
        var hiddenSum = Training.Filter(hiddenKinds, null).Where(e => e.Segment == segment.Index).Sum(e => e.Amount ?? 0);
        var logTotal = shownSum + hiddenSum;
        Check(Math.Abs(logTotal - segment.TotalDamage) < 1, $"журнал: видимые {shownSum:R} + скрытые {hiddenSum:R} = {logTotal:R}, измеритель {segment.TotalDamage:R}");
        Check(log.ShownEntries.Count == Training.Filter(log.VisibleKinds.ToHashSet(), null).Count, "строк в окне не столько, сколько дает фильтр");

        var tree = log.Lines;
        var nameColumn = tree.GetColumnWidth(1);
        var need = log.NameTextWidth() + 2 * tree.GetThemeConstant("h_separation");
        Check(nameColumn >= need, $"колонка имени {nameColumn} px уже имени {need:0} px («{CombatLogWindow.LongestNameSample}»)");
        GD.Print($"SELFTEST NOTE измеритель = журнал: сегмент {segment.Label}, урон {segment.TotalDamage:0.###}, журнал видимые {shownSum:0.###} + скрытые {hiddenSum:0.###}, " +
                 $"разница {logTotal - segment.TotalDamage:0.######}; строк журнала {Training.Log.Count}; колонка имени {nameColumn} px при имени {need:0} px");
    }

    /// <summary>
    /// A click on a skill bar (through the GUI) opens «Разбор» headed by the bar's own number (the skill's DPS in the segment with the
    /// dummy's defence, its split in the note) with the skill's main damage line one step back in the history.
    /// </summary>
    private async Task MeterBarOpensBreakdown()
    {
        var meter = Meter;
        _main.CloseAllWindows();
        _main.OpenWindow(WindowIds.Meter);
        meter.SetMode(MeterMode.DpsBySkill);
        meter.Refresh();
        await Settle();
        var segment = meter.SelectedSegment ?? throw new Failure("нет сегмента");
        var top = meter.ShownRows[0];
        var bars = meter.Bars.GetGlobalRect();
        PushButton(bars.Position + new Vector2(20, MeterBars.RowHeight / 2), MouseButton.Left);
        await Settle();
        Check(_main.IsOpen(WindowIds.Breakdown), "клик по полосе не открыл «Разбор»");
        var current = _main.Breakdown!.Current ?? throw new Failure("«Разбор» пуст");
        var name = _main.Session!.Data.Skills[top.Key].Name;
        Check(current.Line.Key == MeterWindow.BarKey(top.Key) && current.Context == $"{name} · сегмент №{segment.Index}", $"открылось «{current.Line.Key}» ({current.Context}), а не DPS полосы «{name}»");
        Check(_main.Breakdown.TitleText.EndsWith(BreakdownText.Value(top.Value), StringComparison.Ordinal), $"в заголовке «Разбора» нет числа полосы: «{_main.Breakdown.TitleText}»");
        Check(current.Line.Value == top.Value && top.Value == segment.DpsBySkill().Single(r => r.Key == top.Key).Value,
            $"в «Разборе» {current.Line.Value:R}, на полосе {top.Value:R}");
        var (expected, _) = meter.SkillLine(segment, top.Key);
        Check(current.Line.Note == expected.Note, "пояснение не то, что у полосы");
        Check(current.Line.Note!.Contains(segment.Label) && current.Line.Note.Contains("Защита манекена") && current.Line.Note.Contains("Лист скилла"),
            $"в пояснении нет чисел сегмента или строки листа: {current.Line.Note}");
        var history = _main.Breakdown.History;
        Check(history.Count >= 2 && history[1].Line.Key == MeterWindow.MainDamageKey && history[1].Context == name,
            "главной строки урона скилла нет в истории «Разбора» под числом полосы");
        // Damage types and the defence split open nothing.
        meter.SetMode(MeterMode.DamageByType);
        meter.Refresh();
        Check(!meter.OpenRow(0), "полоса типа урона открыла разбор");
        GD.Print($"SELFTEST NOTE клик по полосе «{top.Label}»: {_main.Breakdown.TitleText} | {current.Line.Note.ReplaceLineEndings(" / ")}");
        _main.CloseAllWindows();
    }

    /// <summary>The kind and skill filters of the log, and autoscroll that stops when the owner scrolls up.</summary>
    private async Task LogFiltersAndAutoscroll()
    {
        var log = LogWindow;
        _main.CloseAllWindows();
        _main.OpenWindow(WindowIds.Log);
        await Settle();
        log.Refresh();
        var all = Enum.GetValues<LogKind>().ToHashSet();
        var ticks = Training.Log.Count(e => e.Kind == LogKind.DotTick);
        Check(!log.ShownEntries.Any(e => e.Kind == LogKind.DotTick), "тики недугов видны по умолчанию");
        log.SetKindVisible(LogKind.DotTick, true);
        log.Refresh();
        Check(log.ShownEntries.Count(e => e.Kind == LogKind.DotTick) == ticks, $"тиков в окне {log.ShownEntries.Count(e => e.Kind == LogKind.DotTick)}, в журнале {ticks}");
        var popup = log.KindsMenu.GetPopup();
        Check(popup.IsItemChecked(popup.GetItemIndex((int)LogKind.DotTick)), "галочка тиков не стоит");
        log.SetKindVisible(LogKind.Hit, false);
        var resetIndex = Enumerable.Range(0, popup.ItemCount).Single(i => popup.GetItemText(i) == "Восстановить виды по умолчанию");
        popup.EmitSignal(PopupMenu.SignalName.IdPressed, popup.GetItemId(resetIndex));
        foreach (var (kind, _) in CombatLogWindow.Kinds)
        {
            var expected = !TrainingRoom.DefaultHiddenKinds.Contains(kind);
            Check(log.VisibleKinds.Contains(kind) == expected && popup.IsItemChecked(popup.GetItemIndex((int)kind)) == expected,
                $"сброс фильтра не восстановил вид события {kind}");
        }

        var skill = Training.Log.First(e => e.SkillId != null).SkillId!;
        log.Refresh();
        Check(log.SkillChoices.Contains(skill), $"скилла {skill} нет в фильтре");
        log.SkillOption.Select(log.SkillChoices.ToList().IndexOf(skill));
        log.SkillOption.EmitSignal(OptionButton.SignalName.ItemSelected, log.SkillChoices.ToList().IndexOf(skill));
        log.Refresh();
        Check(log.SkillFilter == skill && log.ShownEntries.Count > 0 && log.ShownEntries.All(e => e.SkillId == skill), $"фильтр по скиллу {skill} пропустил чужие строки");
        log.SetSkillFilter(null);
        log.Refresh();
        Check(log.ShownEntries.Count == Training.Filter(log.VisibleKinds.ToHashSet(), null).Count, "без фильтра по скиллу строк не все");
        Check(log.StatusText.Contains("скрыты"), $"строка состояния не говорит о скрытых: «{log.StatusText}»");

        // Autoscroll: at the bottom while following; scrolling up stops it, new lines do not move the view, the bottom resumes it.
        var scroll = log.Scroll ?? throw new Failure("у журнала нет полосы прокрутки");
        bool Scrollable() => scroll.MaxValue - scroll.Page > scroll.MinValue + 50;
        PlaceNearDummy();
        for (var round = 0; round < 6 && (round == 0 || !Scrollable()); round++)
        {
            await Fight(2, close: false);
            log.Refresh();
            await Settle();
            log.Refresh();
        }
        Check(Scrollable(), $"журнал не прокручивается: строк {log.ShownEntries.Count}, max {scroll.MaxValue}, страница {scroll.Page}");
        await Settle();
        Check(log.FollowTail && log.AtBottom(), $"автопрокрутка не внизу: {scroll.Value} из {scroll.MaxValue - scroll.Page}");
        scroll.Value = 0; // the owner scrolls up
        Check(!log.FollowTail, "прокрутка вверх не остановила автопрокрутку");
        await Fight(1, close: false);
        log.Refresh();
        await Settle();
        Check(scroll.Value < 1 && !log.FollowTail, $"новые строки сдвинули журнал: {scroll.Value}");
        scroll.Value = scroll.MaxValue; // back to the bottom
        Check(log.FollowTail, "прокрутка вниз не вернула автопрокрутку");
        log.SetFollow(true);
        GD.Print($"SELFTEST NOTE журнал: строк {log.ShownEntries.Count}, тиков скрыто {ticks}, прокрутка до {scroll.MaxValue - scroll.Page:0}");
        _main.CloseAllWindows();
    }

    /// <summary>
    /// «Потеря на защите» on a build that shocks: «дошло» and the losses minus the bonuses equal the damage before defence; the
    /// vulnerability bar has its own colour and the «+N от уязвимости» caption; the note about 100% is shown.
    /// </summary>
    private async Task MeterMitigationIdentity()
    {
        FightSegment? segment = null;
        foreach (var file in new[] { Thunderer, SparkHybrid })
        {
            segment = await ShortSegment(file, 5);
            if (segment.Mitigation().Any(r => r.Key == "vulnerability")) break;
        }
        var rows = segment!.Mitigation();
        var before = segment.MitigationBySkill().Values.Sum(p => p.BeforeDefense);
        var losses = rows.Where(r => !r.IsBonus).Sum(r => r.Value);
        var bonus = rows.Where(r => r.IsBonus).Sum(r => r.Value);
        Check(Near(losses - bonus, before, 1e-6 * Math.Max(1, before)), $"дошло + потери {losses:R} − прибавки {bonus:R} ≠ до защиты {before:R}");
        Check(Near(rows.Single(r => r.Key == "final").Value, segment.TotalDamage, 1e-6 * Math.Max(1, segment.TotalDamage)), "«дошло» не равно урону сегмента");
        var vulnerability = rows.FirstOrDefault(r => r.Key == "vulnerability");
        Check(vulnerability != null, "ни у громовержца, ни у искры нет прибавки от уязвимости");

        var meter = Meter;
        _main.CloseAllWindows();
        _main.OpenWindow(WindowIds.Meter);
        meter.SelectSegment(null);
        meter.SetMode(MeterMode.Mitigation);
        meter.Refresh();
        await Settle();
        Check(meter.ShownRows[0].Key == MeterWindow.BeforeKey && meter.Bars.FillOf(meter.ShownRows[0]) == MeterColors.Before,
            "первая полоса — не нейтральная «до защиты»");
        var final = meter.ShownRows.Single(r => r.Key == "final");
        Check(final.Share <= 1 || MeterBars.ValueText(final, MeterMode.Mitigation).Contains('×'), $"у «дошло» выше 100% нет множителя: «{MeterBars.ValueText(final, MeterMode.Mitigation)}»");
        var shownBonus = meter.ShownRows.Single(r => r.Key == "vulnerability");
        Check(shownBonus.IsBonus && shownBonus.Label.StartsWith('+') && shownBonus.Label.EndsWith("от уязвимости", StringComparison.Ordinal), $"подпись прибавки «{shownBonus.Label}»");
        var bonusFill = meter.Bars.FillOf(shownBonus);
        Check(meter.ShownRows.Where(r => !r.IsBonus).All(r => meter.Bars.FillOf(r) != bonusFill), "полоса уязвимости того же цвета, что потери");
        var total = segment.DefenceTotal;
        Check(Near(total.TakenBonus, shownBonus.Value, 1e-9 * Math.Max(1, total.TakenBonus)), "прибавка от уязвимости не та, что в DefenceTotal");
        var bySkill = segment.DefenceBySkill().Values;
        Check(Near(bySkill.Sum(s => s.TakenBonus), total.TakenBonus, 1e-6 * Math.Max(1, total.TakenBonus))
              && Near(bySkill.Sum(s => s.TakenLoss), total.TakenLoss, 1e-6 * Math.Max(1, total.TakenLoss)), "раскладка по скиллам не складывается в общую");
        Check(meter.HintText.Contains("100"), $"нет подписи, что проценты не дают 100: «{meter.HintText}»");
        Check(MeterBars.ValueText(shownBonus).StartsWith('+'), "число прибавки без «+»");
        GD.Print($"SELFTEST NOTE потеря на защите ({segment.Label}): до защиты {before:0}, " +
                 string.Join(", ", rows.Select(r => $"{r.Label} {r.Value:0} ({r.Share * 100:0.#}%)")) + $"; Σ потерь − прибавки − до защиты = {losses - bonus - before:0.######}");
    }

    /// <summary>The charts of the meter's segment: the total line adds up to the segment, every skill has a line, the bars are filled.</summary>
    private async Task ChartsSeries()
    {
        var charts = Charts;
        var meter = Meter;
        _main.OpenWindow(WindowIds.Meter);
        meter.SelectSegment(null);
        // «Графики» in the meter opens the window.
        var button = meter.FindChild("Charts", true, false) as Button ?? throw new Failure("нет кнопки «Графики»");
        button.EmitSignal(BaseButton.SignalName.Pressed);
        await Settle();
        Check(_main.IsOpen(WindowIds.Charts), "«Графики» не открыли окно графиков");
        charts.Refresh();
        await Settle();
        var segment = meter.SelectedSegment ?? throw new Failure("нет сегмента");
        Check(charts.Segment == segment, "графики не того сегмента, что в измерителе");
        var total = charts.Lines.FirstOrDefault(l => l.Key == ChartsWindow.TotalKey) ?? throw new Failure("нет линии общего урона");
        Check(total.Points.Count > 0 && Near(total.Points.Sum(p => p.Damage), segment.TotalDamage, 1e-6 * Math.Max(1, segment.TotalDamage)),
            $"линия общего урона: точек {total.Points.Count}, сумма {total.Points.Sum(p => p.Damage):R}, урон {segment.TotalDamage:R}");
        var skills = segment.DamageBySkill();
        Check(charts.Lines.Count == 1 + Math.Min(skills.Count, ChartsWindow.MaxSkillLines), $"линий {charts.Lines.Count} при {skills.Count} скиллах");
        foreach (var line in charts.Lines.Skip(1))
            Check(line.Points.Count == total.Points.Count && line.Points.Sum(p => p.Damage) > 0, $"линия «{line.Label}» пуста");
        Check(charts.DpsBars.Count == skills.Count && charts.DpsBars.Count > 0, "нет столбиков DPS");
        Check(charts.TypeBars.Count > 0, "нет долей урона по типам");
        Check(charts.DefenceBars.Count == skills.Count && Near(charts.DefenceBars.Sum(b => b.Split.Final), segment.TotalDamage, 1e-6 * Math.Max(1, segment.TotalDamage)),
            "столбики потери на защите не сходятся с уроном");
        Check(charts.Canvas.CustomMinimumSize.Y > 0, "холст графиков пуст");
        GD.Print($"SELFTEST NOTE графики: линий {charts.Lines.Count}, точек {total.Points.Count}, столбиков DPS {charts.DpsBars.Count}, типов {charts.TypeBars.Count}, " +
                 $"потеря на защите {charts.DefenceBars.Count}, высота {charts.Canvas.CustomMinimumSize.Y:0}");
        _main.CloseAllWindows();
    }

    /// <summary>
    /// Takes a passive off by a click in the P window, one that changes the build's damage: tries the build's notables, then any
    /// node; returns its id, or null when none can go. <paramref name="changed"/> checks the damage after the click.
    /// </summary>
    private async Task<string?> DropPassive(Func<Task<bool>> changed)
    {
        var session = _main.Session!;
        var nodes = session.Build.PassiveNodes
            .Select(id => session.Data.Passives.Nodes[id])
            .OrderBy(n => n.Kind == PassiveKind.Notable ? 0 : 1).ThenBy(n => n.Id, StringComparer.Ordinal)
            .Select(n => n.Id).ToList();
        var tried = 0;
        foreach (var id in nodes)
        {
            if (tried >= 8) break;
            bool dropped;
            try { dropped = await ClickPassive(id); }
            catch (Failure) { continue; } // the click landed on a neighbour: another node
            if (!dropped) continue;
            tried++;
            _main.CloseAllWindows();
            if (await changed()) return id;
            Check(await ClickPassive(id), $"узел {id} не взят обратно");
            _main.CloseAllWindows();
        }
        return null;
    }

    /// <summary>
    /// One segment from a fresh fight: the fight has just restarted (the edit, or <see cref="TrainingRoom.SetMode"/> «среднее»), so
    /// resource, cooldowns and buffs start from zero; the hero at the same spot, the same presses for the same game time, the driver
    /// stepped by hand. Two such segments of one build are equal; only the build can make them differ.
    /// </summary>
    private async Task<FightSegment> FreshSegment(bool restart)
    {
        if (restart) Meter.SetSimMode(SimMode.Average, 1);
        Check(Training.Mode == SimMode.Average && Training.State is { Time: 0 }, $"бой не с нуля: режим {Training.Mode}, время {Training.State?.Time}");
        Driver.SetProcess(false);
        Driver.ResetVisuals();
        PlaceNearDummy();
        var focusLosses = _main.FocusLossEvents;
        await Fight(3, close: true);
        var segment = Training.Segments[^1];
        GD.Print($"SELFTEST NOTE fresh segment: damage {segment.TotalDamage:R}, dps {segment.Dps:R}, focus losses {_main.FocusLossEvents - focusLosses}");
        return Training.Segments[^1];
    }

    /// <summary>
    /// Spec §10.5 and §6.4: a fresh segment, a control (the same fresh fight again gives the same DPS to the bit), then a passive
    /// taken off in the P window and the same fresh fight: the new segment is labelled «после правки билда», its DPS differs (mode
    /// «среднее», same presses and start state, so only the build differs) and the meter shows «DPS ±N%».
    /// </summary>
    private async Task MeterSegmentAfterBuildEdit()
    {
        await LoadForCombat(WarriorMelee);
        var meter = Meter;
        await FreshSegment(restart: true); // the first segment after the load is «после правки билда» too
        var control = await FreshSegment(restart: true);
        var before = await FreshSegment(restart: true);
        Check(!before.AfterBuildChange, $"сегмент без правки подписан «{before.Label}»");
        Check(before.Dps == control.Dps && before.TotalDamage == control.TotalDamage, $"без правки DPS разный: {control.Dps:R} и {before.Dps:R}");
        FightSegment? after = null;
        var dropped = await DropPassive(async () =>
        {
            Check(Training.Mode == SimMode.Average, "режим сбросился после правки билда");
            after = await FreshSegment(restart: false); // the edit itself restarted the fight
            return after.Dps != before.Dps;
        });
        Check(dropped != null && after != null, "ни один узел пассивок не снимается с изменением DPS");
        try
        {
            Check(after!.AfterBuildChange && after.Label.EndsWith("после правки билда", StringComparison.Ordinal), $"подпись нового сегмента «{after.Label}»");
            Check(after.DpsChangeVsPrevious is { } change && Near(change, after.Dps / before.Dps - 1, 1e-9), "нет разницы DPS с прошлым сегментом");
            _main.OpenWindow(WindowIds.Meter);
            meter.SelectSegment(null);
            meter.Refresh();
            await Settle();
            var expected = CombatLogText.DpsChange(after.DpsChangeVsPrevious!.Value);
            Check(meter.ChangeText.StartsWith(expected, StringComparison.Ordinal), $"рядом с подписью «{meter.ChangeText}», а не «{expected}»");
            Check(meter.SegmentOption.GetItemText(0) == MeterWindow.SegmentText(after) && meter.SegmentOption.GetItemText(0).Contains(expected),
                $"в списке сегментов «{meter.SegmentOption.GetItemText(0)}»");
            meter.SelectSegment(before.Index);
            meter.Refresh();
            Check(meter.SelectedSegment == before && meter.SegmentOption.Selected == meter.SegmentChoices.ToList().IndexOf(before.Index), "старый сегмент не выбирается");
            GD.Print($"SELFTEST NOTE правка билда: контроль без правки DPS {control.Dps:0.##} = {before.Dps:0.##}; снят {dropped}; «{before.Label}» DPS {before.Dps:0.##} → «{after.Label}» DPS {after.Dps:0.##} ({expected})");
            meter.SelectSegment(null);
        }
        finally
        {
            Check(await ClickPassive(dropped!), $"узел {dropped} не взят обратно");
            _main.CloseAllWindows();
            meter.SetSimMode(SimMode.Roll, 1);
        }
    }

    /// <summary>«Сбросить» asks first: «Отмена» keeps the segments, «Да, сбросить» clears them and the meter says there was no fight.</summary>
    private async Task MeterResetConfirm()
    {
        var meter = Meter;
        _main.OpenWindow(WindowIds.Meter);
        await Settle();
        if (Training.Segments.Count == 0)
        {
            PlaceNearDummy();
            await Fight(1, close: false);
        }
        var count = Training.Segments.Count;
        Check(count > 0, "сегментов нет");
        var reset = meter.FindChild("Reset", true, false) as Button ?? throw new Failure("нет кнопки «Сбросить»");
        reset.EmitSignal(BaseButton.SignalName.Pressed);
        await Settle();
        Check(meter.ConfirmingReset && Training.Segments.Count == count, "«Сбросить» не спросил подтверждение или сбросил сразу");
        (meter.FindChild("No", true, false) as Button ?? throw new Failure("нет «Отмена»")).EmitSignal(BaseButton.SignalName.Pressed);
        await Settle();
        Check(!meter.ConfirmingReset && Training.Segments.Count == count, "«Отмена» сбросила сегменты");
        reset.EmitSignal(BaseButton.SignalName.Pressed);
        (meter.FindChild("Yes", true, false) as Button ?? throw new Failure("нет «Да, сбросить»")).EmitSignal(BaseButton.SignalName.Pressed);
        await Settle();
        meter.Refresh();
        Check(Training.Segments.Count == 0 && Training.Current == null && meter.ShownRows.Count == 0 && meter.SegmentChoices.Count == 0, "после «Да» сегменты остались");
        Check(Training.Log.Count > 0, "сброс измерителя очистил журнал");
        _main.CloseAllWindows();
    }

    // ---------- screenshots ----------

    /// <summary>
    /// Screenshots for the UI review (window run only): the meter in each mode mid-fight on a shocking build (the vulnerability bar
    /// in «Потеря на защите»), the log with the kind filter open, the charts, the segment list with labels and «DPS ±N%», the reset
    /// question.
    /// </summary>
    private async Task MeterScreenshots()
    {
        var meter = Meter;
        try
        {
            await LoadForCombat(Thunderer);
            meter.ConfirmReset(); // only this run's segments in the list
            var room = Room;
            room.PlaceHero(room.Dummy.TargetPosition(0) + new Vector3(1, 0, -1).Normalized() * 1.3f + TowardsCamera * 0.4f);
            room.CameraRig.ResetZoom();
            // Two closed segments (the second after a passive edit), then a running one: the list has labels and «DPS ±N%».
            await Fight(3, close: true);
            await Fight(3, close: true);
            var dropped = await DropPassive(async () =>
            {
                Driver.SetProcess(false);
                room.PlaceHero(room.Dummy.TargetPosition(0) + new Vector3(1, 0, -1).Normalized() * 1.3f + TowardsCamera * 0.4f);
                await Fight(3, close: true);
                return true;
            });
            await Fight(4, close: false);
            Check(Training.Current != null, "бой не идет");

            _main.CloseAllWindows();
            _main.OpenWindow(WindowIds.Meter);
            _main.OpenWindow(WindowIds.Log);
            meter.SelectSegment(null);
            foreach (var (mode, name) in new[] { (MeterMode.DpsBySkill, "dps"), (MeterMode.DamageBySkill, "damage"), (MeterMode.DamageByType, "types"), (MeterMode.Mitigation, "mitigation") })
            {
                meter.SetMode(mode);
                meter.Refresh();
                LogWindow.Refresh();
                await Shot($"task-11-meter-{name}");
                var s = LogWindow.Scroll!;
                GD.Print($"SELFTEST NOTE журнал в кадре {name}: прокрутка {s.Value:0}/{s.MaxValue:0} страница {s.Page:0}, следит {LogWindow.FollowTail}, внизу {LogWindow.AtBottom()}, " +
                         $"последняя строка «{LogWindow.ShownEntries[^1].Text}», tree.scroll {LogWindow.Lines.GetScroll()}");
            }
            GD.Print("SELFTEST NOTE потеря на защите в кадре: " + string.Join(", ", meter.ShownRows.Select(r => r.Label)));

            LogWindow.Refresh();
            LogWindow.KindsMenu.ShowPopup();
            await Shot("task-11-log-filters");
            LogWindow.KindsMenu.GetPopup().Hide();

            meter.SetMode(MeterMode.DpsBySkill);
            meter.SegmentOption.ShowPopup();
            await Shot("task-11-segments");
            meter.SegmentOption.GetPopup().Hide();

            meter.RequestReset();
            await Shot("task-11-reset-confirm");
            meter.CancelReset();

            _main.CloseWindow(WindowIds.Log);
            _main.OpenWindow(WindowIds.Charts);
            Charts.Refresh();
            await Shot("task-11-charts");
            Charts.ScrollToEnd();
            await Shot("task-11-charts-defence");
            if (dropped != null) Check(await ClickPassive(dropped), $"узел {dropped} не взят обратно");

            // The log on «Рой»: target numbers in the lines and a collapsed refusal «×6» (task 12 acceptance needs the frame).
            await LoadForCombat(WarriorMelee);
            Driver.SetDummy("swarm");
            Driver.SetProcess(false);
            PlaceNearDummy();
            await Fight(2.5, close: false);
            var charge = SlotOf("charge");
            await Run(3, _ => { if (Training.State is { Busy: false } s && !s.CooldownLeftBySlot.ContainsKey(charge)) Driver.PressSlot(charge); },
                until: () => Training.State is { Busy: false } s && s.CooldownLeftBySlot.ContainsKey(charge));
            for (var i = 0; i < 6; i++) Driver.PressSlot(charge);
            await Run(FrameSec * 2);
            Check(Training.Log.Any(e => e.Kind == LogKind.Refusal && e.Repeat >= 6), "схлопнутого отказа ×6 нет в журнале");
            Check(Training.Log.Any(e => e.Text.Contains("цель ", StringComparison.Ordinal)), "на «Рое» в журнале нет номера цели");
            _main.CloseAllWindows();
            _main.OpenWindow(WindowIds.Log);
            LogWindow.Refresh();
            await Shot("task-11-log-swarm");
        }
        finally
        {
            meter.SetMode(MeterMode.DpsBySkill);
            _main.CloseAllWindows();
            await CombatRestore();
        }
    }
}
