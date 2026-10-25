using System.Text.Json;
using Dialoid.Core.Stats;

namespace Dialoid.Core.Data.Json;

/// <summary>Reads the short modifier form: {"stat","kind","value","tags","per","conditions","priority"}.</summary>
public static class ModifierJson
{
    public static Modifier? Read(JsonElement e, SourceRef source, List<string> errors)
    {
        int before = errors.Count;
        if (e.ValueKind != JsonValueKind.Object) { errors.Add("Модификатор должен быть объектом"); return null; }

        string? stat = Str(e, "stat");
        if (string.IsNullOrEmpty(stat)) errors.Add("У модификатора не задан «stat»");

        ModKind kind = ModKind.Flat;
        var kindText = Str(e, "kind");
        if (kindText == null || !Enum.TryParse(kindText, true, out kind) || !Enum.IsDefined(kind) || int.TryParse(kindText, out _))
            errors.Add($"Неизвестный вид модификатора «{kindText}» (ожидается flat, increased, more или override)");

        double value = 0;
        if (!e.TryGetProperty("value", out var v) || v.ValueKind != JsonValueKind.Number) errors.Add("У модификатора не задано числовое «value»");
        else value = v.GetDouble();

        var tags = TagSet.Empty;
        if (e.TryGetProperty("tags", out var t))
        {
            if (t.ValueKind != JsonValueKind.Array) errors.Add("«tags» должен быть массивом строк");
            else tags = TagSet.Of(t.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToArray());
        }

        PerStat? per = null;
        if (e.TryGetProperty("per", out var p))
        {
            var ps = Str(p, "stat");
            if (p.ValueKind != JsonValueKind.Object || string.IsNullOrEmpty(ps) || !p.TryGetProperty("every", out var ev) || ev.ValueKind != JsonValueKind.Number || ev.GetDouble() <= 0)
                errors.Add("«per» требует «stat» и положительное «every»");
            else
                per = new PerStat(ps, ev.GetDouble(), p.TryGetProperty("cap", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetDouble() : null);
        }

        var conditions = new List<Condition>();
        if (e.TryGetProperty("conditions", out var cs))
        {
            if (cs.ValueKind != JsonValueKind.Array) errors.Add("«conditions» должен быть массивом");
            else foreach (var c in cs.EnumerateArray())
            {
                if (c.ValueKind == JsonValueKind.String) conditions.Add(new Condition(c.GetString()!));
                else if (c.ValueKind == JsonValueKind.Object && Str(c, "id") is { Length: > 0 } cid)
                    conditions.Add(new Condition(cid,
                        c.TryGetProperty("param", out var pp) && pp.ValueKind == JsonValueKind.Number ? pp.GetDouble() : null,
                        Str(c, "ref")));
                else errors.Add("Условие должно быть строкой или объектом с «id»");
            }
        }

        int priority = e.TryGetProperty("priority", out var pr) && pr.ValueKind == JsonValueKind.Number ? pr.GetInt32() : 0;

        if (errors.Count > before) return null;
        return new Modifier(stat!, kind, value, tags, conditions, per, source, priority);
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
