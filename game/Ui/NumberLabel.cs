using Dialoid.Core.Explain;
using Dialoid.Sandbox;
using Godot;

namespace Dialoid.Game.Ui;

/// <summary>
/// A flat button showing one number as «Label: value». Pressing it opens the number's breakdown on the «Разбор» tab
/// (spec §13.6); a derived number without a breakdown shows its Note there. Every number of the debug scene goes through it.
/// </summary>
public partial class NumberLabel : Button
{
    /// <summary>The shown number.</summary>
    public NumberLine Line { get; private set; } = new("", "", 0, null);

    /// <summary>Where the number comes from (e.g. a skill name), shown in the breakdown; null for the character.</summary>
    public string? Context { get; private set; }

    private Action<NumberLine, string?>? _open;

    /// <summary>A label for the line; <paramref name="open"/> is <see cref="Main.ShowBreakdown"/>.</summary>
    public static NumberLabel For(NumberLine line, Action<NumberLine, string?> open, string? context = null)
    {
        var label = new NumberLabel { Flat = true, Alignment = HorizontalAlignment.Left, _open = open, Context = context };
        label.SetLine(line);
        label.Pressed += label.Open;
        return label;
    }

    /// <summary>Text of a line: «[context — ]Label: value» with one decimal and the Russian comma.</summary>
    public static string TextOf(NumberLine line, string? context = null) =>
        (context == null ? "" : context + " — ") + $"{line.Label}: {BreakdownText.Number(line.Value)}";

    /// <summary>Replaces the shown number.</summary>
    public void SetLine(NumberLine line)
    {
        Line = line;
        Text = TextOf(line, Context);
        TooltipText = line.Breakdown == null ? line.Note ?? "" : "Нажмите, чтобы открыть разбор";
    }

    /// <summary>What pressing the label does; the self-test calls it directly.</summary>
    public void Open() => _open?.Invoke(Line, Context);
}
