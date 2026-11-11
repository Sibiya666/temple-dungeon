using Dialoid.Core.Explain;
using Dialoid.Core.Simulation;
using Dialoid.Sandbox;
using Godot;
using Dialoid.Game.Ui.Art;

namespace Dialoid.Game.Ui;

/// <summary>
/// The «Симуляция» tab (spec §13.5): dummy, pack size (default from the dummy), mode, seed, «манекен бьет персонажа» and
/// «Симулировать». The run goes through <see cref="SimulationRunner"/> (build snapshot taken on the main thread, simulation on
/// the thread pool); the outcome is applied on the main thread only (the await resumes on Godot's synchronization context). While running the button is off and «считаю…» shows.
/// Results (spec §11.3) come straight from <see cref="SimResult"/> through <see cref="NumberLabel"/>: DPS single/pack, DPS and damage by
/// skill and by type, hits vs damage over time, time to kill, time to death, the defensive totals, the resource series drawn
/// on a <see cref="SeriesGraph"/> and the event log on a button. A result whose revision is no longer current is not shown
/// («билд изменился — пересчитайте»); a rejected run shows its violations instead of numbers.
/// The inputs are built once; the result area redraws (deferred) on a session change or a new outcome.
/// </summary>
public partial class SimulationPanel : SessionPanel
{
    /// <summary>Shown instead of a result whose build revision is no longer the session's.</summary>
    public const string StaleText = "билд изменился — пересчитайте";

    /// <summary>Shown (and on the button) while a run is in progress.</summary>
    public const string RunningText = "считаю…";

    private const string RunCaption = "Симулировать";

    private Action<NumberLine, string?> _open = null!;
    private SimulationRunner _runner = null!;
    private int _mainThreadId;
    private Dialoid.Core.Data.GameData _data = null!;
    private readonly List<string> _dummyIds = new();
    private readonly List<SimMode> _modes = [SimMode.Average, SimMode.Roll];
    private readonly List<NumberLabel> _numbers = new();
    private string? _runError;
    private bool _logShown;
    private VBoxContainer _settings = null!;

    /// <summary>The input controls (the self-test sets them like the owner does).</summary>
    public OptionButton DummyOptions { get; private set; } = null!;
    public SpinBox PackSize { get; private set; } = null!;
    public OptionButton ModeOptions { get; private set; } = null!;
    public SpinBox Seed { get; private set; } = null!;
    public CheckBox DummyAttacks { get; private set; } = null!;
    public Button RunButton { get; private set; } = null!;

    /// <summary>Dummy id of each <see cref="DummyOptions"/> item.</summary>
    public IReadOnlyList<string> DummyIds => _dummyIds;

    /// <summary>True from the press of «Симулировать» until its outcome is applied.</summary>
    public bool Running { get; private set; }

    /// <summary>The run in progress (or the last one): completes after its outcome is applied on the main thread.</summary>
    public Task? RunTask { get; private set; }

    /// <summary>The last applied outcome, or null.</summary>
    public SimOutcome? LastOutcome { get; private set; }

    /// <summary>True when the last outcome's continuation ran on the main thread (Godot's sync context).</summary>
    public bool AppliedOnMainThread { get; private set; }

    /// <summary>What the result area says above the numbers (running, stale, rejected, ready...).</summary>
    public string StatusText { get; private set; } = "";

    /// <summary>The result numbers shown now.</summary>
    public IReadOnlyList<NumberLabel> Numbers => _numbers;

    /// <summary>The result number of a key (sim_dps_single, sim_dps_pack...), or null.</summary>
    public NumberLabel? NumberFor(string key) => _numbers.FirstOrDefault(n => n.Line.Key == key);

    /// <summary>The violation messages shown instead of results.</summary>
    public IReadOnlyList<string> ShownViolations { get; private set; } = Array.Empty<string>();

    /// <summary>The event log view (shown by «Показать лог»), or null when no result is shown.</summary>
    public RichTextLabel? Log { get; private set; }

    /// <summary>The resource series graph of the shown result, or null.</summary>
    public SeriesGraph? Graph { get; private set; }

    /// <summary>The «Время до смерти» line when the run has no time to death (null otherwise).</summary>
    public string? TimeToDeathText { get; private set; }

    public void Setup(SandboxSession session, Action<NumberLine, string?> open)
    {
        _open = open;
        _runner = new SimulationRunner(session);
        _mainThreadId = System.Environment.CurrentManagedThreadId;
        _data = session.Data;
        Theme = RoomHudTheme.TowerWindowTheme();
        AddChild(RoomHudTheme.TowerHeading("Проверка билда / параметры запуска и результат одной ревизии", 18));
        var columns = new TowerColumns { Name = "SimulationColumns", SizeFlagsVertical = SizeFlags.ExpandFill };
        AddChild(columns);
        var settingsScroll = new ScrollContainer { Name = "SettingsScroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        columns.AddChild(settingsScroll);
        _settings = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _settings.AddThemeConstantOverride("separation", RoomHudTheme.GroupGap);
        settingsScroll.AddChild(_settings);
        _settings.AddChild(RoomHudTheme.TowerHeading("ПАРАМЕТРЫ", 16));
        BuildInputs(session);
        InitSession(session);
        // Keep SessionPanel's result content and deferred redraw semantics; only its scroll moves to the workspace.
        var results = new VBoxContainer();
        results.AddThemeConstantOverride("separation", RoomHudTheme.GroupGap);
        columns.AddChild(results);
        results.AddChild(RoomHudTheme.TowerHeading("РЕЗУЛЬТАТ / ТЕКУЩИЙ БИЛД", 16));
        var resultScroll = Content!.GetParent<ScrollContainer>();
        RemoveChild(resultScroll);
        results.AddChild(resultScroll);
    }

    private void BuildInputs(SandboxSession session)
    {
        var data = session.Data;
        var grid = new GridContainer { Columns = 1, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("v_separation", RoomHudTheme.ItemGap);
        _settings.AddChild(grid);

        DummyOptions = new OptionButton { FitToLongestItem = false, ClipText = true };
        foreach (var (id, _) in data.Dummies)
        {
            _dummyIds.Add(id);
            DummyOptions.AddItem(BreakdownText.DummyName(data, id));
        }
        DummyOptions.ItemSelected += i => SelectDummy((int)i);
        Row(grid, "Манекен", DummyOptions);

        PackSize = new SpinBox { MinValue = 1, MaxValue = DummyDefinition.MaxPackSize, Step = 1 };
        Row(grid, "Размер пака", PackSize);

        ModeOptions = new OptionButton();
        foreach (var m in _modes) ModeOptions.AddItem(BreakdownText.ModeName(m));
        ModeOptions.Selected = 0;
        Row(grid, "Режим", ModeOptions);

        Seed = new SpinBox { MinValue = 0, MaxValue = 1_000_000, Step = 1, Value = 1, TooltipText = "Сид случайных бросков (режим «бросок»)" };
        Row(grid, "Сид", Seed);
        ButtonPrompts.SeedPad(Seed);

        DummyAttacks = new CheckBox { Text = "Манекен бьет персонажа" };
        Row(grid, "Защита", DummyAttacks);

        RunButton = new Button { Text = RunCaption, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        RunButton.Pressed += () => Simulate();
        _settings.AddChild(RunButton);

        if (_dummyIds.Count > 0)
        {
            int start = Math.Max(0, _dummyIds.IndexOf("training"));
            DummyOptions.Selected = start;
            SelectDummy(start);
        }
        else RunButton.Disabled = true;
    }

    // ---- handlers (the controls and the self-test call these) -------------------------------------------------------

    /// <summary>Picks a dummy and resets the pack size to its own.</summary>
    public void SelectDummy(int index)
    {
        if (index < 0 || index >= _dummyIds.Count) return;
        PackSize.Value = _data.Dummies[_dummyIds[index]].PackSize;
    }

    /// <summary>The request the inputs describe now.</summary>
    public SimRequest CurrentRequest() => new(
        _dummyIds[Math.Max(0, DummyOptions.Selected)],
        _modes[Math.Max(0, ModeOptions.Selected)],
        (int)Seed.Value,
        (int)PackSize.Value,
        DummyAttacks.ButtonPressed);

    /// <summary>
    /// Starts a run (ignored while one is running). The build snapshot is taken now, on the main thread; the outcome is
    /// applied on the main thread. Returns <see cref="RunTask"/>.
    /// </summary>
    public Task Simulate()
    {
        if (Running && RunTask != null) return RunTask;
        if (_dummyIds.Count == 0) return Task.CompletedTask;
        Running = true;
        _runError = null;
        RunButton.Disabled = true;
        RunButton.Text = RunningText;
        RequestRedraw();
        Task<SimOutcome> task;
        try
        {
            task = _runner.RunAsync(CurrentRequest());
        }
        catch (Exception e)
        {
            Apply(null, $"{e.GetType().Name}: {e.Message}");
            return RunTask = Task.CompletedTask;
        }
        RunTask = Await(task);
        return RunTask;
    }

    /// <summary>
    /// Awaits the run. Started on the main thread, the continuation comes back to it through Godot's synchronization
    /// context (the self-test checks <see cref="AppliedOnMainThread"/>), so the outcome is applied there.
    /// </summary>
    private async Task Await(Task<SimOutcome> task)
    {
        SimOutcome? outcome = null;
        string? error = null;
        try { outcome = await task; }
        catch (Exception e) { error = $"{e.GetType().Name}: {e.Message}"; }
        AppliedOnMainThread = System.Environment.CurrentManagedThreadId == _mainThreadId;
        Apply(outcome, error);
    }

    /// <summary>Main thread only. An error clears the old outcome, so a crash is never shown above old numbers.</summary>
    private void Apply(SimOutcome? outcome, string? error)
    {
        if (!IsInstanceValid(this)) return;
        Running = false;
        LastOutcome = error != null ? null : outcome ?? LastOutcome;
        _runError = error;
        _logShown = false;
        RunButton.Disabled = false;
        RunButton.Text = RunCaption;
        RequestRedraw();
    }

    /// <summary>Shows or hides the event log.</summary>
    public void ToggleLog()
    {
        _logShown = !_logShown;
        RequestRedraw();
    }

    // ---- drawing ------------------------------------------------------------------------------------------------------

    protected override void DrawContent()
    {
        _numbers.Clear();
        ShownViolations = Array.Empty<string>();
        Log = null;
        Graph = null;
        TimeToDeathText = null;

        if (_runError != null) AddText("Симуляция упала: " + _runError, Red);
        if (Running)
        {
            Status(RunningText, Amber);
            return;
        }
        if (LastOutcome is not { } outcome)
        {
            Status("Нажмите «Симулировать».", null);
            return;
        }
        if (!_runner.IsCurrent(outcome))
        {
            Status(StaleText, Amber);
            return;
        }

        var result = outcome.Result;
        if (result.Violations.Count > 0)
        {
            Status("Симуляция не запущена: билд нарушает правила.", Red);
            ShownViolations = result.Violations.Select(v => v.Message).ToList();
            foreach (var v in ShownViolations) AddText("• " + v, Red);
            return;
        }

        var data = Session.Data;
        var r = outcome.Request;
        var source = $"манекен «{BreakdownText.DummyName(data, r.DummyId)}», режим «{BreakdownText.ModeName(r.Mode)}», сид {r.Seed}"
                     + (r.PackSize is { } p ? $", пак {p}" : "") + (r.DummyAttacks ? ", манекен бьет" : "");
        Status("Результат: " + source, Green);
        var note = "результат симуляции: " + source;

        Header("Урон в секунду");
        Number("sim_dps_single", "DPS по одной цели", result.DpsSingleTarget, BreakdownText.Whole, note);
        Number("sim_dps_pack", "DPS по паку", result.DpsPack, BreakdownText.Whole, note);

        string SkillTitle(string id) => data.Skills.TryGetValue(id, out var s) ? s.Name : id;
        var dpsNote = note + "; DPS по всем целям, в сумме дает DPS по паку";

        Header("DPS по скиллам (по всем целям)");
        if (result.DpsBySkill.Count == 0) AddText("нет урона");
        var skillOrder = MeterColors.SkillOrder(Session);
        var maxSkillDps = result.DpsBySkill.Values.DefaultIfEmpty(0).Max();
        foreach (var (id, dps) in result.DpsBySkill.OrderByDescending(kv => kv.Value))
            Number("sim_dps_skill_" + id, SkillTitle(id), dps, BreakdownText.Whole, dpsNote, GameIcons.Skill(id), maxSkillDps, MeterColors.Skill(id, skillOrder));

        Header("DPS по типам урона (по всем целям)");
        if (result.DpsByType.Count == 0) AddText("нет урона");
        foreach (var (type, dps) in result.DpsByType.OrderByDescending(kv => kv.Value))
            Number("sim_dps_type_" + type.ToString().ToLowerInvariant(), BreakdownText.DamageTypeName(type), dps, BreakdownText.Whole, dpsNote, GameIcons.Damage(type));

        Header("Урон за бой по скиллам");
        foreach (var (id, dmg) in result.DamageBySkill.OrderByDescending(kv => kv.Value))
            Number("sim_skill_" + id, SkillTitle(id), dmg, BreakdownText.Whole, note, GameIcons.Skill(id));

        Header("Урон за бой по типам");
        foreach (var (type, dmg) in result.DamageByType.OrderByDescending(kv => kv.Value))
            Number("sim_type_" + type.ToString().ToLowerInvariant(), BreakdownText.DamageTypeName(type), dmg, BreakdownText.Whole, note, GameIcons.Damage(type));

        Header("Попадания и урон по времени");
        Number("sim_hit_damage", "Урон попаданиями", result.HitDamage, BreakdownText.Whole, note);
        Number("sim_dot_damage", "Урон по времени", result.DotDamage, BreakdownText.Whole, note);

        Header("Время");
        if (result.TimeToKill is { } ttk) Number("sim_ttk", "Время убийства, с", ttk, BreakdownText.Seconds, note);
        else AddText("Время убийства: манекен не убит (бессмертен или не успели)");
        if (result.TimeToDeath is { } ttd) Number("sim_ttd", "Время до смерти, с", ttd, BreakdownText.Seconds, note);
        else
        {
            TimeToDeathText = BreakdownText.NoTimeToDeath(r.DummyAttacks, result);
            AddText(TimeToDeathText);
        }

        if (result.Defense is { } d)
        {
            Header("Защита");
            Number("sim_def_attacks", "Атак получено", d.AttacksReceived, BreakdownText.Whole, note);
            Number("sim_def_dodged", "Атак уклонено", d.AttacksDodged, BreakdownText.Whole, note);
            foreach (var (type, dmg) in d.IncomingBeforeMitigation)
                Number("sim_def_incoming_" + type.ToString().ToLowerInvariant(),
                    $"Входящий урон до защиты ({BreakdownText.DamageTypeName(type)})", dmg, BreakdownText.Whole, note, GameIcons.Damage(type));
            Number("sim_def_armor", "Снято броней", d.MitigatedByArmor, BreakdownText.Whole, note);
            Number("sim_def_resist", "Снято сопротивлениями", d.MitigatedByResist, BreakdownText.Whole, note);
            if (result.DamageTaken != null)
                foreach (var (type, dmg) in result.DamageTaken)
                    Number("sim_def_taken_" + type.ToString().ToLowerInvariant(),
                        $"Получено урона ({BreakdownText.DamageTypeName(type)})", dmg, BreakdownText.Whole, note, GameIcons.Damage(type));
            Number("sim_def_regen", "Восстановлено здоровья", d.LifeRegenerated, BreakdownText.Whole, note);
        }

        Header("Ресурс и здоровье по времени");
        if (result.ResourceSeries.Count == 0) AddText("нет данных");
        else
        {
            Graph = new SeriesGraph { CustomMinimumSize = new Vector2(0, 160), SizeFlagsHorizontal = SizeFlags.ExpandFill };
            Graph.SetSeries(result.ResourceSeries);
            Content!.AddChild(Graph);
            var legend = new HFlowContainer();
            legend.AddChild(Swatch(SeriesGraph.ResourceColor));
            legend.AddChild(new Label { Text = "ресурс   " });
            legend.AddChild(Swatch(SeriesGraph.LifeColor));
            legend.AddChild(new Label { Text = "здоровье" });
            Content.AddChild(legend);
            Content.AddChild(new Label { Text = "Каждая линия от 0 до своего максимума за бой.", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        }

        Content!.AddChild(new HSeparator());
        var logButton = new Button
        {
            Text = _logShown ? "Скрыть лог" : $"Показать лог ({result.Log.Count} событий)",
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
        };
        logButton.Pressed += ToggleLog;
        Content.AddChild(logButton);
        if (_logShown)
        {
            Log = new RichTextLabel
            {
                BbcodeEnabled = false,
                ScrollActive = true,
                SelectionEnabled = true,
                CustomMinimumSize = new Vector2(0, 320),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            foreach (var e in result.Log)
            {
                var icon = GameIcons.Ailment(e.AilmentId) ?? GameIcons.Skill(e.SkillId);
                if (icon != null) Log.AddImage(icon, 20, 20);
                Log.AddText($" [{BreakdownText.Seconds(e.Time)} с] {BreakdownText.EventKindName(e.Kind)}: {e.Text}\n");
            }
            Content.AddChild(Log);
        }
    }

    private void Status(string text, Color? color)
    {
        StatusText = text;
        AddText(text, color);
    }

    private void Header(string text)
    {
        Content!.AddChild(RoomHudTheme.TowerHeading(text, 17));
        Content.AddChild(new HSeparator());
    }

    /// <summary>A result number: the formatted value is put into the label, so NumberLabel prints it as is.</summary>
    private void Number(string key, string caption, double value, Func<double, string> format, string note, Texture2D? icon = null, double? maximum = null, Color? barColor = null)
    {
        var label = NumberLabel.For(new NumberLine(key, $"{caption}: {format(value)}", value, null, note), _open);
        label.Icon = icon;
        label.AddThemeConstantOverride("icon_max_width", 20);
        if (key is "sim_dps_single" or "sim_dps_pack")
        {
            label.Text = caption + "\n" + format(value);
            label.AddThemeFontSizeOverride("font_size", 25);
            label.AddThemeColorOverride("font_color", RoomHudTheme.Parchment);
        }
        _numbers.Add(label);
        if (maximum is { } max)
        {
            var row = new Control { CustomMinimumSize = new Vector2(0, 36), SizeFlagsHorizontal = SizeFlags.ExpandFill };
            var track = new ProgressBar { MinValue = 0, MaxValue = Math.Max(1, max), Value = value, ShowPercentage = false, MouseFilter = MouseFilterEnum.Ignore };
            track.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = MeterColors.Track });
            track.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = barColor ?? RoomHudTheme.Focus });
            row.AddChild(track);
            track.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            row.AddChild(label);
            label.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            Content!.AddChild(row);
        }
        else Content!.AddChild(label);
    }

    private static ColorRect Swatch(Color color) =>
        new() { Color = color, CustomMinimumSize = new Vector2(14, 14), SizeFlagsVertical = SizeFlags.ShrinkCenter };

    private static void Row(GridContainer grid, string caption, Control control)
    {
        grid.AddChild(new Label { Text = caption, SizeFlagsHorizontal = SizeFlags.ExpandFill });
        control.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        grid.AddChild(control);
    }
}

/// <summary>
/// The resource and life of <see cref="SimResult.ResourceSeries"/> as two lines on a plain Control, each scaled from 0 to
/// its own maximum over the run (time on the horizontal axis). Only draws; the samples come from the simulator.
/// </summary>
public partial class SeriesGraph : Control
{
    public static readonly Color ResourceColor = new(0.4f, 0.6f, 1f);
    public static readonly Color LifeColor = new(1f, 0.4f, 0.4f);
    private static readonly Color Background = new(0.08f, 0.08f, 0.1f);

    private IReadOnlyList<ResourceSample> _samples = Array.Empty<ResourceSample>();

    public IReadOnlyList<ResourceSample> Samples => _samples;

    public void SetSeries(IReadOnlyList<ResourceSample> samples)
    {
        _samples = samples;
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Background);
        if (_samples.Count < 2 || Size.X < 2 || Size.Y < 2) return;
        DrawSeries(s => s.Resource, ResourceColor);
        DrawSeries(s => s.Life, LifeColor);
    }

    private void DrawSeries(Func<ResourceSample, double> value, Color color)
    {
        double tMax = Math.Max(_samples[^1].Time, 1e-9);
        double vMax = Math.Max(_samples.Max(value), 1e-9);
        const float pad = 4f;
        var points = _samples.Select(s => new Vector2(
            pad + (float)(s.Time / tMax) * (Size.X - 2 * pad),
            Size.Y - pad - (float)(Math.Max(0, value(s)) / vMax) * (Size.Y - 2 * pad))).ToArray();
        DrawPolyline(points, color, 2f, true);
    }
}
