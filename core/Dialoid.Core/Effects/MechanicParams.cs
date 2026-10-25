using System.Text.Json;

namespace Dialoid.Core.Effects;

/// <summary>Typed reads of <see cref="MechanicEffect.Params"/>. A missing param yields the fallback; a param of the wrong type throws.</summary>
public static class MechanicParams
{
    public static double Number(MechanicEffect e, string name, double fallback)
    {
        if (!e.Params.TryGetValue(name, out var v)) return fallback;
        return v.ValueKind == JsonValueKind.Number
            ? v.GetDouble()
            : throw new ArgumentException($"Mechanic '{e.MechanicId}': param '{name}' must be a number.");
    }

    public static string Text(MechanicEffect e, string name, string fallback)
    {
        if (!e.Params.TryGetValue(name, out var v)) return fallback;
        return v.ValueKind == JsonValueKind.String
            ? v.GetString()!
            : throw new ArgumentException($"Mechanic '{e.MechanicId}': param '{name}' must be a string.");
    }
}
