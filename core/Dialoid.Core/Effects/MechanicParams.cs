using System.Text.Json;

namespace Dialoid.Core.Effects;

/// <summary>Typed reads of <see cref="MechanicEffect.Params"/>. A missing param yields the fallback; a param of the wrong type throws
/// (a backstop: loaded data is checked against <see cref="IMechanic.ParamSchema"/>, so only hand-built data gets here).</summary>
public static class MechanicParams
{
    public static double Number(MechanicEffect e, string name, double fallback)
    {
        if (!e.Params.TryGetValue(name, out var v)) return fallback;
        return v.ValueKind == JsonValueKind.Number
            ? v.GetDouble()
            : throw new ArgumentException($"Mechanic '{e.MechanicId}': param '{name}' must be a number.");
    }

    /// <summary>A list of strings: a JSON array of strings, or one string (one item).</summary>
    public static IReadOnlyList<string> Texts(MechanicEffect e, string name, IReadOnlyList<string> fallback)
    {
        if (!e.Params.TryGetValue(name, out var v)) return fallback;
        if (v.ValueKind == JsonValueKind.String) return new[] { v.GetString()! };
        if (v.ValueKind == JsonValueKind.Array && v.EnumerateArray().All(x => x.ValueKind == JsonValueKind.String))
            return v.EnumerateArray().Select(x => x.GetString()!).ToList();
        throw new ArgumentException($"Mechanic '{e.MechanicId}': param '{name}' must be a string or an array of strings.");
    }

    public static string Text(MechanicEffect e, string name, string fallback)
    {
        if (!e.Params.TryGetValue(name, out var v)) return fallback;
        return v.ValueKind == JsonValueKind.String
            ? v.GetString()!
            : throw new ArgumentException($"Mechanic '{e.MechanicId}': param '{name}' must be a string.");
    }
}
