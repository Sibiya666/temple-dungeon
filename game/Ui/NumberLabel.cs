using System.Text.RegularExpressions;
using Dialoid.Core.Explain;
using Dialoid.Sandbox;
using Godot;

namespace Dialoid.Game.Ui;

/// <summary>
/// A flat button showing one number. Pressing it opens the number's breakdown on the «Разбор» tab (spec §13.6); a number
/// without a breakdown shows its Note there. Every number of the debug scene goes through it.
/// Text rule (<see cref="TextOf"/>): a core Label that already holds digits (e.g. «Сопротивление огню: 40 / кап 75») shows
/// its value itself and is printed as is; any other Label is printed as «Label: value» (<see cref="BreakdownText.Value"/>:
/// whole when integral, else one decimal, Russian comma).
/// </summary>
public partial class NumberLabel : Button
{
    /// <summary>
    /// Key prefix of a number the owner sets (level, skill level...), not one the core computes. Such lines have no breakdown;
    /// the «Разбор» tab marks them as owner-set. Build them with <see cref="InputLine"/>.
    /// </summary>
    public const string InputKeyPrefix = "input_";

    private static readonly Regex Digit = new(@"\d", RegexOptions.CultureInvariant);

    /// <summary>The shown number.</summary>
    public NumberLine Line { get; private set; } = new("", "", 0, null);

    /// <summary>Where the number comes from (e.g. a skill name), shown in the text and the breakdown; null for the character.</summary>
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

    /// <summary>An owner-set number (no breakdown); <paramref name="note"/> says where it is set.</summary>
    public static NumberLine InputLine(string key, string label, double value, string note) =>
        new(InputKeyPrefix + key, label, value, null, note);

    /// <summary>True for a line made by <see cref="InputLine"/>.</summary>
    public static bool IsInput(NumberLine line) => line.Key.StartsWith(InputKeyPrefix, StringComparison.Ordinal);

    /// <summary>Text of a line: «[context — ]Label» when the Label holds digits, else «[context — ]Label: value».</summary>
    public static string TextOf(NumberLine line, string? context = null)
    {
        var body = Digit.IsMatch(line.Label) ? line.Label : $"{line.Label}: {BreakdownText.Value(line.Value)}";
        return context == null ? body : context + " — " + body;
    }

    /// <summary>Replaces the shown number.</summary>
    public void SetLine(NumberLine line)
    {
        Line = line;
        Text = TextOf(line, Context);
        TooltipText = line.Note ?? (line.Breakdown != null ? "Нажмите, чтобы открыть разбор" : "");
    }

    /// <summary>What pressing the label does; the self-test calls it directly.</summary>
    public void Open() => _open?.Invoke(Line, Context);
}
