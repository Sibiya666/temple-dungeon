using Dialoid.Core.Explain;
using Dialoid.Sandbox;
using Godot;
using Dialoid.Game.Ui.Art;

namespace Dialoid.Game.Ui;

/// <summary>
/// The «Разбор» tab (spec §13.6): the title of the opened number, its <see cref="BreakdownText.Lines"/>, its Note whenever
/// it has one (resists and dodge carry the capped value there), and buttons for the last <see cref="HistorySize"/> opened
/// numbers. The shown line is a snapshot taken when the number was pressed; after a build change the panel says so.
/// Loading, importing or a class change replaces the build: <see cref="ClearHistory"/> then drops the old build's numbers.
/// </summary>
public partial class BreakdownPanel : SessionPanel, IPadPanel
{
    public const int HistorySize = 10;

    /// <summary>One opened number: the line, where it came from and the session revision it was taken at.</summary>
    public sealed record Entry(NumberLine Line, string? Context, int Revision);

    private readonly List<Entry> _history = new();
    private VBoxContainer _historyList = null!;
    private VBoxContainer _detail = null!;

    /// <summary>The number shown now, or null.</summary>
    public Entry? Current { get; private set; }

    /// <summary>Opened numbers, newest first.</summary>
    public IReadOnlyList<Entry> History => _history;

    /// <summary>The title of the shown number (empty when nothing is open).</summary>
    public string TitleText => Current is { } e ? NumberLabel.TextOf(e.Line, e.Context) : "";

    /// <summary>The explanation lines of the shown number (empty when nothing is open).</summary>
    public IReadOnlyList<string> ShownLines => Current is { } e ? LinesOf(e.Line) : Array.Empty<string>();

    public void Setup(SandboxSession session)
    {
        InitSession(session, withContent: false);
        Theme = RoomHudTheme.TowerWindowTheme();
        AddChild(RoomHudTheme.TowerHeading("Раскрытие числа / источники, модификаторы и история", 22));
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        AddChild(scroll);
        var columns = new TowerColumns { LeftRatio = .30f, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        scroll.AddChild(columns);
        _historyList = new VBoxContainer { Name = "BreakdownHistory", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _detail = new VBoxContainer { Name = "BreakdownDetail", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        columns.AddChild(_historyList);
        columns.AddChild(_detail);
    }

    /// <summary>Opens a number: shows it and puts it on top of the history.</summary>
    public void ShowLine(NumberLine line, string? context = null)
    {
        var entry = new Entry(line, context, Session.Revision);
        _history.RemoveAll(e => e.Line.Key == entry.Line.Key && e.Context == entry.Context);
        _history.Insert(0, entry);
        if (_history.Count > HistorySize) _history.RemoveRange(HistorySize, _history.Count - HistorySize);
        Show(entry);
    }

    /// <summary>Shows an entry and remembers it for B (the gamepad's step back).</summary>
    private void Show(Entry entry)
    {
        if (Current != null && Current != entry) _trail.Add(Current);
        if (_trail.Count > HistorySize) _trail.RemoveAt(0);
        Current = entry;
        RequestRedraw();
    }

    private readonly List<Entry> _trail = new();

    /// <summary>The numbers B steps back through, oldest first.</summary>
    public IReadOnlyList<Entry> Trail => _trail;

    /// <summary>B: the number shown before this one (the nested number's parent); false when there is none (the window closes).</summary>
    public bool Back()
    {
        while (_trail.Count > 0)
        {
            var previous = _trail[^1];
            _trail.RemoveAt(_trail.Count - 1);
            if (!_history.Contains(previous)) continue; // dropped from the history meanwhile
            Current = previous;
            RequestRedraw();
            return true;
        }
        return false;
    }

    public bool PadButton(PadCommand command, Control? focus) => false;

    public bool PadBack() => Back();

    public IEnumerable<(string Button, string Label)> PadPrompts(Control? focus)
    {
        if (_trail.Count > 0) yield return ("[B]", "Назад по истории");
    }

    public void PadClosed() => _trail.Clear();

    /// <summary>Forgets the shown number and the history (the build was replaced).</summary>
    public void ClearHistory()
    {
        _history.Clear();
        _trail.Clear();
        Current = null;
        RequestRedraw();
    }

    /// <summary>Breakdown lines (or the no-breakdown marker), then the Note when there is one.</summary>
    public static IReadOnlyList<string> LinesOf(NumberLine line)
    {
        var lines = new List<string>();
        if (line.Breakdown is { } b)
        {
            lines.Add($"Стат: {BreakdownText.Title(b.Stat)}");
            lines.AddRange(BreakdownText.Lines(b));
        }
        else if (NumberLabel.IsInput(line))
        {
            lines.Add("Значение задано владельцем, ядро его не вычисляет.");
        }
        else
        {
            lines.Add("Производное число, разбора по модификаторам нет.");
        }
        if (line.Note != null) lines.Add("Пояснение: " + line.Note);
        return lines;
    }

    protected override void DrawContent()
    {
        foreach (var container in new[] { _historyList, _detail })
            foreach (var child in container.GetChildren())
            {
                container.RemoveChild(child);
                child.QueueFree();
            }
        _historyList.AddChild(RoomHudTheme.TowerHeading($"ИСТОРИЯ / ПОСЛЕДНИЕ {HistorySize}", 15));
        _historyList.AddChild(new HSeparator());
        foreach (var entry in _history)
        {
            var e = entry;
            var item = NumberLabel.For(e.Line, (_, _) => Show(e), e.Context);
            item.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            item.TooltipText = NumberLabel.TextOf(e.Line, e.Context);
            _historyList.AddChild(item);
        }
        _detail.AddChild(RoomHudTheme.TowerHeading("СНИМОК ВЫБРАННОГО ЧИСЛА", 15));
        _detail.AddChild(new HSeparator());
        if (Current is not { } cur)
        {
            DetailText("Нажмите на любое число, чтобы увидеть его разбор.");
        }
        else
        {
            if (cur.Context != null) DetailText(cur.Context, RoomHudTheme.MutedText);
            _detail.AddChild(RoomHudTheme.TowerHeading(cur.Line.Label, 26));
            _detail.AddChild(RoomHudTheme.TowerHeading(BreakdownText.Value(cur.Line.Value), 38));
            _detail.AddChild(new HSeparator());
            foreach (var line in ShownLines) DetailText(line);
            if (cur.Revision != Session.Revision)
                DetailText("Билд изменился после открытия: это снимок. Откройте число заново для свежего разбора.", Amber);
        }
    }

    private void DetailText(string text, Color? color = null)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        if (color != null) label.AddThemeColorOverride("font_color", color.Value);
        _detail.AddChild(label);
    }
}
