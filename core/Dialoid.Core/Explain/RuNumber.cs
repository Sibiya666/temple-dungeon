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
}
