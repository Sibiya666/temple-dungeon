using System.Globalization;

namespace Dialoid.Core.Explain;

/// <summary>
/// The one Russian number convention shown to the owner: an invariant .NET format pattern, then the decimal comma and the
/// Unicode minus; a negative value that formats to zero ("-0", "-0.0") loses its sign.
/// </summary>
public static class RuNumber
{
    /// <summary>Formats <paramref name="v"/> with the invariant <paramref name="pattern"/> ("0.##", "0.0", ...) in Russian style.</summary>
    public static string Format(double v, string pattern)
    {
        var text = v.ToString(pattern, CultureInfo.InvariantCulture);
        if (text.StartsWith('-'))
        {
            var body = text[1..];
            text = body.Trim('0', '.').Length == 0 ? body : "−" + body; // "-0" becomes "0"
        }
        return text.Replace('.', ',');
    }

    /// <summary>A whole number (rounded half away from zero) with a space between digit groups: «12 345», «−1 234 568».</summary>
    public static string Grouped(double v) =>
        Format(Math.Round(v, MidpointRounding.AwayFromZero), "#,0").Replace(',', ' ');

    /// <summary>
    /// The meter's short form (spec 7): below a thousand a whole number, then one decimal with «тыс.», «млн», «млрд»:
    /// «812», «1,2 тыс.», «3,4 млн». A value that rounds up to the next unit moves to it (999 960 is «1 млн»).
    /// </summary>
    public static string Short(double v)
    {
        var abs = Math.Abs(v);
        if (Math.Round(abs, MidpointRounding.AwayFromZero) < 1000) return Format(Math.Round(v, MidpointRounding.AwayFromZero), "0");
        (double Scale, string Unit)[] units = { (1e3, "тыс."), (1e6, "млн"), (1e9, "млрд") };
        for (var i = 0; i < units.Length; i++)
        {
            var scaled = Math.Round(abs / units[i].Scale, 1, MidpointRounding.AwayFromZero);
            if (scaled < 1000 || i == units.Length - 1)
                return Format(Math.Sign(v) * scaled, "0.#") + " " + units[i].Unit;
        }
        throw new InvalidOperationException("unreachable");
    }
}
