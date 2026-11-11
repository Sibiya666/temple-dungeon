using Dialoid.Core.Explain;
using Dialoid.Sandbox;
using Godot;
using Dialoid.Game.Ui.Art;

namespace Dialoid.Game.Ui;

/// <summary>One line of the damage-over-time chart: the whole segment or one skill, final damage per bucket.</summary>
public sealed record ChartLine(string Key, string Label, Color Color, IReadOnlyList<(double Time, double Damage)> Points);

/// <summary>One skill's defence split for the chart (the sandbox's per-hit <see cref="DefenceSplit"/>, not netted over the skill).</summary>
public sealed record DefenceBar(string SkillId, string Label, Color Color, DefenceSplit Split)
{
    /// <summary>The stack's length: what reached the dummy plus every loss (bonuses are drawn under it).</summary>
    public double Stack => Split.Final + Split.Armor + Split.ResistLoss + Split.TakenLoss + Split.Nullified;
}

/// <summary>
/// The «Графики» window (spec §8.2, plan 4 task 11) for the segment the meter shows: damage over time in
/// <see cref="BucketSec"/> buckets (the whole segment and each skill, lines), DPS by skill (bars), damage by type (bars with
/// shares) and the defence split of each skill (stacked bars «дошло / броня / сопротивления / множитель / обнулено», the
/// vulnerability and penetration bonuses as a violet / teal strip under the stack). All data is the sandbox's
/// (<see cref="FightSegment.DamageOverTime"/>, <see cref="FightSegment.DpsBySkill"/>, <see cref="FightSegment.DamageByType"/>,
/// <see cref="FightSegment.DefenceBySkill"/>); everything is drawn on <see cref="Control._Draw"/> from geometry laid out on refresh
/// and resize (the draw itself allocates nothing), numbers in <see cref="RuNumber.Short"/>. Refreshed at most every
/// <see cref="RefreshSec"/> while open.
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
        Theme = RoomHudTheme.TowerWindowTheme();
        AddThemeConstantOverride("separation", RoomHudTheme.GroupGap);
        _title = new Label { Name = "Title", AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _title.AddThemeColorOverride("font_color", RoomHudTheme.MutedText);
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
            var bySkill = segment.DamageBySkill();
            var lines = new List<ChartLine> { new(TotalKey, "Всего", MeterColors.Text, segment.DamageOverTime(BucketSec)) };
            foreach (var row in bySkill.Take(MaxSkillLines))
                lines.Add(new ChartLine(row.Key, row.Label, Lighter(MeterColors.Skill(row.Key, order)), segment.DamageOverTime(BucketSec, row.Key)));
            Lines = lines;
            DpsBars = segment.DpsBySkill();
            TypeBars = segment.DamageByType();
            var names = bySkill.ToDictionary(r => r.Key, r => r.Label);
            DefenceBars = segment.DefenceBySkill()
                .Select(kv => new DefenceBar(kv.Key, names.GetValueOrDefault(kv.Key, kv.Key), MeterColors.Skill(kv.Key, order), kv.Value))
                .OrderByDescending(b => b.Split.BeforeDefense).ToList();
        }
        _canvas.Relayout();
    }

    public static Color Lighter(Color c) => c.Lerp(Colors.White, 0.25f);
}

/// <summary>
/// The four charts of <see cref="ChartsWindow"/>, stacked. <see cref="Relayout"/> (on refresh and resize) turns the data into
/// rectangles, polylines and strings; <see cref="_Draw"/> only paints them.
/// </summary>
public partial class ChartsCanvas : Control
{
    public const float SectionGap = 14;
    public const float TitleHeight = 24;
    public const float LineChartHeight = 170;
    public const float AxisWidth = 86;
    public const float BarHeight = 22;
    public const float DefenceRowHeight = 68;
    public const float LegendRow = 22;

    private static readonly Color Axis = new("71858c");
    private static readonly Color Grid = new("293b42");
    private static readonly Color Caption = new("d8d5c6");
    private static readonly Color Muted = new("9baeb3");

    private static readonly (string Label, Color Color)[] DefenceLegend =
    [
        ("дошло", MeterColors.Final), ("броня", MeterColors.Armor), ("сопротивления", MeterColors.Resist), ("множитель", MeterColors.Taken),
        ("обнулено", MeterColors.Nullified), ("+ уязвимость", MeterColors.Vulnerability), ("+ пробивание", MeterColors.Penetration),
    ];

    private readonly ChartsWindow _owner;

    // The laid-out picture.
    private readonly List<(Rect2 Rect, Color Color, bool Filled)> _rects = new();
    private readonly List<(Vector2 At, string Text, Color Color, HorizontalAlignment Align, float Width)> _texts = new();
    private readonly List<(Vector2[] Points, Color Color, float Width)> _polylines = new();
    private readonly List<(Vector2 From, Vector2 To, Color Color)> _segments = new();
    private readonly List<(Vector2 At, Color Color)> _dots = new();

    public ChartsCanvas(ChartsWindow owner)
    {
        _owner = owner;
        MouseFilter = MouseFilterEnum.Pass;
        Resized += Relayout;
    }

    private (Font Font, int Size) Font() => (GetThemeDefaultFont(), GetThemeDefaultFontSize());

    /// <summary>Lays the charts out for the current data and width, sets the height they need and redraws.</summary>
    public void Relayout()
    {
        _rects.Clear();
        _texts.Clear();
        _polylines.Clear();
        _segments.Clear();
        _dots.Clear();
        var height = 0f;
        if (_owner.Segment != null && Size.X > 1)
        {
            var y = LayLines(0) + SectionGap;
            if (Size.X >= 760)
            {
                var width = (Size.X - 32) / 2;
                var skillEnd = LayBars(y, "DPS ПО СКИЛЛАМ", _owner.DpsBars, r => MeterColors.Skill(r.Key, _owner.SkillOrder), false, 0, width);
                var typeEnd = LayBars(y, "УРОН ПО ТИПАМ", _owner.TypeBars, r => MeterColors.Type(r.Key), true, width + 32, width);
                y = Mathf.Max(skillEnd, typeEnd) + SectionGap * 2;
            }
            else
            {
                y = LayBars(y, "DPS по скиллам (урон / длительность)", _owner.DpsBars, r => MeterColors.Skill(r.Key, _owner.SkillOrder), false) + SectionGap;
                y = LayBars(y, "Урон по типам (доля от урона сегмента)", _owner.TypeBars, r => MeterColors.Type(r.Key), true) + SectionGap;
            }
            height = LayDefence(y) + SectionGap;
        }
        if (!Mathf.IsEqualApprox(CustomMinimumSize.Y, height)) CustomMinimumSize = new Vector2(0, height);
        QueueRedraw();
    }

    public override void _Draw()
    {
        var (font, size) = Font();
        foreach (var (rect, color, filled) in _rects) DrawRect(rect, color, filled, filled ? -1 : 1);
        foreach (var (from, to, color) in _segments) DrawLine(from, to, color, 1);
        foreach (var (points, color, width) in _polylines) DrawPolyline(points, color, width, true);
        foreach (var (at, color) in _dots) DrawCircle(at, 3, color);
        foreach (var (at, text, color, align, width) in _texts)
        {
            DrawString(font, at + new Vector2(1, 1), text, align, width, size, MeterColors.TextShadow);
            DrawString(font, at, text, align, width, size, color);
        }
    }

    private void Text(string text, Vector2 at, Color color, HorizontalAlignment align = HorizontalAlignment.Left, float width = -1) =>
        _texts.Add((at, text, color, align, width));

    private float Baseline(float top, float height)
    {
        var (font, size) = Font();
        return top + (height + font.GetAscent(size) - font.GetDescent(size)) / 2;
    }

    private float LayLines(float y)
    {
        var chartHeight = Size.X >= 760 ? 240 : LineChartHeight;
        _rects.Add((new Rect2(0, y, Size.X, chartHeight + TitleHeight + 26), new Color(.047f, .067f, .078f, .86f), true));
        Text("Урон по времени (за каждую секунду сегмента)", new Vector2(0, Baseline(y, TitleHeight)), Caption);
        y += TitleHeight;
        var lines = _owner.Lines;
        var plot = new Rect2(AxisWidth, y + 6, Mathf.Max(10, Size.X - AxisWidth - 10), chartHeight - 12);
        var max = 0.0;
        foreach (var line in lines)
            foreach (var p in line.Points)
                if (p.Damage > max) max = p.Damage;
        if (max <= 0) max = 1;
        var count = lines.Count > 0 ? lines[0].Points.Count : 0;
        for (var k = 0; k <= 2; k++)
        {
            var gy = plot.End.Y - plot.Size.Y * k / 2f;
            _segments.Add((new Vector2(plot.Position.X, gy), new Vector2(plot.End.X, gy), k == 0 ? Axis : Grid));
            Text(RuNumber.Short(max * k / 2), new Vector2(0, gy + 6), Muted, HorizontalAlignment.Right, AxisWidth - 6);
        }
        _segments.Add((plot.Position, new Vector2(plot.Position.X, plot.End.Y), Axis));
        float X(int i) => count <= 1 ? plot.Position.X + plot.Size.X / 2 : plot.Position.X + plot.Size.X * i / (count - 1);
        float Y(double v) => plot.End.Y - (float)(v / max) * plot.Size.Y;
        // Skill lines first, the total last so it stays on top.
        for (var n = 1; n <= lines.Count; n++)
        {
            var line = lines[n % lines.Count];
            if (line.Points.Count == 1)
            {
                _dots.Add((new Vector2(X(0), Y(line.Points[0].Damage)), line.Color));
                continue;
            }
            var points = new Vector2[line.Points.Count];
            for (var i = 0; i < points.Length; i++) points[i] = new Vector2(X(i), Y(line.Points[i].Damage));
            _polylines.Add((points, line.Color, line.Key == ChartsWindow.TotalKey ? 2.5f : 1.6f));
        }
        var axisY = plot.End.Y + 20;
        if (count > 0)
        {
            var span = (count - 1) * ChartsWindow.BucketSec;
            Text("0 с", new Vector2(plot.Position.X - 4, axisY), Muted);
            if (count > 2) Text($"{RuNumber.Format(span / 2, "0.#")} с", new Vector2(X((count - 1) / 2) - 14, axisY), Muted);
            Text($"{RuNumber.Format(span, "0.#")} с", new Vector2(plot.Position.X, axisY), Muted, HorizontalAlignment.Right, plot.Size.X);
        }
        y += chartHeight + 26;
        var legend = new List<(string, Color)>(lines.Count);
        foreach (var line in lines) legend.Add((line.Label, line.Color));
        return LayLegend(y, legend);
    }

    private float LayLegend(float y, IReadOnlyList<(string Label, Color Color)> items)
    {
        var (font, size) = Font();
        float x = 0;
        foreach (var (label, color) in items)
        {
            var w = 18 + font.GetStringSize(label, HorizontalAlignment.Left, -1, size).X + 14;
            if (x > 0 && x + w > Size.X) { y += LegendRow; x = 0; }
            _rects.Add((new Rect2(x, y + 6, 12, 12), color, true));
            Text(label, new Vector2(x + 18, Baseline(y, LegendRow)), Muted);
            x += w;
        }
        return y + LegendRow;
    }

    private float LayBars(float y, string title, IReadOnlyList<MeterRow> rows, Func<MeterRow, Color> color, bool share, float x = 0, float width = -1)
    {
        if (width < 0) width = Size.X;
        var rowHeight = Size.X >= 760 ? 36 : BarHeight;
        Text(title, new Vector2(x, Baseline(y, TitleHeight)), Caption, width: width);
        _segments.Add((new Vector2(x, y + TitleHeight), new Vector2(x + width, y + TitleHeight), Grid));
        y += TitleHeight + 8;
        var max = 0.0;
        foreach (var row in rows) if (row.Value > max) max = row.Value;
        foreach (var row in rows)
        {
            _rects.Add((new Rect2(x, y + 2, width, rowHeight - 4), MeterColors.Track, true));
            if (max > 0) _rects.Add((new Rect2(x, y + 2, (float)(row.Value / max) * width, rowHeight - 4), color(row), true));
            var baseline = Baseline(y, rowHeight);
            var value = RuNumber.Short(row.Value) + (share ? " · " + RuNumber.Format(row.Share * 100, "0.#") + "%" : "");
            var (font, size) = Font();
            var valueWidth = font.GetStringSize(value, HorizontalAlignment.Left, -1, size).X;
            Text(row.Label, new Vector2(x + 6, baseline), MeterColors.Text, width: Mathf.Max(0, width - valueWidth - 24));
            Text(value, new Vector2(x + 6, baseline), MeterColors.Text, HorizontalAlignment.Right, width - 12);
            y += rowHeight;
        }
        return y;
    }

    private float LayDefence(float y)
    {
        Text("Потеря на защите по скиллам", new Vector2(0, Baseline(y, TitleHeight)), Caption);
        y += TitleHeight;
        y = LayLegend(y, DefenceLegend);
        var bars = _owner.DefenceBars;
        var max = 0.0;
        foreach (var bar in bars) max = Math.Max(max, Math.Max(bar.Stack, bar.Split.BeforeDefense));
        foreach (var bar in bars)
        {
            var p = bar.Split;
            Text(bar.Label, new Vector2(0, Baseline(y, 20)), MeterColors.Text);
            var bonus = p.TakenBonus + p.ResistBonus;
            Text($"до защиты {RuNumber.Short(p.BeforeDefense)} · дошло {RuNumber.Short(p.Final)}" + (bonus > 0 ? $" · прибавки +{RuNumber.Short(bonus)}" : ""),
                new Vector2(0, Baseline(y + 20, 20)), Muted);
            var top = y + 42;
            _rects.Add((new Rect2(0, top, Size.X, 14), MeterColors.Track, true));
            if (max > 0)
            {
                var x = 0f;
                foreach (var (value, color) in new[]
                         {
                             (p.Final, MeterColors.Final), (p.Armor, MeterColors.Armor), (p.ResistLoss, MeterColors.Resist),
                             (p.TakenLoss, MeterColors.Taken), (p.Nullified, MeterColors.Nullified),
                         })
                {
                    var w = (float)(value / max) * Size.X;
                    if (w <= 0) continue;
                    _rects.Add((new Rect2(x, top, w, 14), color, true));
                    x += w;
                }
                // Bonuses: a strip under the stack, as long as the damage they added.
                var bx = 0f;
                foreach (var (value, color) in new[] { (p.TakenBonus, MeterColors.Vulnerability), (p.ResistBonus, MeterColors.Penetration) })
                {
                    if (value <= 0) continue;
                    var w = (float)(value / max) * Size.X;
                    _rects.Add((new Rect2(bx, top + 16, Mathf.Max(2, w), 5), color, true));
                    bx += w;
                }
            }
            y += DefenceRowHeight;
        }
        return y;
    }
}
