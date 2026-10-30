using Dialoid.Core.Combat;
using Dialoid.Core.Explain;
using Dialoid.Sandbox;
using Godot;

namespace Dialoid.Game.Ui;

/// <summary>One line of the damage-over-time chart: the whole segment or one skill, final damage per bucket.</summary>
public sealed record ChartLine(string Key, string Label, Color Color, IReadOnlyList<(double Time, double Damage)> Points);

/// <summary>One skill's defence split for the chart: damage before defence and what each step took (bonuses as positive numbers).</summary>
public sealed record DefenceBar(string SkillId, string Label, Color Color, DamagePart Part)
{
    public double Losses => Part.Final + Part.ByArmor + Math.Max(0, Part.ByResist) + Math.Max(0, Part.ByTakenMultiplier) + Part.Nullified;
    public double Vulnerability => Math.Max(0, -Part.ByTakenMultiplier);
    public double Penetration => Math.Max(0, -Part.ByResist);
}

/// <summary>
/// The «Графики» window (spec §8.2, plan 4 task 11) for the segment the meter shows: damage over time in
/// <see cref="BucketSec"/> buckets (the whole segment and each skill, lines), DPS by skill (bars), damage by type (bars with
/// shares) and the defence split of each skill (stacked bars «дошло / броня / сопротивления / множитель / обнулено», the
/// vulnerability and penetration bonuses as a violet / teal strip under the stack). All data is the sandbox's
/// (<see cref="FightSegment.DamageOverTime"/>, <see cref="FightSegment.DpsBySkill"/>, <see cref="FightSegment.DamageByType"/>,
/// <see cref="FightSegment.MitigationBySkill"/>); everything is drawn on <see cref="Control._Draw"/>, numbers in
/// <see cref="RuNumber.Short"/>. Refreshed at most every <see cref="RefreshSec"/> while open.
/// </summary>
public partial class ChartsWindow : SessionPanel
{
    public const double RefreshSec = 0.25;
    public const double BucketSec = 1;
    /// <summary>Most skill lines drawn (the top ones by damage); the total line is always drawn.</summary>
    public const int MaxSkillLines = 6;
    public const string TotalKey = "total";

    private TrainingRoom _room = null!;
    private Func<FightSegment?> _segment = null!;
    private Label _title = null!;
    private ChartsCanvas _canvas = null!;
    private bool _dirty = true;
    private bool _subscribed;
    private bool _wasVisible;
    private double _sinceRefresh = RefreshSec;

    /// <summary>The segment drawn at the last refresh.</summary>
    public FightSegment? Segment { get; private set; }

    public IReadOnlyList<ChartLine> Lines { get; private set; } = Array.Empty<ChartLine>();
    public IReadOnlyList<MeterRow> DpsBars { get; private set; } = Array.Empty<MeterRow>();
    public IReadOnlyList<MeterRow> TypeBars { get; private set; } = Array.Empty<MeterRow>();
    public IReadOnlyList<DefenceBar> DefenceBars { get; private set; } = Array.Empty<DefenceBar>();
    /// <summary>The build's skills in slot order (their colours).</summary>
    public IReadOnlyList<string> SkillOrder { get; private set; } = Array.Empty<string>();
    public string TitleText => _title.Text;
    public ChartsCanvas Canvas => _canvas;

    /// <param name="segment">The meter's shown segment (<see cref="MeterWindow.SelectedSegment"/>).</param>
    public void Setup(SandboxSession session, TrainingRoom room, Func<FightSegment?> segment)
    {
        InitSession(session, withContent: false);
        _room = room;
        _segment = segment;
        AddThemeConstantOverride("separation", 4);
        _title = new Label { Name = "Title", AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _title.AddThemeColorOverride("font_color", new Color(0.93f, 0.85f, 0.7f));
        AddChild(_title);
        var scroll = new ScrollContainer { Name = "Scroll", SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        AddChild(scroll);
        _canvas = new ChartsCanvas(this) { Name = "Canvas", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(_canvas);
    }

    public override void _EnterTree()
    {
        base._EnterTree();
        if (_room == null || _subscribed) return;
        _room.Updated += OnRoomUpdated;
        _subscribed = true;
        _dirty = true;
    }

    public override void _ExitTree()
    {
        base._ExitTree();
        if (!_subscribed) return;
        _room.Updated -= OnRoomUpdated;
        _subscribed = false;
    }

    private void OnRoomUpdated() => _dirty = true;

    protected override void OnSessionChanged() => _dirty = true;

    protected override void DrawContent() => Refresh();

    /// <summary>The meter picked another segment.</summary>
    public void MarkDirty()
    {
        _dirty = true;
        if (IsVisibleInTree()) RequestRedraw();
    }

    public override void _Process(double delta)
    {
        _sinceRefresh += delta;
        var visible = IsVisibleInTree();
        var opened = visible && !_wasVisible;
        _wasVisible = visible;
        if (visible && _dirty && (opened || _sinceRefresh >= RefreshSec)) Refresh();
    }

    /// <summary>Scrolls to the defence chart at the bottom (the screenshot run shows it).</summary>
    public void ScrollToEnd()
    {
        var scroll = GetNode<ScrollContainer>("Scroll");
        scroll.ScrollVertical = (int)scroll.GetVScrollBar().MaxValue;
    }

    /// <summary>Reads the segment's series now (the self-test calls it).</summary>
    public void Refresh()
    {
        _dirty = false;
        _sinceRefresh = 0;
        var segment = Segment = _segment();
        if (segment == null)
        {
            _title.Text = "Боя еще не было: графики появятся после первого удара по манекену.";
            Lines = Array.Empty<ChartLine>();
            DpsBars = TypeBars = Array.Empty<MeterRow>();
            DefenceBars = Array.Empty<DefenceBar>();
        }
        else
        {
            _title.Text = "Сегмент " + MeterWindow.SegmentText(segment) + " (выбирается в измерителе)";
            var order = SkillOrder = MeterColors.SkillOrder(Session);
            var lines = new List<ChartLine> { new(TotalKey, "Всего", MeterColors.Text, segment.DamageOverTime(BucketSec)) };
            foreach (var row in segment.DamageBySkill().Take(MaxSkillLines))
                lines.Add(new ChartLine(row.Key, row.Label, Lighter(MeterColors.Skill(row.Key, order)), segment.DamageOverTime(BucketSec, row.Key)));
            Lines = lines;
            DpsBars = segment.DpsBySkill();
            TypeBars = segment.DamageByType();
            var names = segment.DamageBySkill().ToDictionary(r => r.Key, r => r.Label);
            DefenceBars = segment.MitigationBySkill()
                .Select(kv => new DefenceBar(kv.Key, names.GetValueOrDefault(kv.Key, kv.Key), MeterColors.Skill(kv.Key, order), kv.Value))
                .OrderByDescending(b => b.Part.BeforeDefense).ToList();
        }
        _canvas.Relayout();
    }

    public static Color Lighter(Color c) => c.Lerp(Colors.White, 0.25f);
}

/// <summary>The four charts of <see cref="ChartsWindow"/>, stacked, drawn on <see cref="Control._Draw"/>.</summary>
public partial class ChartsCanvas : Control
{
    public const float SectionGap = 14;
    public const float TitleHeight = 24;
    public const float LineChartHeight = 170;
    public const float AxisWidth = 86;
    public const float BarHeight = 22;
    public const float DefenceRowHeight = 68;

    private static readonly Color Axis = new(0.45f, 0.42f, 0.38f);
    private static readonly Color Grid = new(0.22f, 0.21f, 0.2f);
    private static readonly Color Caption = new(0.93f, 0.85f, 0.7f);
    private static readonly Color Muted = new(0.7f, 0.67f, 0.62f);

    private readonly ChartsWindow _owner;

    public ChartsCanvas(ChartsWindow owner)
    {
        _owner = owner;
        MouseFilter = MouseFilterEnum.Pass;
        Resized += Relayout;
    }

    /// <summary>Recomputes the height the charts need and redraws.</summary>
    public void Relayout()
    {
        CustomMinimumSize = new Vector2(0, TotalHeight());
        QueueRedraw();
    }

    private (Font Font, int Size) Font() => (GetThemeDefaultFont(), GetThemeDefaultFontSize());

    private float LegendHeight(IEnumerable<string> labels)
    {
        var (font, size) = Font();
        float x = 0, rows = 1;
        foreach (var label in labels)
        {
            var w = 18 + font.GetStringSize(label, HorizontalAlignment.Left, -1, size).X + 14;
            if (x > 0 && x + w > Size.X) { rows++; x = 0; }
            x += w;
        }
        return rows * 22;
    }

    private static readonly string[] DefenceLegend = ["дошло", "броня", "сопротивления", "множитель", "обнулено", "+ уязвимость", "+ пробивание"];

    private float TotalHeight()
    {
        if (_owner.Segment == null) return 0;
        return TitleHeight + LineChartHeight + 26 + LegendHeight(_owner.Lines.Select(l => l.Label)) + SectionGap
               + TitleHeight + _owner.DpsBars.Count * BarHeight + SectionGap
               + TitleHeight + _owner.TypeBars.Count * BarHeight + SectionGap
               + TitleHeight + LegendHeight(DefenceLegend) + _owner.DefenceBars.Count * DefenceRowHeight + SectionGap;
    }

    public override void _Draw()
    {
        if (_owner.Segment == null) return;
        var y = 0f;
        y = DrawLines(y) + SectionGap;
        y = DrawBars(y, "DPS по скиллам (урон за сегмент / длительность)", _owner.DpsBars, r => MeterColors.Skill(r.Key, _owner.SkillOrder), false) + SectionGap;
        y = DrawBars(y, "Урон по типам (доля от урона сегмента)", _owner.TypeBars, r => MeterColors.Type(r.Key), true) + SectionGap;
        DrawDefence(y);
    }

    private void Text(string text, Vector2 at, Color color, HorizontalAlignment align = HorizontalAlignment.Left, float width = -1)
    {
        var (font, size) = Font();
        DrawString(font, at + new Vector2(1, 1), text, align, width, size, MeterColors.TextShadow);
        DrawString(font, at, text, align, width, size, color);
    }

    private float Baseline(float top, float height)
    {
        var (font, size) = Font();
        return top + (height + font.GetAscent(size) - font.GetDescent(size)) / 2;
    }

    private float DrawLines(float y)
    {
        Text("Урон по времени (за каждую секунду сегмента)", new Vector2(0, Baseline(y, TitleHeight)), Caption);
        y += TitleHeight;
        var lines = _owner.Lines;
        var plot = new Rect2(AxisWidth, y + 6, Mathf.Max(10, Size.X - AxisWidth - 10), LineChartHeight - 12);
        var max = lines.SelectMany(l => l.Points).Select(p => p.Damage).DefaultIfEmpty(0).Max();
        if (max <= 0) max = 1;
        var count = lines.Count > 0 ? lines[0].Points.Count : 0;
        // Grid: 0, half, max.
        for (var k = 0; k <= 2; k++)
        {
            var gy = plot.End.Y - plot.Size.Y * k / 2f;
            DrawLine(new Vector2(plot.Position.X, gy), new Vector2(plot.End.X, gy), k == 0 ? Axis : Grid, 1);
            Text(RuNumber.Short(max * k / 2), new Vector2(0, gy + 6), Muted, HorizontalAlignment.Right, AxisWidth - 6);
        }
        DrawLine(plot.Position, new Vector2(plot.Position.X, plot.End.Y), Axis, 1);
        float X(int i) => count <= 1 ? plot.Position.X + plot.Size.X / 2 : plot.Position.X + plot.Size.X * i / (count - 1);
        float Y(double v) => plot.End.Y - (float)(v / max) * plot.Size.Y;
        // The total last so it stays on top.
        foreach (var line in lines.Skip(1).Append(lines.FirstOrDefault()).OfType<ChartLine>())
        {
            var width = line.Key == ChartsWindow.TotalKey ? 2.5f : 1.6f;
            if (line.Points.Count == 1)
            {
                DrawCircle(new Vector2(X(0), Y(line.Points[0].Damage)), 3, line.Color);
                continue;
            }
            var pts = line.Points.Select((p, i) => new Vector2(X(i), Y(p.Damage))).ToArray();
            DrawPolyline(pts, line.Color, width, true);
        }
        // Time axis: 0 с, the middle, the end.
        var axisY = plot.End.Y + 20;
        if (count > 0)
        {
            var span = (count - 1) * ChartsWindow.BucketSec;
            Text("0 с", new Vector2(plot.Position.X - 4, axisY), Muted);
            if (count > 2) Text($"{RuNumber.Format(span / 2, "0.#")} с", new Vector2(X((count - 1) / 2) - 14, axisY), Muted);
            Text($"{RuNumber.Format(span, "0.#")} с", new Vector2(plot.Position.X, axisY), Muted, HorizontalAlignment.Right, plot.Size.X);
        }
        y += LineChartHeight + 26;
        return DrawLegend(y, lines.Select(l => (l.Label, l.Color)));
    }

    private float DrawLegend(float y, IEnumerable<(string Label, Color Color)> items)
    {
        var (font, size) = Font();
        float x = 0;
        foreach (var (label, color) in items)
        {
            var w = 18 + font.GetStringSize(label, HorizontalAlignment.Left, -1, size).X + 14;
            if (x > 0 && x + w > Size.X) { y += 22; x = 0; }
            DrawRect(new Rect2(x, y + 6, 12, 12), color);
            Text(label, new Vector2(x + 18, Baseline(y, 22)), Muted);
            x += w;
        }
        return y + 22;
    }

    private float DrawBars(float y, string title, IReadOnlyList<MeterRow> rows, Func<MeterRow, Color> color, bool share)
    {
        Text(title, new Vector2(0, Baseline(y, TitleHeight)), Caption);
        y += TitleHeight;
        var max = rows.Select(r => r.Value).DefaultIfEmpty(0).Max();
        foreach (var row in rows)
        {
            DrawRect(new Rect2(0, y + 1, Size.X, BarHeight - 2), MeterColors.Track);
            if (max > 0) DrawRect(new Rect2(0, y + 1, (float)(row.Value / max) * Size.X, BarHeight - 2), color(row));
            var baseline = Baseline(y, BarHeight);
            Text(row.Label, new Vector2(6, baseline), MeterColors.Text);
            var value = RuNumber.Short(row.Value) + (share ? " · " + RuNumber.Format(row.Share * 100, "0.#") + "%" : "");
            Text(value, new Vector2(6, baseline), MeterColors.Text, HorizontalAlignment.Right, Size.X - 12);
            y += BarHeight;
        }
        return y;
    }

    private void DrawDefence(float y)
    {
        Text("Потеря на защите по скиллам", new Vector2(0, Baseline(y, TitleHeight)), Caption);
        y += TitleHeight;
        y = DrawLegend(y, DefenceLegend.Zip(new[]
        {
            MeterColors.Final, MeterColors.Armor, MeterColors.Resist, MeterColors.Taken, MeterColors.Nullified, MeterColors.Vulnerability, MeterColors.Penetration,
        }, (l, c) => (l, c)));
        var bars = _owner.DefenceBars;
        var max = bars.Select(b => Math.Max(b.Losses, b.Part.BeforeDefense)).DefaultIfEmpty(0).Max();
        foreach (var bar in bars)
        {
            var p = bar.Part;
            Text(bar.Label, new Vector2(0, Baseline(y, 20)), MeterColors.Text);
            var bonus = bar.Vulnerability + bar.Penetration;
            Text($"до защиты {RuNumber.Short(p.BeforeDefense)} · дошло {RuNumber.Short(p.Final)}" + (bonus > 0 ? $" · прибавки +{RuNumber.Short(bonus)}" : ""),
                new Vector2(0, Baseline(y + 20, 20)), Muted);
            var top = y + 42;
            DrawRect(new Rect2(0, top, Size.X, 14), MeterColors.Track);
            var x = 0f;
            if (max > 0)
                foreach (var (value, color) in new[]
                         {
                             (p.Final, MeterColors.Final), (p.ByArmor, MeterColors.Armor), (Math.Max(0, p.ByResist), MeterColors.Resist),
                             (Math.Max(0, p.ByTakenMultiplier), MeterColors.Taken), (p.Nullified, MeterColors.Nullified),
                         })
                {
                    var w = (float)(value / max) * Size.X;
                    if (w <= 0) continue;
                    DrawRect(new Rect2(x, top, w, 14), color);
                    x += w;
                }
            // Bonuses: a strip under the stack, as long as the damage they added.
            var bx = 0f;
            foreach (var (value, color) in new[] { (bar.Vulnerability, MeterColors.Vulnerability), (bar.Penetration, MeterColors.Penetration) })
            {
                if (value <= 0 || max <= 0) continue;
                var w = (float)(value / max) * Size.X;
                DrawRect(new Rect2(bx, top + 16, Mathf.Max(2, w), 5), color);
                bx += w;
            }
            y += DefenceRowHeight;
        }
    }
}
