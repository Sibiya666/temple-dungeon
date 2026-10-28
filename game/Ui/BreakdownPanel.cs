using Dialoid.Core.Explain;
using Dialoid.Sandbox;
using Godot;

namespace Dialoid.Game.Ui;

/// <summary>
/// The «Разбор» tab (spec §13.6): the title of the opened number, its <see cref="BreakdownText.Lines"/> (or the Note of a
/// derived number) and buttons for the last <see cref="HistorySize"/> opened numbers. The shown line is a snapshot taken
/// when the number was pressed; after a build change the panel says so.
/// </summary>
public partial class BreakdownPanel : VBoxContainer
{
    public const int HistorySize = 10;

    /// <summary>One opened number: the line, where it came from and the session revision it was taken at.</summary>
    public sealed record Entry(NumberLine Line, string? Context, int Revision);

    private SandboxSession _session = null!;
    private readonly List<Entry> _history = new();
    private VBoxContainer _content = null!;

    /// <summary>The number shown now, or null.</summary>
    public Entry? Current { get; private set; }

    /// <summary>Opened numbers, newest first.</summary>
    public IReadOnlyList<Entry> History => _history;

    /// <summary>The title text shown now (empty when nothing is open).</summary>
    public string TitleText { get; private set; } = "";

    /// <summary>The breakdown lines shown now.</summary>
    public IReadOnlyList<string> ShownLines { get; private set; } = Array.Empty<string>();

    public void Setup(SandboxSession session)
    {
        _session = session;
        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        AddChild(scroll);
        _content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(_content);
        _session.Changed += Redraw;
        Redraw();
    }

    public override void _ExitTree()
    {
        if (_session != null) _session.Changed -= Redraw;
    }

    /// <summary>Opens a number: shows it and puts it on top of the history.</summary>
    public void ShowLine(NumberLine line, string? context = null)
    {
        var entry = new Entry(line, context, _session.Revision);
        _history.RemoveAll(e => SameNumber(e, entry));
        _history.Insert(0, entry);
        if (_history.Count > HistorySize) _history.RemoveRange(HistorySize, _history.Count - HistorySize);
        Current = entry;
        Redraw();
    }

    private static bool SameNumber(Entry a, Entry b) => a.Line.Key == b.Line.Key && a.Context == b.Context;

    private static string TitleOf(Entry e) => NumberLabel.TextOf(e.Line, e.Context);

    private void Redraw()
    {
        foreach (var child in _content.GetChildren())
        {
            _content.RemoveChild(child);
            child.QueueFree();
        }

        if (Current is not { } cur)
        {
            TitleText = "";
            ShownLines = Array.Empty<string>();
            AddText("Нажмите на любое число, чтобы увидеть его разбор.");
        }
        else
        {
            TitleText = TitleOf(cur);
            var title = AddText(TitleText);
            title.AddThemeFontSizeOverride("font_size", 20);
            if (cur.Line.Breakdown is { } b)
            {
                AddText($"Стат: {BreakdownText.Title(b.Stat)}");
                ShownLines = BreakdownText.Lines(b);
            }
            else
            {
                ShownLines = new[] { "Производное число, разбора по модификаторам нет.", "Из чего состоит: " + (cur.Line.Note ?? "—") };
            }
            foreach (var line in ShownLines) AddText(line);
            if (cur.Revision != _session.Revision)
            {
                var stale = AddText("Билд изменился после открытия: это снимок. Откройте число заново для свежего разбора.");
                stale.AddThemeColorOverride("font_color", new Color(1f, 0.8f, 0.4f));
            }
        }

        _content.AddChild(new HSeparator());
        AddText($"История (последние {HistorySize}):");
        foreach (var entry in _history)
        {
            var e = entry;
            _content.AddChild(NumberLabel.For(e.Line, (_, _) => { Current = e; Redraw(); }, e.Context));
        }
    }

    private Label AddText(string text)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _content.AddChild(label);
        return label;
    }
}
