using System.Text.Json;

namespace Dialoid.Core.Data.Json;

/// <summary>
/// Rejects object properties that no reader knows about, so a typo in a data file is a load error instead of a silently ignored field.
/// Allowed-field sets live next to the reader of each object shape. Objects whose keys are data (resists, spell_damage, ...) are not checked here.
/// </summary>
internal static class StrictJson
{
    public static HashSet<string> Fields(params string[] names) => new(names, StringComparer.Ordinal);

    public static string Message(string name) => $"Неизвестное поле «{name}»";

    /// <summary>Names of the object's properties that are not in <paramref name="allowed"/> (empty for non-objects).</summary>
    public static IEnumerable<string> Unknown(JsonElement obj, IReadOnlySet<string> allowed) =>
        obj.ValueKind == JsonValueKind.Object
            ? obj.EnumerateObject().Select(p => p.Name).Where(n => !allowed.Contains(n))
            : Enumerable.Empty<string>();

    public static void RejectUnknown(JsonElement obj, IReadOnlySet<string> allowed, string file, string? id, List<DataError> errors)
    {
        foreach (var name in Unknown(obj, allowed))
            errors.Add(new DataError(file, id, Message(name)));
    }

    /// <summary>Same as <see cref="RejectUnknown"/> for each element of an array property (missing or non-array: nothing to check).</summary>
    public static void RejectUnknownIn(JsonElement parent, string arrayProperty, IReadOnlySet<string> allowed, string file, string? id, List<DataError> errors)
    {
        if (parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(arrayProperty, out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var item in arr.EnumerateArray()) RejectUnknown(item, allowed, file, id, errors);
    }

    /// <summary>Nested object property (missing or non-object: nothing to check).</summary>
    public static bool TryObject(JsonElement parent, string property, out JsonElement child)
    {
        child = default;
        return parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(property, out child) && child.ValueKind == JsonValueKind.Object;
    }
}
