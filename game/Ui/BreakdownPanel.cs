using Dialoid.Core.Explain;
using Dialoid.Sandbox;
using Godot;

namespace Dialoid.Game.Ui;

/// <summary>
/// The «Разбор» tab (spec §13.6): the title of the opened number, its <see cref="BreakdownText.Lines"/>, its Note whenever
/// it has one (resists and dodge carry the capped value there), and buttons for the last <see cref="HistorySize"/> opened
/// numbers. The shown line is a snapshot taken when the number was pressed; after a build change the panel says so.
/// </summary>
public partial class BreakdownPanel : SessionPanel
{
    public const int HistorySize = 10;

    /// <summary>One opened number: the line, where it came from and the session revision it was taken at.</summary>
    public sealed record Entry(NumberLine Line, string? Context, int Revision);

    private readonly List<Entry> _history = new();

    /// <summary>The number shown now, or null.</summary>
    public Entry? Current { get; private set; }

    /// <summary>Opened numbers, newest first.</summary>
    public IReadOnlyList<Entry> History => _history;

    /// <summary>The title of the shown number (empty when nothing is open).</summary>
    public string TitleText => Current is { } e ? NumberLabel.TextOf(e.Line, e.Context) : "";

    /// <summary>The explanation lines of the shown number (empty when nothing is open).</summary>
    public IReadOnlyList<string> ShownLines => Current is { } e ? LinesOf(e.Line) : Array.Empty<string>();

    public void Setup(SandboxSession session) => InitSession(session);

    /// <summary>Opens a number: shows it and puts it on top of the history.</summary>
    public void ShowLine(NumberLine line, string? context = null)
    {
        var entry = new Entry(line, context, Session.Revision);
        _history.RemoveAll(e => e.Line.Key == entry.Line.Key && e.Context == entry.Context);
        _history.Insert(0, entry);
        if (_history.Count > HistorySize) _history.RemoveRange(HistorySize, _history.Count - HistorySize);
        Current = entry;
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
        if (Current is not { } cur)
        {
            AddText("Нажмите на любое число, чтобы увидеть его разбор.");
        }
        else
        {
            AddText(TitleText).AddThemeFontSizeOverride("font_size", 20);
            foreach (var line in ShownLines) AddText(line);
            if (cur.Revision != Session.Revision)
                AddText("Билд изменился после открытия: это снимок. Откройте число заново для свежего разбора.", Amber);
        }

        Content!.AddChild(new HSeparator());
        AddText($"История (последние {HistorySize}):");
        foreach (var entry in _history)
        {
            var e = entry;
            Content.AddChild(NumberLabel.For(e.Line, (_, _) => { Current = e; RequestRedraw(); }, e.Context));
        }
    }
}
