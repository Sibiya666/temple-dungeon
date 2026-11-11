using Dialoid.Core.Explain;
using Dialoid.Sandbox;
using Godot;
using Dialoid.Game.Ui.Art;

namespace Dialoid.Game.Ui;

/// <summary>
/// The «Журнал» window (spec §7, plan 4 task 11): the lines of <see cref="TrainingRoom.Log"/> in three columns (time, who, what
/// happened), filtered by kind (DoT ticks hidden by default, <see cref="TrainingRoom.DefaultHiddenKinds"/>) and by skill through
/// <see cref="TrainingRoom.Filter"/>. The rows are kept in sync incrementally (new lines appended, trimmed ones dropped, a collapsed
/// refusal updated in place), at most <see cref="RefreshSec"/> apart, only while the window is open. Autoscroll follows the newest
/// line until the owner scrolls up, and comes back when he scrolls to the bottom (or ticks «Автопрокрутка»). The name column is
/// as wide as the longest skill name of the data (at least «Возвращающийся топор»), so a name is never cut. The tree takes no
/// keyboard focus: hotkeys and skill keys keep working over it. The texts are the sandbox's (no rules here).
/// </summary>
public partial class CombatLogWindow : SessionPanel, IPadPanel
{
    /// <summary>Most often the rows are synced (seconds).</summary>
    public const double RefreshSec = 0.25;

    /// <summary>The name the name column must hold whole (spec §7), besides every skill name of the data.</summary>
    public const string LongestNameSample = "Возвращающийся топор";

    /// <summary>Kinds in filter order, with their Russian names (the sandbox's event dictionary where it has one).</summary>
    public static readonly IReadOnlyList<(LogKind Kind, string Name)> Kinds =
    [
        (LogKind.Use, Capital(BreakdownText.EventKindName("use"))),
        (LogKind.Hit, Capital(BreakdownText.EventKindName("hit"))),
        (LogKind.Crit, Capital(BreakdownText.EventKindName("crit"))),
        (LogKind.Ailment, Capital(BreakdownText.EventKindName("ailment"))),
        (LogKind.DotTick, "Тики недугов и зон"),
        (LogKind.Proc, Capital(BreakdownText.EventKindName("proc"))),
        (LogKind.Mechanic, Capital(BreakdownText.EventKindName("mechanic"))),
        (LogKind.Refusal, "Отказы"),
        (LogKind.System, "Сообщения комнаты"),
    ];

    private const string Separator = " · ";

    /// <summary>Rows use a slightly smaller font than the window, so the event column keeps a whole hit on one or two lines.</summary>
    public const int RowFontSize = 14;

    /// <summary>Space between rows, pixels (a 14 px line plus this is a ~22 px row).</summary>
    public const int RowPadding = 2;

    private static readonly System.Text.RegularExpressions.Regex DigitGroups = new(@"(?<=\d) (?=\d)", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    private const int TimeColumn = 0, WhoColumn = 1, WhatColumn = 2;

    private static readonly Color TextColor = new("d8d5c6");
    private static readonly Color CritColor = new("e6d8cd");
    private static readonly Color RefusalColor = new(1f, 0.55f, 0.48f);
    private static readonly Color SystemColor = new("9baeb3");
    private static readonly Color DotColor = new(0.85f, 0.66f, 0.45f);
    private static readonly Color EffectColor = new(0.62f, 0.85f, 0.6f);

    private TrainingRoom _room = null!;
    private readonly HashSet<LogKind> _kinds = new();
    private readonly List<CombatLogEntry> _shown = new();
    private readonly List<string?> _skillChoices = new();
    private string? _skillId;
    private Tree _tree = null!;
    private MenuButton _kindsMenu = null!;
    private OptionButton _skillOption = null!;
    private CheckBox _follow = null!;
    private Label _status = null!;
    private VScrollBar? _scroll;
    private bool _followTail = true;
    private float _wrapWidth;
    private IReadOnlyList<string> _skillOrder = Array.Empty<string>();
    private bool _rewrap;
    private bool _syncing;
    private bool _dirty = true;
    private bool _subscribed;
    private bool _wasVisible;
    private double _sinceRefresh = RefreshSec;
    private bool _wideRows;

    /// <summary>The kinds shown now.</summary>
    public IReadOnlySet<LogKind> VisibleKinds => _kinds;

    /// <summary>The skill the log is filtered by, or null for every skill.</summary>
    public string? SkillFilter => _skillId;

    /// <summary>The entries shown as rows, oldest first (the filter's result at the last sync).</summary>
    public IReadOnlyList<CombatLogEntry> ShownEntries => _shown;

    /// <summary>The rows.</summary>
    public Tree Lines => _tree;

    /// <summary>The kind filter (its popup holds one check item per <see cref="Kinds"/> entry, id = the kind).</summary>
    public MenuButton KindsMenu => _kindsMenu;

    /// <summary>The skill filter; item i is <see cref="SkillChoices"/>[i] (null: every skill).</summary>
    public OptionButton SkillOption => _skillOption;

    public IReadOnlyList<string?> SkillChoices => _skillChoices;

    /// <summary>Autoscroll follows the newest line (off after the owner scrolled up).</summary>
    public bool FollowTail => _followTail;

    /// <summary>The vertical scroll bar of the rows (null before the tree made it).</summary>
    public VScrollBar? Scroll => _scroll;

    /// <summary>The line under the rows: how many are shown and which kinds are hidden.</summary>
    public string StatusText => _status.Text;

    public void Setup(SandboxSession session, TrainingRoom room)
    {
        InitSession(session, withContent: false);
        _room = room;
        foreach (var (kind, _) in Kinds)
            if (!TrainingRoom.DefaultHiddenKinds.Contains(kind)) _kinds.Add(kind);
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
        UpdateRowPresentation();
        _sinceRefresh += delta;
        var visible = IsVisibleInTree();
        var opened = visible && !_wasVisible;
        _wasVisible = visible;
        if (visible && _dirty && (opened || _sinceRefresh >= RefreshSec)) Refresh();
        // The tree lays out its rows a frame after they are added: keep following until it really is at the bottom.
        else if (visible && _followTail && !AtBottom()) ScrollToEnd();
    }

    // ---------- handlers (the controls and the self-test call these) ----------

    /// <summary>Shows or hides a kind of line.</summary>
    public void SetKindVisible(LogKind kind, bool visible)
    {
        if (visible) _kinds.Add(kind);
        else _kinds.Remove(kind);
        var popup = _kindsMenu.GetPopup();
        var at = popup.GetItemIndex((int)kind);
        if (at >= 0) popup.SetItemChecked(at, visible);
        _dirty = true;
        RequestRedraw();
    }

    /// <summary>Filters by one skill (null: every skill).</summary>
    public void SetSkillFilter(string? skillId)
    {
        _skillId = skillId;
        var at = _skillChoices.IndexOf(skillId);
        if (at >= 0 && _skillOption.Selected != at) _skillOption.Select(at);
        _dirty = true;
        RequestRedraw();
    }

    /// <summary>Turns autoscroll on (jumps to the newest line) or off.</summary>
    public void SetFollow(bool follow)
    {
        _followTail = follow;
        _follow.SetPressedNoSignal(follow);
        if (follow) ScrollToEnd();
    }

    /// <summary>Syncs the rows with the log now (the self-test calls it; the window calls it at most every <see cref="RefreshSec"/>).</summary>
    public void Refresh()
    {
        _dirty = false;
        _sinceRefresh = 0;
        SyncSkillChoices();
        _skillOrder = MeterColors.SkillOrder(Session);
        var entries = _room.Filter(_kinds, _skillId);
        Sync(entries);
        var hidden = Kinds.Where(k => !_kinds.Contains(k.Kind)).Select(k => k.Name.ToLowerInvariant()).ToList();
        _status.Text = $"Строк {RuNumber.Grouped(entries.Count)} из {RuNumber.Grouped(_room.Log.Count)}" +
                       (hidden.Count > 0 ? $" · скрыты: {string.Join(", ", hidden)}" : "");
    }

    /// <summary>True when the rows are scrolled to the newest line (or all fit).</summary>
    public bool AtBottom() => _scroll is not { } s || s.MaxValue - s.Page <= s.MinValue + 0.5 || s.Value >= s.MaxValue - s.Page - 1;

    /// <summary>The width the name column needs: the widest of the data's skill names and <see cref="LongestNameSample"/>.</summary>
    public float NameTextWidth()
    {
        var font = _tree.GetThemeFont("font");
        var size = _tree.GetThemeFontSize("font_size");
        return Session.Data.Skills.Values.Select(s => s.Name).Append(LongestNameSample)
            .Max(n => font.GetStringSize(n, HorizontalAlignment.Left, -1, size).X);
    }

    /// <summary>What a cell needs around its text: the column separation and the inner margins, plus a few pixels of air.</summary>
    public float NameColumnPadding() =>
        2 * _tree.GetThemeConstant("h_separation") + _tree.GetThemeConstant("inner_item_margin_left") + _tree.GetThemeConstant("inner_item_margin_right") + 8;

    /// <summary>Splits a sandbox line into the time, who (skill, on «Рой» the target follows in the third column) and what happened.</summary>
    public static (string Time, string Who, string What) Columns(CombatLogEntry e)
    {
        var parts = e.Text.Split(Separator, 3);
        if (parts.Length == 1) return ("", "", e.Text);
        if (e.Kind == LogKind.System || parts.Length == 2) return (parts[0], "", string.Join(Separator, parts.Skip(1)));
        return (parts[0], parts[1], parts[2]);
    }

    // ---------- building ----------

    private void Build()
    {
        Theme = RoomHudTheme.TowerWindowTheme();
        AddThemeConstantOverride("separation", RoomHudTheme.ItemGap);
        AddChild(RoomHudTheme.TowerHeading("События тренировочной комнаты / время, источник, результат", 16));
        var filters = new HFlowContainer { Name = "Filters" }; // wraps in a narrow log (1366: the log stops left of the combat screen)
        AddChild(filters);

        _kindsMenu = new MenuButton { Name = "Kinds", Text = "Виды событий ▾", Flat = false, FocusMode = FocusModeEnum.None, TooltipText = "Какие строки показывать. Тики недугов скрыты по умолчанию: их много." };
        var popup = _kindsMenu.GetPopup();
        popup.HideOnCheckableItemSelection = false;
        foreach (var (kind, name) in Kinds)
        {
            popup.AddCheckItem(name, (int)kind);
            popup.SetItemChecked(popup.GetItemIndex((int)kind), _kinds.Contains(kind));
        }
        popup.AddSeparator();
        popup.AddItem("Восстановить виды по умолчанию");
        var resetKindsId = popup.GetItemId(popup.ItemCount - 1);
        popup.IdPressed += id =>
        {
            if (id == resetKindsId)
            {
                foreach (var (kind, _) in Kinds)
                    SetKindVisible(kind, !TrainingRoom.DefaultHiddenKinds.Contains(kind));
                return;
            }
            var selected = (LogKind)(int)id;
            SetKindVisible(selected, !_kinds.Contains(selected));
        };
        filters.AddChild(_kindsMenu);

        _skillOption = new OptionButton { Name = "Skill", FocusMode = FocusModeEnum.None, ClipText = true, FitToLongestItem = false, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(110, 0), TooltipText = "Строки одного скилла" };
        _skillOption.ItemSelected += i => SetSkillFilter(i >= 0 && i < _skillChoices.Count ? _skillChoices[(int)i] : null);
        filters.AddChild(_skillOption);

        _follow = new CheckBox { Name = "Follow", Text = "Автопрокрутка", ButtonPressed = true, FocusMode = FocusModeEnum.None, TooltipText = "Следить за новыми строками. Выключается, когда вы прокручиваете вверх." };
        _follow.Toggled += SetFollow;
        filters.AddChild(_follow);

        _tree = new Tree
        {
            Name = "Lines",
            Columns = 3,
            HideRoot = true,
            ColumnTitlesVisible = true,
            SelectMode = Tree.SelectModeEnum.Row,
            FocusMode = FocusModeEnum.None, // typing over the log must not search it: keys stay with the fight and the hotkeys
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 80),
        };
        _tree.AddThemeFontSizeOverride("font_size", RowFontSize);
        var surface = RoomHudTheme.TowerSurface(6);
        surface.BgColor = new Color(.047f, .067f, .078f, .78f);
        surface.SetBorderWidthAll(0);
        _tree.AddThemeStyleboxOverride("panel", surface);
        _tree.AddThemeStyleboxOverride("selected", RoomHudTheme.ReferenceSurface(3, true));
        // A dense chat log: thin row padding and no indent (a flat list), so ~10 rows fit and the event column gets the indent.
        _tree.AddThemeConstantOverride("v_separation", RowPadding);
        _tree.AddThemeConstantOverride("item_margin", 0);
        _tree.SetColumnTitle(TimeColumn, "Время");
        _tree.SetColumnTitle(WhoColumn, "Кто");
        _tree.SetColumnTitle(WhatColumn, "Событие");
        foreach (var c in new[] { TimeColumn, WhoColumn, WhatColumn }) _tree.SetColumnTitleAlignment(c, HorizontalAlignment.Left);
        _tree.CreateItem();
        ButtonPrompts.Skip(_tree); // rows are read, not pressed: the right stick scrolls them, Y jumps to the newest
        AddChild(_tree);
        _scroll = _tree.GetChildren(true).OfType<VScrollBar>().FirstOrDefault();
        if (_scroll != null) _scroll.ValueChanged += _ => OnScrolled();
        _tree.Ready += FitColumns;
        _tree.Resized += OnTreeResized;

        _status = new Label { Name = "Status", AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _status.AddThemeColorOverride("font_color", SystemColor);
        AddChild(_status);
    }

    private void UpdateRowPresentation()
    {
        var wide = Size.X >= 760;
        if (_tree == null || wide == _wideRows) return;
        _wideRows = wide;
        _tree.AddThemeFontSizeOverride("font_size", wide ? 18 : RowFontSize);
        _tree.AddThemeConstantOverride("v_separation", wide ? 8 : RowPadding);
        FitColumns();
        _rewrap = _dirty = true;
    }

    /// <summary>Time and name columns fixed to their widest text (plus the cell margins), the event column takes the rest.</summary>
    private void FitColumns()
    {
        var font = _tree.GetThemeFont("font");
        var size = _tree.GetThemeFontSize("font_size");
        var pad = NameColumnPadding();
        _tree.SetColumnExpand(TimeColumn, false);
        // As wide as the longest time or the column title, whichever is wider.
        var titleFont = _tree.GetThemeFont("title_button_font");
        var titleSize = _tree.GetThemeFontSize("title_button_font_size");
        var time = Math.Max(font.GetStringSize("99,9 с", HorizontalAlignment.Left, -1, size).X,
            titleFont.GetStringSize(_tree.GetColumnTitle(TimeColumn), HorizontalAlignment.Left, -1, titleSize).X);
        _tree.SetColumnCustomMinimumWidth(TimeColumn, (int)Math.Ceiling(time + pad));
        _tree.SetColumnExpand(WhoColumn, false);
        _tree.SetColumnCustomMinimumWidth(WhoColumn, (int)Math.Ceiling(NameTextWidth() + pad));
        _tree.SetColumnExpand(WhatColumn, true);
        _tree.SetColumnClipContent(WhatColumn, true);
    }

    /// <summary>
    /// Breaks the event text into lines that fit <see cref="_wrapWidth"/>, at spaces but never inside a number («12 221»). The tree's
    /// own autowrap measures a row only when it is drawn, so its scroll range lags behind and the view cannot stay at the newest
    /// line; explicit line breaks are measured when the row is added.
    /// </summary>
    private string Wrap(string text)
    {
        if (_wrapWidth <= 0) return text;
        var font = _tree.GetThemeFont("font");
        var size = _tree.GetThemeFontSize("font_size");
        float Width(string s) => font.GetStringSize(s, HorizontalAlignment.Left, -1, size).X;
        if (Width(text) <= _wrapWidth) return text;
        var words = DigitGroups.Replace(text, " ").Split(' ');
        var lines = new List<string>();
        var line = "";
        foreach (var word in words)
        {
            var candidate = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && Width(candidate) > _wrapWidth)
            {
                lines.Add(line);
                line = word;
            }
            else line = candidate;
        }
        lines.Add(line);
        return string.Join("\n", lines).Replace(' ', ' ');
    }

    /// <summary>The event column changed width: the rows are wrapped again at the next sync.</summary>
    private void OnTreeResized()
    {
        // Room for the scroll bar even before it shows (it appears once the rows outgrow the view, without a resize).
        var bar = _scroll is { Visible: false } s ? s.GetCombinedMinimumSize().X : 0;
        var width = _tree.GetColumnWidth(WhatColumn) - NameColumnPadding() - bar;
        if (Math.Abs(width - _wrapWidth) < 1) return;
        _wrapWidth = width;
        _rewrap = true;
        _dirty = true;
    }

    private void OnScrolled()
    {
        // Before Tree has measured its first viewport, its internal scrollbar has the default
        // max=100/page=0 and resets its value asynchronously. That is layout, not owner input.
        // Keep the requested follow state until a real scrollable viewport exists.
        if (_syncing || _scroll is not { Page: > 0 }) return;
        var follow = AtBottom();
        if (follow == _followTail) return;
        _followTail = follow;
        _follow.SetPressedNoSignal(follow);
    }

    private void ScrollToEnd()
    {
        var root = _tree.GetRoot();
        var count = root.GetChildCount();
        if (count == 0) return;
        _syncing = true;
        _tree.ScrollToItem(root.GetChild(count - 1));
        if (_scroll is { Page: > 0 }) _scroll.Value = _scroll.MaxValue;
        _syncing = false;
    }

    /// <summary>The skills offered: every skill of the build's slots, then any other skill that has lines.</summary>
    private void SyncSkillChoices()
    {
        var wanted = new List<string?> { null };
        for (var i = 0; i < SandboxSession.SlotCount; i++)
            if (Session.GetSlot(i) is { } slot && !wanted.Contains(slot.SkillId)) wanted.Add(slot.SkillId);
        foreach (var e in _room.Log)
            if (e.SkillId is { } id && !wanted.Contains(id)) wanted.Add(id);
        if (_skillId != null && !wanted.Contains(_skillId)) wanted.Add(_skillId);
        if (wanted.SequenceEqual(_skillChoices) || _skillOption.GetPopup().Visible) return;
        _skillChoices.Clear();
        _skillChoices.AddRange(wanted);
        _skillOption.Clear();
        foreach (var id in _skillChoices)
            _skillOption.AddItem(id == null ? "Все скиллы" : Session.Data.Skills.TryGetValue(id, out var s) ? s.Name : id);
        _skillOption.Select(_skillChoices.IndexOf(_skillId));
    }

    /// <summary>
    /// Brings the rows to <paramref name="entries"/>: the log only grows at its end, drops its oldest lines in batches and replaces
    /// its last refusal when it collapses a repeat, so the rows keep their common middle and change only at both ends.
    /// </summary>
    private void Sync(IReadOnlyList<CombatLogEntry> entries)
    {
        var root = _tree.GetRoot();
        _syncing = true;
        var start = entries.Count == 0 || _rewrap ? -1 : _shown.FindIndex(e => ReferenceEquals(e, entries[0]));
        _rewrap = false;
        if (start < 0)
        {
            foreach (var child in root.GetChildren()) child.Free();
            _shown.Clear();
        }
        else
        {
            for (var i = 0; i < start; i++) root.GetChild(0).Free();
            _shown.RemoveRange(0, start);
            var same = 0;
            while (same < _shown.Count && same < entries.Count && ReferenceEquals(_shown[same], entries[same])) same++;
            while (_shown.Count > same)
            {
                root.GetChild(_shown.Count - 1).Free();
                _shown.RemoveAt(_shown.Count - 1);
            }
        }
        for (var i = _shown.Count; i < entries.Count; i++) AddRow(root, entries[i]);
        _syncing = false;
        if (_followTail) ScrollToEnd();
    }

    private void AddRow(TreeItem root, CombatLogEntry e)
    {
        var item = _tree.CreateItem(root);
        var (time, who, what) = Columns(e);
        item.SetText(TimeColumn, time);
        item.SetText(WhoColumn, who);
        item.SetText(WhatColumn, Wrap(ShortTypes(what))); // a long hit wraps instead of losing its number; the full line is the tooltip
        // The kind colours only what happened; the name keeps its skill's colour from the meter and the charts.
        item.SetCustomColor(TimeColumn, SystemColor);
        item.SetCustomColor(WhoColumn, e.SkillId is { } skill ? NameColor(skill) : TextColor);
        item.SetCustomColor(WhatColumn, ColorOf(e.Kind));
        for (var c = TimeColumn; c <= WhatColumn; c++) item.SetTooltipText(c, e.Text);
        _shown.Add(e);
    }

    /// <summary>Short type names for a hit of several types (UI review of task 11: on «Рой» a three-type hit took three lines).</summary>
    // The full words are the sandbox's instrumental type names (Texts.DamageTypeInstrumental, internal there).
    private static readonly IReadOnlyList<(string Full, string Short)> TypeShort =
    [
        (" физическим", " физ."),
        (" огнем", " огн."),
        (" холодом", " хол."),
        (" молнией", " молн."),
        (" ядом", " яд."),
    ];

    /// <summary>
    /// «42 физическим + 275 молнией + 1 121 огнем» → «42 физ. + 275 молн. + 1 121 огн.»: a hit of several types keeps every number
    /// on one or two lines; one type keeps its full word. The row's tooltip has the sandbox's full line.
    /// </summary>
    public static string ShortTypes(string what)
    {
        if (!what.Contains(" + ", StringComparison.Ordinal)) return what;
        foreach (var (full, abbr) in TypeShort) what = what.Replace(full, abbr, StringComparison.Ordinal);
        return what;
    }

    // ---------- gamepad ----------

    public bool PadButton(PadCommand command, Control? focus)
    {
        if (command != PadCommand.Extra) return false;
        SetFollow(true); // Y: to the newest line, autoscroll on
        return true;
    }

    public bool PadBack() => false;

    public IEnumerable<(string Button, string Label)> PadPrompts(Control? focus)
    {
        yield return ("[RS]", "Прокрутка");
        yield return ("[Y]", "К последней строке");
    }

    public void PadClosed() { }

    /// <summary>A skill's name colour: its meter colour, lightened for text on the dark window.</summary>
    public Color NameColor(string skillId) => MeterColors.Skill(skillId, _skillOrder).Lerp(Colors.White, 0.45f);

    public static Color ColorOf(LogKind kind) => kind switch
    {
        LogKind.Crit => CritColor,
        LogKind.Refusal => RefusalColor,
        LogKind.System => SystemColor,
        LogKind.DotTick => DotColor,
        LogKind.Ailment or LogKind.Proc or LogKind.Mechanic => EffectColor,
        _ => TextColor,
    };

    private static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
