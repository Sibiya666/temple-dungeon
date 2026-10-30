using Dialoid.Core.Combat;
using Dialoid.Core.Explain;
using Dialoid.Core.Simulation;
using Dialoid.Game.Room;
using Dialoid.Sandbox;
using Godot;

namespace Dialoid.Game.Ui;

/// <summary>
/// The «Измеритель» window in the spirit of Details! (spec §8.1, §6.4; plan 4 task 11). Everything comes from the sandbox: the
/// segments of <see cref="TrainingRoom.Segments"/> (label «№4 · 0:42 · Бронированный · после правки билда», the running one marked
/// «●», «DPS +12%» against the one before), the bars of <see cref="FightSegment.Rows"/> in four modes (name, number in
/// <see cref="RuNumber.Short"/>, share), the vulnerability and penetration bonus bars in their own colour with the sandbox's
/// «+N от уязвимости» caption and the note that the shares need not add up to 100. Under the bars: «бросок / среднее», the seed
/// and «Сбросить» (asks first) of <see cref="TrainingRoom"/>, «Графики», and the floating numbers switch.
/// <para>The newest segment is followed until the owner picks an older one. A click on a skill bar opens «Разбор» of the skill's main
/// damage line (<see cref="MainDamageKey"/> of its <see cref="SandboxSession.SkillLines"/>), whose note gets the bar's own numbers:
/// the skill's DPS and damage in the segment and what the dummy's defence took. Redrawn at most every <see cref="RefreshSec"/>
/// while open; built once, updated in place (no control is freed).</para>
/// </summary>
public partial class MeterWindow : SessionPanel
{
    /// <summary>The meter refreshes four times a second (plan 4 task 11).</summary>
    public const double RefreshSec = 0.25;

    /// <summary>Key of a skill's main damage line in its SkillLines (the average hit with crits).</summary>
    public const string MainDamageKey = "hit_avg_with_crit";

    public const string NoFightText = "Боя еще не было: ударьте манекен, и здесь появятся полосы.";
    public const string SegmentsHint = "Сегмент боя: от первого удара до 5 с без ударов. Верхний — последний, за ним измеритель следит сам.";
    public const string ResetQuestion = "Сбросить измеритель? Все сохраненные сегменты будут удалены, журнал останется.";

    private static readonly MeterMode[] ModeOrder = [MeterMode.DpsBySkill, MeterMode.DamageBySkill, MeterMode.DamageByType, MeterMode.Mitigation];
    private static readonly SimMode[] SimModes = [SimMode.Roll, SimMode.Average];
    private static readonly Color Grey = new(0.62f, 0.6f, 0.56f);

    private TrainingRoom _room = null!;
    private Action<NumberLine, string?> _open = null!;
    private FloatingNumbers? _numbers;
    private Action _openCharts = null!;
    private readonly List<int> _segmentChoices = new(); // segment Index per item, newest first
    private int? _pinned;
    private bool _dirty = true;
    private bool _subscribed;
    private bool _wasVisible;
    private double _sinceRefresh = RefreshSec;

    private OptionButton _segments = null!;
    private Label _change = null!;
    private OptionButton _modes = null!;
    private Label _total = null!;
    private MeterBars _bars = null!;
    private Label _empty = null!;
    private Label _hint = null!;
    private OptionButton _simMode = null!;
    private SpinBox _seed = null!;
    private Button _reset = null!;
    private Button _charts = null!;
    private CheckBox _floating = null!;
    private VBoxContainer _confirm = null!;

    /// <summary>The bars' mode.</summary>
    public MeterMode Mode { get; private set; } = MeterMode.DpsBySkill;

    /// <summary>Raised when the shown segment or mode changed (the charts follow the meter's segment).</summary>
    public event Action? SelectionChanged;

    /// <summary>The segment shown: the picked one while it is kept, else the newest; null before the first fight.</summary>
    public FightSegment? SelectedSegment
    {
        get
        {
            var segments = _room.Segments;
            if (_pinned is { } index && segments.FirstOrDefault(s => s.Index == index) is { } pinned) return pinned;
            return segments.Count > 0 ? segments[^1] : null;
        }
    }

    /// <summary>The bars shown now (the selected segment's rows of <see cref="Mode"/> at the last refresh).</summary>
    public IReadOnlyList<MeterRow> ShownRows => _bars.Rows;

    public MeterBars Bars => _bars;
    public OptionButton SegmentOption => _segments;

    /// <summary>The segment Index of each item of <see cref="SegmentOption"/>, newest first.</summary>
    public IReadOnlyList<int> SegmentChoices => _segmentChoices;

    public string ChangeText => _change.Text;
    public string TotalText => _total.Text;
    public string HintText => _hint.Visible ? _hint.Text : "";
    public SpinBox Seed => _seed;
    public OptionButton SimModeOption => _simMode;
    public CheckBox FloatingSwitch => _floating;

    /// <summary>The «Сбросить» question is shown.</summary>
    public bool ConfirmingReset => _confirm.Visible;

    public void Setup(SandboxSession session, TrainingRoom room, Action<NumberLine, string?> open, FloatingNumbers? numbers, Action openCharts)
    {
        InitSession(session, withContent: false);
        _room = room;
        _open = open;
        _numbers = numbers;
        _openCharts = openCharts;
        Build();
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

    public override void _Process(double delta)
    {
        _sinceRefresh += delta;
        var visible = IsVisibleInTree();
        var opened = visible && !_wasVisible;
        _wasVisible = visible;
        if (visible && _dirty && (opened || _sinceRefresh >= RefreshSec)) Refresh();
    }

    // ---------- handlers (the controls and the self-test call these) ----------

    public void SetMode(MeterMode mode)
    {
        Mode = mode;
        var at = Array.IndexOf(ModeOrder, mode);
        if (_modes.Selected != at) _modes.Select(at);
        SelectionChanged?.Invoke();
        _dirty = true;
        RequestRedraw();
    }

    /// <summary>Shows the segment with this <see cref="FightSegment.Index"/>; the newest one (or null) follows the fight again.</summary>
    public void SelectSegment(int? index)
    {
        var segments = _room.Segments;
        _pinned = index is { } i && segments.Count > 0 && segments[^1].Index != i && segments.Any(s => s.Index == i) ? i : null;
        SelectionChanged?.Invoke();
        _dirty = true;
        RequestRedraw();
    }

    /// <summary>«бросок / среднее» and the seed: restarts the fight (the room's rule).</summary>
    public void SetSimMode(SimMode mode, int seed)
    {
        _room.SetMode(mode, seed);
        _dirty = true;
        RequestRedraw();
    }

    public void SetFloatingNumbers(bool on)
    {
        if (_numbers != null) _numbers.Enabled = on;
        _floating.SetPressedNoSignal(on);
    }

    /// <summary>«Сбросить»: asks first.</summary>
    public void RequestReset()
    {
        _confirm.Visible = true;
        _reset.Disabled = true;
    }

    public void CancelReset()
    {
        _confirm.Visible = false;
        _reset.Disabled = false;
    }

    /// <summary>The answer «да»: the room closes the current segment and forgets every segment (the log stays).</summary>
    public void ConfirmReset()
    {
        CancelReset();
        _pinned = null;
        _room.ResetMeter();
        SelectionChanged?.Invoke();
        _dirty = true;
        RequestRedraw();
    }

    /// <summary>A click on bar <paramref name="index"/>: in the skill modes opens the skill's breakdown; true when it did.</summary>
    public bool OpenRow(int index)
    {
        if (Mode is not (MeterMode.DpsBySkill or MeterMode.DamageBySkill)) return false;
        if (index < 0 || index >= _bars.Rows.Count || SelectedSegment is not { } segment) return false;
        var (line, context) = SkillLine(segment, _bars.Rows[index].Key);
        _open(line, context);
        return true;
    }

    /// <summary>
    /// What a skill bar opens: the skill's main damage line from the current build's SkillLines with the segment's numbers added to
    /// its note (DPS and damage with the dummy's defence, what each defence step took); a skill outside the slots (a triggered one)
    /// gets a line of its segment DPS with the same note. The context is the skill's name.
    /// </summary>
    public (NumberLine Line, string Context) SkillLine(FightSegment segment, string skillId)
    {
        var name = Session.Data.Skills.TryGetValue(skillId, out var skill) ? skill.Name : skillId;
        var damage = segment.DamageBySkill().FirstOrDefault(r => r.Key == skillId)?.Value ?? 0;
        var dps = damage / segment.Duration;
        var note = $"В сегменте {segment.Label}: DPS {RuNumber.Grouped(dps)} и урон {RuNumber.Grouped(damage)} с учетом защиты манекена.";
        if (segment.MitigationBySkill().TryGetValue(skillId, out var part)) note += " " + DefenceNote(part);
        NumberLine? main = null;
        for (var i = 0; i < SandboxSession.SlotCount && main == null; i++)
            if (Session.GetSlot(i)?.SkillId == skillId)
                main = Session.SkillLines(i).FirstOrDefault(l => l.Key == MainDamageKey);
        if (main == null) return (new NumberLine("meter_dps_" + skillId, "DPS за сегмент", dps, null, note), name);
        return (main with { Note = (main.Note is { } own ? own + ".\n" : "") + note + " Строка листа — по текущему билду." }, name);
    }

    /// <summary>«Защита манекена: до защиты 60 000, броня −5 000, сопротивления −3 000, уязвимость +1 200, обнулено 0, дошло 51 828.»</summary>
    public static string DefenceNote(DamagePart p)
    {
        static string Signed(double loss, string lossName, string bonusName) =>
            loss >= 0 ? $"{lossName} −{RuNumber.Grouped(loss)}" : $"{bonusName} +{RuNumber.Grouped(-loss)}";
        return $"Защита манекена: до защиты {RuNumber.Grouped(p.BeforeDefense)}, броня −{RuNumber.Grouped(p.ByArmor)}, " +
               $"{Signed(p.ByResist, "сопротивления", "пробивание")}, {Signed(p.ByTakenMultiplier, "множитель получаемого урона", "уязвимость")}, " +
               $"обнулено {RuNumber.Grouped(p.Nullified)}, дошло {RuNumber.Grouped(p.Final)}.";
    }

    /// <summary>A segment's item text: «● » for the running one, its label, «идет», «DPS +12%».</summary>
    public static string SegmentText(FightSegment s) =>
        (s.IsOpen ? "● " : "") + s.Label + (s.IsOpen ? " · идет" : "") +
        (s.DpsChangeVsPrevious is { } change ? " · " + CombatLogText.DpsChange(change) : "");

    /// <summary>Brings every control to the room's state now (the self-test calls it; the window at most every <see cref="RefreshSec"/>).</summary>
    public void Refresh()
    {
        _dirty = false;
        _sinceRefresh = 0;
        SyncSegments();
        var segment = SelectedSegment;
        var skillMode = Mode is MeterMode.DpsBySkill or MeterMode.DamageBySkill;
        if (segment == null)
        {
            _bars.SetRows(Array.Empty<MeterRow>(), Mode, false);
            _empty.Visible = true;
            _total.Text = "";
            _change.Text = "";
        }
        else
        {
            _empty.Visible = false;
            _bars.SetRows(segment.Rows(Mode), Mode, skillMode, MeterColors.SkillOrder(Session));
            var seconds = (int)Math.Floor(segment.Duration);
            _total.Text = $"Урон {RuNumber.Short(segment.TotalDamage)} · DPS {RuNumber.Short(segment.Dps)} · длительность {seconds / 60}:{seconds % 60:00}";
            _total.TooltipText = $"Урон сегмента {RuNumber.Grouped(segment.TotalDamage)}, DPS {RuNumber.Grouped(segment.Dps)} (урон / длительность сегмента)";
            if (segment.DpsChangeVsPrevious is { } change)
            {
                _change.Text = CombatLogText.DpsChange(change) + " к прошлому сегменту";
                _change.AddThemeColorOverride("font_color", change >= 0 ? new Color(0.55f, 0.9f, 0.55f) : new Color(1f, 0.5f, 0.45f));
                _change.TooltipText = "DPS этого сегмента против предыдущего";
            }
            else
            {
                _change.Text = "";
                _change.TooltipText = "";
            }
        }
        var hint = CombatLogText.MeterModeHint(Mode);
        _hint.Visible = hint != null && segment != null;
        _hint.Text = hint ?? "";
        _bars.TooltipText = skillMode ? "Нажмите на полосу, чтобы открыть разбор урона скилла" : "";
        _simMode.Select(Array.IndexOf(SimModes, _room.Mode));
        _seed.SetValueNoSignal(_room.Seed);
        _floating.SetPressedNoSignal(_numbers?.Enabled ?? true);
    }

    // ---------- building ----------

    private void SyncSegments()
    {
        var segments = _room.Segments;
        if (_pinned is { } p && !segments.Any(s => s.Index == p)) _pinned = null;
        var wanted = segments.Reverse().Select(s => s.Index).ToList();
        if (!wanted.SequenceEqual(_segmentChoices))
        {
            if (_segments.GetPopup().Visible) { _dirty = true; return; } // the list changes after the owner closes it
            _segmentChoices.Clear();
            _segmentChoices.AddRange(wanted);
            _segments.Clear();
            foreach (var _ in wanted) _segments.AddItem("");
        }
        for (var i = 0; i < _segmentChoices.Count; i++)
        {
            var segment = segments.First(s => s.Index == _segmentChoices[i]);
            _segments.SetItemText(i, SegmentText(segment));
        }
        _segments.Disabled = _segmentChoices.Count == 0;
        if (_segmentChoices.Count == 0) _segments.Text = "Сегментов нет";
        else if (SelectedSegment is { } shown)
        {
            _segments.Select(_segmentChoices.IndexOf(shown.Index));
            _segments.TooltipText = SegmentText(shown) + "\n" + SegmentsHint;
        }
    }

    private void Build()
    {
        AddThemeConstantOverride("separation", 4);

        var top = new HBoxContainer { Name = "Segment" };
        AddChild(top);
        _segments = new OptionButton { Name = "Segments", FocusMode = FocusModeEnum.None, ClipText = true, FitToLongestItem = false, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(120, 0), TooltipText = SegmentsHint };
        _segments.ItemSelected += i => SelectSegment(i >= 0 && i < _segmentChoices.Count ? (i == 0 ? null : _segmentChoices[(int)i]) : null);
        top.AddChild(_segments);

        var modeRow = new HBoxContainer { Name = "ModeRow" };
        AddChild(modeRow);
        _modes = new OptionButton { Name = "Modes", FocusMode = FocusModeEnum.None };
        foreach (var mode in ModeOrder) _modes.AddItem(CombatLogText.MeterModeName(mode));
        _modes.Select(0);
        _modes.ItemSelected += i => SetMode(ModeOrder[(int)i]);
        modeRow.AddChild(_modes);
        _change = new Label { Name = "Change", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Stop };
        modeRow.AddChild(_change);

        _total = new Label { Name = "Total", ClipText = true, MouseFilter = MouseFilterEnum.Stop };
        _total.AddThemeColorOverride("font_color", new Color(0.93f, 0.85f, 0.7f));
        AddChild(_total);

        var scroll = new ScrollContainer { Name = "BarsScroll", SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(0, 60) };
        AddChild(scroll);
        var barsBox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(barsBox);
        _bars = new MeterBars { Name = "Bars", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _bars.RowClicked += i => OpenRow(i);
        barsBox.AddChild(_bars);
        _empty = new Label { Name = "Empty", Text = NoFightText, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _empty.AddThemeColorOverride("font_color", Grey);
        barsBox.AddChild(_empty);
        _hint = new Label { Name = "Hint", AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill, Visible = false };
        _hint.AddThemeColorOverride("font_color", MeterColors.BonusText);
        barsBox.AddChild(_hint);

        AddChild(new HSeparator());
        var sim = new HBoxContainer { Name = "Sim" };
        AddChild(sim);
        sim.AddChild(new Label { Text = "Случайность:", VerticalAlignment = VerticalAlignment.Center });
        _simMode = new OptionButton { Name = "SimMode", FocusMode = FocusModeEnum.None, TooltipText = "Бросок: настоящие криты и шансы недугов по сиду. Среднее: ожидаемые значения." };
        foreach (var mode in SimModes) _simMode.AddItem(BreakdownText.ModeName(mode));
        _simMode.ItemSelected += i => SetSimMode(SimModes[(int)i], (int)_seed.Value);
        sim.AddChild(_simMode);
        sim.AddChild(new Label { Text = "Сид:", VerticalAlignment = VerticalAlignment.Center });
        _seed = new SpinBox { Name = "Seed", MinValue = 1, MaxValue = 999_999, Step = 1, Rounded = true, Value = 1, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(90, 0), TooltipText = "Сид бросков; смена перезапускает бой" };
        _seed.ValueChanged += v => { if ((int)v != _room.Seed) SetSimMode(_room.Mode, (int)v); };
        sim.AddChild(_seed);

        var actions = new HBoxContainer { Name = "Actions" };
        AddChild(actions);
        _reset = new Button { Name = "Reset", Text = "Сбросить", FocusMode = FocusModeEnum.None, TooltipText = "Закрыть текущий сегмент и удалить все сохраненные (спросит подтверждение)" };
        _reset.Pressed += RequestReset;
        actions.AddChild(_reset);
        _charts = new Button { Name = "Charts", Text = $"Графики ({GameWindow.KeyName(WindowHost.SpecOf(WindowIds.Charts).Key)})", FocusMode = FocusModeEnum.None, TooltipText = "Подробные графики выбранного сегмента" };
        _charts.Pressed += () => _openCharts();
        actions.AddChild(_charts);
        _floating = new CheckBox { Name = "Floating", Text = "Числа урона", ButtonPressed = true, FocusMode = FocusModeEnum.None, TooltipText = "Всплывающие числа урона над манекеном" };
        _floating.Toggled += SetFloatingNumbers;
        actions.AddChild(_floating);

        _confirm = new VBoxContainer { Name = "ConfirmReset", Visible = false };
        AddChild(_confirm);
        var question = new Label { Text = ResetQuestion, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        question.AddThemeColorOverride("font_color", new Color(1f, 0.8f, 0.4f));
        _confirm.AddChild(question);
        var answers = new HBoxContainer();
        _confirm.AddChild(answers);
        var yes = new Button { Name = "Yes", Text = "Да, сбросить", FocusMode = FocusModeEnum.None };
        yes.Pressed += ConfirmReset;
        answers.AddChild(yes);
        var no = new Button { Name = "No", Text = "Отмена", FocusMode = FocusModeEnum.None };
        no.Pressed += CancelReset;
        answers.AddChild(no);
    }
}

/// <summary>
/// Horizontal Details!-style bars drawn on <see cref="Control._Draw"/>: per row a dark track, a fill as long as the value against the
/// largest one, the name on the left and «number · share» on the right (light text with a shadow, readable on every fill).
/// Bonus rows (<see cref="MeterRow.IsBonus"/>) get the bonus colour. A click on a row raises <see cref="RowClicked"/> when
/// <see cref="Clickable"/>.
/// </summary>
public partial class MeterBars : Control
{
    public const float RowHeight = 26;
    public const float TextInset = 6;
    public const float MinGap = 12;

    private IReadOnlyList<MeterRow> _rows = Array.Empty<MeterRow>();
    private IReadOnlyList<string> _skillOrder = Array.Empty<string>();
    private MeterMode _mode;
    private int _hover = -1;

    public IReadOnlyList<MeterRow> Rows => _rows;
    public bool Clickable { get; private set; }

    public event Action<int>? RowClicked;

    public MeterBars()
    {
        MouseFilter = MouseFilterEnum.Stop;
    }

    public void SetRows(IReadOnlyList<MeterRow> rows, MeterMode mode, bool clickable, IReadOnlyList<string>? skillOrder = null)
    {
        _rows = rows;
        _mode = mode;
        _skillOrder = skillOrder ?? Array.Empty<string>();
        Clickable = clickable;
        MouseDefaultCursorShape = clickable ? CursorShape.PointingHand : CursorShape.Arrow;
        CustomMinimumSize = new Vector2(0, rows.Count * RowHeight);
        QueueRedraw();
    }

    /// <summary>The number and share on the right of a row: «1,2 тыс. · 45,3%» (a bonus with «+»).</summary>
    public static string ValueText(MeterRow row) =>
        row.IsBonus
            ? "+" + RuNumber.Format(row.Share * 100, "0.#") + "%" // the caption already says «+19,6 тыс. от уязвимости»
            : RuNumber.Short(row.Value) + " · " + RuNumber.Format(row.Share * 100, "0.#") + "%";

    public Color FillOf(MeterRow row) => MeterColors.Bar(_mode, row, _skillOrder);

    /// <summary>The name and the value of row <paramref name="index"/> fit side by side in the current width.</summary>
    public bool RowFits(int index)
    {
        var (font, size) = Font();
        var row = _rows[index];
        var need = TextInset * 2 + MinGap + font.GetStringSize(row.Label, HorizontalAlignment.Left, -1, size).X +
                   font.GetStringSize(ValueText(row), HorizontalAlignment.Left, -1, size).X;
        return need <= Size.X;
    }

    public int RowAt(Vector2 at) => at.Y < 0 ? -1 : (int)(at.Y / RowHeight) is var i && i < _rows.Count ? i : -1;

    private (Font Font, int Size) Font() => (GetThemeDefaultFont(), GetThemeDefaultFontSize());

    public override void _Draw()
    {
        if (_rows.Count == 0) return;
        var (font, size) = Font();
        var max = _rows.Max(r => r.Value);
        var ascent = font.GetAscent(size);
        var textY = (RowHeight + ascent - font.GetDescent(size)) / 2;
        for (var i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            var y = i * RowHeight;
            var track = new Rect2(0, y + 1, Size.X, RowHeight - 2);
            DrawRect(track, MeterColors.Track);
            var fill = max > 0 ? (float)(row.Value / max) * Size.X : 0;
            if (fill > 0) DrawRect(new Rect2(0, y + 1, fill, RowHeight - 2), FillOf(row));
            if (i == _hover && Clickable) DrawRect(track, MeterColors.HoverEdge, false, 1);
            var value = ValueText(row);
            DrawText(font, new Vector2(TextInset, y + textY), row.Label, HorizontalAlignment.Left, -1, size);
            DrawText(font, new Vector2(TextInset, y + textY), value, HorizontalAlignment.Right, Size.X - 2 * TextInset, size);
        }
    }

    private void DrawText(Font font, Vector2 at, string text, HorizontalAlignment align, float width, int size)
    {
        DrawString(font, at + new Vector2(1, 1), text, align, width, size, MeterColors.TextShadow);
        DrawString(font, at, text, align, width, size, MeterColors.Text);
    }

    public override void _GuiInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseMotion motion:
                var hover = RowAt(motion.Position);
                if (hover != _hover)
                {
                    _hover = hover;
                    QueueRedraw();
                }
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click when Clickable:
                if (RowAt(click.Position) is var row and >= 0)
                {
                    RowClicked?.Invoke(row);
                    AcceptEvent();
                }
                break;
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationMouseExit && _hover >= 0)
        {
            _hover = -1;
            QueueRedraw();
        }
    }

    public override string _GetTooltip(Vector2 atPosition)
    {
        var i = RowAt(atPosition);
        if (i < 0) return TooltipText;
        var row = _rows[i];
        var text = $"{row.Label}: {(row.IsBonus ? "+" : "")}{RuNumber.Grouped(row.Value)} ({RuNumber.Format(row.Share * 100, "0.#")}%)";
        return Clickable ? text + "\nНажмите, чтобы открыть разбор урона скилла" : text;
    }
}

/// <summary>Colours of the meter and the charts: one per skill (by its slot), per damage type, per defence step; bonuses violet.</summary>
public static class MeterColors
{
    public static readonly Color Track = new(0.16f, 0.15f, 0.14f);
    public static readonly Color HoverEdge = new(0.93f, 0.85f, 0.7f);
    public static readonly Color Text = new(0.96f, 0.94f, 0.9f);
    public static readonly Color TextShadow = new(0, 0, 0, 0.85f);
    public static readonly Color BonusText = new(0.82f, 0.68f, 1f);

    private static readonly Color[] Skills =
    [
        new(0.78f, 0.45f, 0.18f), new(0.25f, 0.5f, 0.76f), new(0.42f, 0.62f, 0.25f), new(0.68f, 0.32f, 0.52f),
        new(0.7f, 0.6f, 0.2f), new(0.25f, 0.6f, 0.58f), new(0.52f, 0.38f, 0.72f), new(0.5f, 0.48f, 0.45f),
    ];

    public static readonly Color Final = new(0.36f, 0.6f, 0.3f);
    public static readonly Color Armor = new(0.5f, 0.48f, 0.45f);
    public static readonly Color Resist = new(0.3f, 0.42f, 0.72f);
    public static readonly Color Taken = new(0.62f, 0.34f, 0.3f);
    public static readonly Color Nullified = new(0.3f, 0.28f, 0.27f);
    public static readonly Color Vulnerability = new(0.58f, 0.32f, 0.82f);
    public static readonly Color Penetration = new(0.2f, 0.62f, 0.68f);

    /// <summary>The build's skills in slot order: a skill keeps its colour in every mode and chart.</summary>
    public static IReadOnlyList<string> SkillOrder(SandboxSession session)
    {
        var order = new List<string>();
        for (var i = 0; i < SandboxSession.SlotCount; i++)
            if (session.GetSlot(i) is { } slot && !order.Contains(slot.SkillId)) order.Add(slot.SkillId);
        return order;
    }

    public static Color Skill(string skillId, IReadOnlyList<string> order)
    {
        var at = order.ToList().IndexOf(skillId);
        if (at < 0) at = order.Count + (int)((uint)StableHash(skillId) % 3);
        return Skills[at % Skills.Length];
    }

    public static Color Type(string key) => key switch
    {
        "physical" => new Color(0.6f, 0.56f, 0.5f),
        "fire" => new Color(0.82f, 0.36f, 0.16f),
        "cold" => new Color(0.32f, 0.58f, 0.82f),
        "lightning" => new Color(0.78f, 0.68f, 0.2f),
        "poison" => new Color(0.4f, 0.66f, 0.24f),
        _ => new Color(0.5f, 0.48f, 0.45f),
    };

    public static Color Type(DamageType type) => Type(type.ToString().ToLowerInvariant());

    public static Color Mitigation(string key) => key switch
    {
        "final" => Final,
        "armor" => Armor,
        "resist" => Resist,
        "taken_multiplier" => Taken,
        "nullified" => Nullified,
        "vulnerability" => Vulnerability,
        "penetration" => Penetration,
        _ => Armor,
    };

    public static Color Bar(MeterMode mode, MeterRow row, IReadOnlyList<string> skillOrder) => mode switch
    {
        MeterMode.DamageByType => Type(row.Key),
        MeterMode.Mitigation => Mitigation(row.Key),
        _ => Skill(row.Key, skillOrder),
    };

    private static int StableHash(string s)
    {
        unchecked
        {
            var h = 17;
            foreach (var c in s) h = h * 31 + c;
            return h;
        }
    }
}
