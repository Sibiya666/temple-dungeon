using Dialoid.Core.Combat;
using Dialoid.Core.Explain;
using Dialoid.Core.Simulation;
using Dialoid.Game.Room;
using Dialoid.Sandbox;
using Godot;
using Dialoid.Game.Ui.Art;

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
public partial class MeterWindow : SessionPanel, IPadPanel
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
    private static readonly Color Grey = new("9baeb3");

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
    private Color? _changeColor;

    /// <summary>Key of the first «Потеря на защите» row: the damage before defence, the 100% the other rows are shares of.</summary>
    public const string BeforeKey = "before";

    /// <summary>The bars of a mode: the segment's rows; «Потеря на защите» starts with a neutral «до защиты · 100%» row.</summary>
    public static IReadOnlyList<MeterRow> RowsOf(FightSegment segment, MeterMode mode)
    {
        var rows = segment.Rows(mode);
        if (mode != MeterMode.Mitigation) return rows;
        var list = new List<MeterRow>(rows.Count + 1) { new(BeforeKey, "до защиты", segment.DefenceTotal.BeforeDefense, 1) };
        list.AddRange(rows);
        return list;
    }

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

    /// <summary>«Сбросить»: asks first; on the gamepad the focus waits on «Отмена» (A there resets nothing).</summary>
    public void RequestReset()
    {
        _confirm.Visible = true;
        _reset.Disabled = true;
        if (Dialoid.Game.Input.InputDevice.Gamepad && _no.IsVisibleInTree()) _no.GrabFocus();
    }

    public void CancelReset()
    {
        var focusInside = _confirm.Visible && (_no.HasFocus() || _yes.HasFocus());
        _confirm.Visible = false;
        _reset.Disabled = false;
        if (focusInside) _reset.GrabFocus();
    }

    /// <summary>The «Отмена» and «Да, сбросить» buttons of the confirmation.</summary>
    public Button CancelButton => _no;
    public Button ConfirmButton => _yes;
    public Button ResetButton => _reset;

    private Button _no = null!;
    private Button _yes = null!;

    // ---------- gamepad ----------

    public bool PadButton(PadCommand command, Control? focus)
    {
        if (command != PadCommand.Extra || _confirm.Visible) return false;
        RequestReset();
        return true;
    }

    public bool PadBack()
    {
        if (!_confirm.Visible) return false;
        CancelReset();
        return true;
    }

    public IEnumerable<(string Button, string Label)> PadPrompts(Control? focus)
    {
        if (!_confirm.Visible) yield return ("[Y]", "Сбросить");
    }

    public void PadClosed() { }

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
        var key = _bars.Rows[index].Key;
        if (MainLine(key) is { } main) _open(main, SkillName(key)); // one step back in «Разбор» history: the skill's damage line
        var (line, context) = SkillLine(segment, key);
        _open(line, context);
        return true;
    }

    /// <summary>Key of the line a skill bar opens: the bar's own number.</summary>
    public static string BarKey(string skillId) => "meter_dps_" + skillId;

    private string SkillName(string skillId) => Session.Data.Skills.TryGetValue(skillId, out var skill) ? skill.Name : skillId;

    /// <summary>The skill's main damage line (<see cref="MainDamageKey"/>) of the current build, or null for a skill outside the slots.</summary>
    public NumberLine? MainLine(string skillId)
    {
        for (var i = 0; i < SandboxSession.SlotCount; i++)
            if (Session.GetSlot(i)?.SkillId == skillId)
                return Session.SkillLines(i).FirstOrDefault(l => l.Key == MainDamageKey);
        return null;
    }

    /// <summary>
    /// What a skill bar opens (spec §8.1, «то же число, что на полосе»): the skill's DPS in the segment with the dummy's defence
    /// (<see cref="FightSegment.DpsBySkill"/>), the note giving its damage and the defence split; below it the skill's main damage
    /// line from the sheet, which <see cref="OpenRow"/> also puts into the «Разбор» history one click away. Context: the skill's name.
    /// </summary>
    public (NumberLine Line, string Context) SkillLine(FightSegment segment, string skillId)
    {
        var dps = segment.DpsBySkill().FirstOrDefault(r => r.Key == skillId)?.Value ?? 0;
        var damage = segment.DamageBySkill().FirstOrDefault(r => r.Key == skillId)?.Value ?? 0;
        var note = $"Сегмент {segment.Label}: урон {RuNumber.Grouped(damage)} за {BreakdownText.Seconds(segment.Duration)} с, с учетом защиты манекена.";
        if (segment.DefenceBySkill().TryGetValue(skillId, out var split)) note += "\n" + DefenceNote(split);
        if (MainLine(skillId) is { } main)
            note += $"\nЛист скилла (текущий билд, без защиты манекена): {NumberLabel.TextOf(main)}" + (main.Note is { } own ? $" ({own})" : "") +
                    ". Эта строка — в истории «Разбора» ниже.";
        // No digits in the label, so the title shows the value: «Вихрь · сегмент №10 — DPS за сегмент: 1394,4».
        return (new NumberLine(BarKey(skillId), "DPS за сегмент", dps, null, note), $"{SkillName(skillId)} · сегмент №{segment.Index}");
    }

    /// <summary>«Защита манекена: до защиты 60 000; броня −5 000; сопротивления −3 000; уязвимость +1 200; …; дошло 51 828.»</summary>
    public static string DefenceNote(DefenceSplit p)
    {
        var parts = new List<string> { $"до защиты {RuNumber.Grouped(p.BeforeDefense)}", $"броня −{RuNumber.Grouped(p.Armor)}" };
        parts.Add($"сопротивления −{RuNumber.Grouped(p.ResistLoss)}");
        if (p.ResistBonus > 0) parts.Add($"пробивание +{RuNumber.Grouped(p.ResistBonus)}");
        parts.Add($"множитель получаемого урона −{RuNumber.Grouped(p.TakenLoss)}");
        if (p.TakenBonus > 0) parts.Add($"уязвимость +{RuNumber.Grouped(p.TakenBonus)}");
        parts.Add($"обнулено {RuNumber.Grouped(p.Nullified)}");
        parts.Add($"дошло {RuNumber.Grouped(p.Final)}");
        return "Защита манекена: " + string.Join("; ", parts) + ".";
    }

    /// <summary>A segment's item text: its label, «идет» for the running one, «DPS +12%» once it is closed (a running segment's
    /// DPS is still moving, so its change is shown only beside the list, grey and marked preliminary).</summary>
    public static string SegmentText(FightSegment s) =>
        s.Label + (s.IsOpen ? " · идет" : s.DpsChangeVsPrevious is { } change ? " · " + CombatLogText.DpsChange(change) : "");

    /// <summary>The text beside the list: «DPS +12% к прошлому сегменту», for a running segment «… (предварительно)».</summary>
    public static string ChangeLabel(FightSegment s) =>
        s.DpsChangeVsPrevious is { } change
            ? CombatLogText.DpsChange(change) + " к прошлому" + (s.IsOpen ? " (предварительно)" : " сегменту")
            : "";

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
            _bars.SetRows(RowsOf(segment, Mode), Mode, skillMode, MeterColors.SkillOrder(Session));
            var seconds = (int)Math.Floor(segment.Duration);
            var duration = segment.Duration < 10 ? RuNumber.Format(segment.Duration, "0.###") + " с" : $"{seconds / 60}:{seconds % 60:00}";
            _total.Text = $"Урон {RuNumber.Short(segment.TotalDamage)} · DPS {RuNumber.Short(segment.Dps)} · длительность {duration}";
            _total.TooltipText = $"Урон сегмента {RuNumber.Grouped(segment.TotalDamage)}, DPS {RuNumber.Grouped(segment.Dps)} (урон / длительность сегмента); длительность {RuNumber.Format(segment.Duration, "0.######")} с";
            _change.Text = ChangeLabel(segment);
            // «DPS 0%» is neither better nor worse: neutral grey (curator, task 13 round 1).
            var color = segment.IsOpen || segment.DpsChangeVsPrevious is not { } change || Math.Abs(change) < 0.005 ? Grey
                : change >= 0 ? new Color(0.55f, 0.9f, 0.55f) : new Color(1f, 0.5f, 0.45f);
            if (_changeColor != color)
            {
                _changeColor = color;
                _change.AddThemeColorOverride("font_color", color);
            }
            _change.TooltipText = segment.IsOpen
                ? "Сегмент еще идет: его DPS меняется, окончательная разница — после 5 с без ударов"
                : "DPS этого сегмента против предыдущего";
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
        Theme = RoomHudTheme.TowerWindowTheme();
        AddThemeConstantOverride("separation", RoomHudTheme.ItemGap);
        AddChild(RoomHudTheme.TowerHeading("Сегмент боя / наблюдаемый урон с учетом защиты манекена", 17));
        var columns = new TowerColumns { Name = "MeterColumns", SizeFlagsVertical = SizeFlags.ExpandFill, LeftMinimumHeight = 190 };
        AddChild(columns);
        var summaryScroll = new ScrollContainer { Name = "SegmentScroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        columns.AddChild(summaryScroll);
        var summary = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        summary.AddThemeConstantOverride("separation", RoomHudTheme.GroupGap);
        summaryScroll.AddChild(summary);
        summary.AddChild(RoomHudTheme.TowerHeading("ВЫБОР СЕГМЕНТА", 16));
        var detailsScroll = new ScrollContainer { Name = "DetailsScroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        columns.AddChild(detailsScroll);
        var details = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        details.AddThemeConstantOverride("separation", RoomHudTheme.GroupGap);
        detailsScroll.AddChild(details);

        var top = new HBoxContainer { Name = "Segment" };
        summary.AddChild(top);
        _segments = new OptionButton { Name = "Segments", FocusMode = FocusModeEnum.None, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, FitToLongestItem = false, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(120, 0), TooltipText = SegmentsHint };
        _segments.ItemSelected += i => SelectSegment(i >= 0 && i < _segmentChoices.Count ? (i == 0 ? null : _segmentChoices[(int)i]) : null);
        top.AddChild(_segments);

        var modeRow = new HFlowContainer { Name = "ModeRow" };
        details.AddChild(modeRow);
        _modes = new OptionButton { Name = "Modes", FocusMode = FocusModeEnum.None, FitToLongestItem = false, ClipText = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(180, 0) };
        foreach (var mode in ModeOrder) _modes.AddItem(CombatLogText.MeterModeName(mode));
        _modes.Select(0);
        _modes.ItemSelected += i => SetMode(ModeOrder[(int)i]);
        modeRow.AddChild(_modes);
        _change = new Label { Name = "Change", AutowrapMode = TextServer.AutowrapMode.WordSmart, VerticalAlignment = VerticalAlignment.Center, SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Stop };
        ButtonPrompts.Info(_change);

        _total = ButtonPrompts.Info(new Label { Name = "Total", AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = MouseFilterEnum.Stop });
        _total.AddThemeColorOverride("font_color", RoomHudTheme.Parchment);
        summary.AddChild(_total);
        summary.AddChild(_change);

        var scroll = new ScrollContainer { Name = "BarsScroll", SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(0, 60) };
        details.AddChild(scroll);
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

        details.AddChild(RoomHudTheme.TowerHeading("НАСТРОЙКИ КОМНАТЫ", 16));
        details.AddChild(new HSeparator());
        var sim = new HFlowContainer { Name = "Sim" };
        details.AddChild(sim);
        sim.AddChild(new Label { Text = "Случайность:", VerticalAlignment = VerticalAlignment.Center });
        _simMode = new OptionButton { Name = "SimMode", FocusMode = FocusModeEnum.None, FitToLongestItem = false, ClipText = true, CustomMinimumSize = new Vector2(100, 0), TooltipText = "Бросок: настоящие криты и шансы недугов по сиду. Среднее: ожидаемые значения." };
        foreach (var mode in SimModes) _simMode.AddItem(BreakdownText.ModeName(mode));
        _simMode.ItemSelected += i => SetSimMode(SimModes[(int)i], (int)_seed.Value);
        sim.AddChild(_simMode);
        sim.AddChild(new Label { Text = "Сид:", VerticalAlignment = VerticalAlignment.Center });
        _seed = new SpinBox { Name = "Seed", MinValue = 1, MaxValue = 999_999, Step = 1, Rounded = true, Value = 1, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(90, 0), TooltipText = "Сид бросков; смена перезапускает бой" };
        _seed.ValueChanged += v => { if ((int)v != _room.Seed) SetSimMode(_room.Mode, (int)v); };
        sim.AddChild(_seed);
        ButtonPrompts.SeedPad(_seed);

        var actions = new HFlowContainer { Name = "Actions" };
        summary.AddChild(actions);
        _reset = new Button { Name = "Reset", Text = "Сбросить", FocusMode = FocusModeEnum.None, TooltipText = "Закрыть текущий сегмент и удалить все сохраненные (спросит подтверждение)" };
        _reset.Pressed += RequestReset;
        actions.AddChild(_reset);
        _charts = new Button { Name = "Charts", Text = $"Графики ({GameWindow.KeyName(WindowHost.SpecOf(WindowIds.Charts).Key)})", FocusMode = FocusModeEnum.None, TooltipText = "Подробные графики выбранного сегмента" };
        _charts.Pressed += () => _openCharts();
        actions.AddChild(_charts);
        _floating = new CheckBox { Name = "Floating", Text = "Числа урона", ButtonPressed = true, FocusMode = FocusModeEnum.None, TooltipText = "Всплывающие числа урона над манекеном" };
        _floating.Toggled += SetFloatingNumbers;
        details.AddChild(_floating);
        details.AddChild(new Label { Text = "Нажмите на полосу, чтобы открыть разбор урона скилла.", AutowrapMode = TextServer.AutowrapMode.WordSmart });

        _confirm = new VBoxContainer { Name = "ConfirmReset", Visible = false };
        summary.AddChild(_confirm);
        var question = new Label { Text = ResetQuestion, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        question.AddThemeColorOverride("font_color", new Color("c4968e"));
        _confirm.AddChild(question);
        var answers = new HBoxContainer();
        _confirm.AddChild(answers);
        var yes = _yes = new Button { Name = "Yes", Text = "Да, сбросить", FocusMode = FocusModeEnum.None };
        // The destructive answer in a muted danger frame; «Отмена» keeps the plain face.
        foreach (var (state, bg, edge) in new[]
                 {
                     ("normal", new Color(0.3f, 0.14f, 0.12f), new Color(0.72f, 0.3f, 0.25f)),
                     ("hover", new Color(0.4f, 0.17f, 0.14f), new Color(0.9f, 0.4f, 0.32f)),
                     ("pressed", new Color(0.22f, 0.1f, 0.09f), new Color(0.9f, 0.4f, 0.32f)),
                 })
        {
            var box = new StyleBoxFlat { BgColor = bg, BorderColor = edge, ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 3, ContentMarginBottom = 3 };
            box.SetBorderWidthAll(1);
            box.SetCornerRadiusAll(3);
            yes.AddThemeStyleboxOverride(state, box);
        }
        yes.Pressed += ConfirmReset;
        answers.AddChild(yes);
        var no = _no = new Button { Name = "No", Text = "Отмена", FocusMode = FocusModeEnum.None };
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
public partial class MeterBars : Control, IPadControl
{
    public const float RowHeight = 26;
    public float DisplayRowHeight => Size.X >= 760 ? 40 : RowHeight;
    public const float TextInset = 6;
    public const float MinGap = 12;

    private IReadOnlyList<MeterRow> _rows = Array.Empty<MeterRow>();
    private IReadOnlyList<string> _skillOrder = Array.Empty<string>();
    private MeterMode _mode;
    private int _hover = -1;
    // Per row, computed in SetRows so _Draw allocates nothing: the right-hand text, the fill colour, the text colour, the label width.
    private string[] _values = Array.Empty<string>();
    private Color[] _fills = Array.Empty<Color>();
    private Color[] _texts = Array.Empty<Color>();
    private float[] _labelWidths = Array.Empty<float>();
    private double _max;
    private bool _wide;

    public IReadOnlyList<MeterRow> Rows => _rows;
    public bool Clickable { get; private set; }

    public event Action<int>? RowClicked;

    public MeterBars()
    {
        MouseFilter = MouseFilterEnum.Stop;
        ThemeChanged += () => SetRows(_rows, _mode, Clickable, _skillOrder);
    }

    public void SetRows(IReadOnlyList<MeterRow> rows, MeterMode mode, bool clickable, IReadOnlyList<string>? skillOrder = null)
    {
        _rows = rows;
        _mode = mode;
        _skillOrder = skillOrder ?? Array.Empty<string>();
        Clickable = clickable;
        MouseDefaultCursorShape = clickable ? CursorShape.PointingHand : CursorShape.Arrow;
        CustomMinimumSize = new Vector2(0, rows.Count * DisplayRowHeight);
        var n = rows.Count;
        _values = new string[n];
        _fills = new Color[n];
        _texts = new Color[n];
        _labelWidths = new float[n];
        _max = 0;
        var (font, size) = Font();
        for (var i = 0; i < n; i++)
        {
            var row = rows[i];
            _values[i] = ValueText(row, mode);
            _fills[i] = FillOf(row);
            // A step that took nothing reads as greyed out.
            _texts[i] = mode == MeterMode.Mitigation && row.Value == 0 ? MeterColors.Muted : MeterColors.Text;
            _labelWidths[i] = font.GetStringSize(row.Label, HorizontalAlignment.Left, -1, size).X;
            if (row.Value > _max) _max = row.Value;
        }
        QueueRedraw();
    }

    /// <summary>The number and share on the right of a row: «1,2 тыс. · 45,3%»; a bonus shows its share only («+35%», the caption
    /// already says «+19,6 тыс. от уязвимости»); «дошло» above 100% adds the factor («148,4% · ×1,48»).</summary>
    public static string ValueText(MeterRow row, MeterMode mode = MeterMode.DpsBySkill)
    {
        var share = RuNumber.Format(row.Share * 100, "0.#") + "%";
        if (row.IsBonus) return "+" + share;
        var text = RuNumber.Short(row.Value) + " · " + share;
        if (mode == MeterMode.Mitigation && row.Key == "final" && row.Share > 1) text += " · ×" + RuNumber.Format(row.Share, "0.00");
        return text;
    }

    public Color FillOf(MeterRow row) => row.Key == MeterWindow.BeforeKey && _mode == MeterMode.Mitigation
        ? MeterColors.Before
        : MeterColors.Bar(_mode, row, _skillOrder);

    /// <summary>The name and the value of row <paramref name="index"/> fit side by side in the current width.</summary>
    public bool RowFits(int index)
    {
        var (font, size) = Font();
        var row = _rows[index];
        var need = TextInset * 2 + MinGap + font.GetStringSize(row.Label, HorizontalAlignment.Left, -1, size).X +
                   font.GetStringSize(_values[index], HorizontalAlignment.Left, -1, size).X;
        return need <= Size.X;
    }

    /// <summary>Where the caption of row <paramref name="index"/> starts: inside the fill, or past it when a bonus fill is shorter
    /// than the caption (the caption then reads on the dark track, not half on the colour).</summary>
    public float LabelX(int index)
    {
        var fill = FillWidth(index);
        return _rows[index].IsBonus && fill > 0 && fill < _labelWidths[index] + 2 * TextInset ? fill + TextInset : TextInset;
    }

    private float FillWidth(int index) => _max > 0 ? (float)(_rows[index].Value / _max) * Size.X : 0;

    public int RowAt(Vector2 at) => at.Y < 0 ? -1 : (int)(at.Y / DisplayRowHeight) is var i && i < _rows.Count ? i : -1;

    private (Font Font, int Size) Font() => (GetThemeDefaultFont(), GetThemeDefaultFontSize() + (Size.X >= 760 ? 3 : 0));

    public override void _Draw()
    {
        if (_rows.Count == 0) return;
        var (font, size) = Font();
        var ascent = font.GetAscent(size);
        var height = DisplayRowHeight;
        var textY = (height + ascent - font.GetDescent(size)) / 2;
        for (var i = 0; i < _rows.Count; i++)
        {
            var y = i * height;
            var track = new Rect2(0, y + 2, Size.X, height - 4);
            DrawRect(track, MeterColors.Track);
            var fill = FillWidth(i);
            if (fill > 0) DrawRect(new Rect2(0, y + 2, fill, height - 4), _fills[i]);
            if (i == _hover && HasFocus()) DrawRect(track.Grow(-1), WindowHost.FocusFrame, false, 2);
            else if (i == _hover && Clickable) DrawRect(track, MeterColors.HoverEdge, false, 1);
            DrawText(font, new Vector2(LabelX(i), y + textY), _rows[i].Label, HorizontalAlignment.Left, -1, size, _texts[i]);
            DrawText(font, new Vector2(TextInset, y + textY), _values[i], HorizontalAlignment.Right, Size.X - 2 * TextInset, size, _texts[i]);
        }
    }

    private void DrawText(Font font, Vector2 at, string text, HorizontalAlignment align, float width, int size, Color color)
    {
        DrawString(font, at + new Vector2(1, 1), text, align, width, size, MeterColors.TextShadow);
        DrawString(font, at, text, align, width, size, color);
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
        if (what == NotificationResized && _wide != (Size.X >= 760))
        {
            _wide = Size.X >= 760;
            SetRows(_rows, _mode, Clickable, _skillOrder);
        }
        else if (what == NotificationMouseExit && _hover >= 0 && !Dialoid.Game.Input.InputDevice.Gamepad)
        {
            _hover = -1;
            QueueRedraw();
        }
        else if (what == NotificationFocusEnter)
        {
            if (_hover < 0 || _hover >= _rows.Count) _hover = _rows.Count > 0 ? 0 : -1;
            QueueRedraw();
        }
        else if (what == NotificationFocusExit) QueueRedraw();
    }

    // ---------- gamepad: the bars are in the focus chain, ↑/↓ pick a row, A opens its breakdown ----------

    /// <summary>The row the gamepad (or the mouse) points at, or -1.</summary>
    public int FocusedRow => _hover;

    /// <summary>The focused row's rectangle on screen (the tooltip and checks).</summary>
    public Rect2 RowRect(int index) => new(GlobalPosition + new Vector2(0, index * DisplayRowHeight), new Vector2(Size.X, DisplayRowHeight));

    public bool PadInput(PadCommand command)
    {
        if (_rows.Count == 0) return command == PadCommand.Accept;
        if (_hover < 0 || _hover >= _rows.Count) _hover = 0;
        switch (command)
        {
            case PadCommand.Up when _hover > 0:
            case PadCommand.Down when _hover < _rows.Count - 1:
                _hover += command == PadCommand.Down ? 1 : -1;
                QueueRedraw();
                // Keep the row in view inside the window's scroll.
                if (GetParent()?.GetParent() is ScrollContainer s2)
                {
                    var top = _hover * DisplayRowHeight;
                    if (top < s2.ScrollVertical) s2.ScrollVertical = (int)top;
                    else if (top + DisplayRowHeight > s2.ScrollVertical + s2.Size.Y) s2.ScrollVertical = (int)(top + DisplayRowHeight - s2.Size.Y);
                }
                return true;
            case PadCommand.Accept:
                if (Clickable) RowClicked?.Invoke(_hover);
                return true;
            default:
                return false;
        }
    }

    public bool PadStick(Vector2 value, double delta) => false;

    public string PadTooltip() => _hover >= 0 && _hover < _rows.Count ? _GetTooltip(new Vector2(4, _hover * DisplayRowHeight + DisplayRowHeight / 2)) : TooltipText;

    public IEnumerable<(string Button, string Label)> PadPrompts()
    {
        yield return ("[↑/↓]", "Полоса");
        if (Clickable && _rows.Count > 0) yield return ("[A]", "Разбор");
    }

    public void PadModeChanged(bool gamepad)
    {
        if (!gamepad && _hover >= 0)
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
    public static readonly Color Track = new("132027");
    public static readonly Color HoverEdge = new("b7cbd0");
    public static readonly Color Text = new("d8d5c6");
    public static readonly Color TextShadow = new(0, 0, 0, 0.85f);
    public static readonly Color BonusText = new(0.82f, 0.68f, 1f);
    /// <summary>A row that holds zero, greyed out.</summary>
    public static readonly Color Muted = new("839399");
    /// <summary>The neutral «до защиты» base of «Потеря на защите».</summary>
    public static readonly Color Before = new(0.36f, 0.34f, 0.31f);

    private static readonly Color[] Skills =
    [
        new("456674"), new("526575"), new("536b5b"), new("78546b"),
        new("78795e"), new("497c78"), new("70627f"), new("6b777d"),
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
        var at = -1;
        for (var i = 0; i < order.Count && at < 0; i++)
            if (order[i] == skillId) at = i;
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
