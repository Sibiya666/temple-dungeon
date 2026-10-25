using System.Text.Json;
using Dialoid.Core.Combat;
using Dialoid.Core.Data;
using Dialoid.Core.Stats;

namespace Dialoid.Core.Effects;

/// <summary>What a mechanic param holds. Reference kinds must name something that exists in the loaded data.</summary>
public enum MechanicParamKind
{
    /// <summary>A JSON number.</summary>
    Number,
    /// <summary>Any string (e.g. a buff id).</summary>
    Text,
    /// <summary>A stat id from stats.json.</summary>
    Stat,
    /// <summary>An ailment id from ailments.json.</summary>
    Ailment,
    /// <summary>A skill id from skills.json.</summary>
    Skill,
    /// <summary>One skill id or an array of skill ids.</summary>
    SkillList,
    /// <summary>A damage type: physical, fire, cold, lightning or poison.</summary>
    DamageType,
    /// <summary>An attribute stat id: str, int, will or dex.</summary>
    Attribute,
    /// <summary>A condition id known to <see cref="ConditionCatalog"/>.</summary>
    Condition,
}

/// <summary>One declared mechanic param (spec 4.6). Required params must be present; the others fall back to defaults.</summary>
public sealed record MechanicParam(string Name, MechanicParamKind Kind, bool Required = false);

/// <summary>The scopes a mechanic works in (the scope of the effect that carries it).</summary>
[Flags]
public enum MechanicScopes
{
    Global = 1,
    Skill = 2,
    Tags = 4,
    /// <summary>Works on its own skills too: a scoped copy fires only for the skills it landed on.</summary>
    Any = Global | Skill | Tags,
}

/// <summary>Load-time checks of a mechanic effect against the mechanic's declared params and scopes (Russian messages).</summary>
public static class MechanicParamSchema
{
    private static readonly string[] Attributes = { "str", "int", "will", "dex" };

    public static MechanicScopes ScopeOf(Scope scope) => scope switch
    {
        Scope.Global => MechanicScopes.Global,
        Scope.Skill => MechanicScopes.Skill,
        _ => MechanicScopes.Tags,
    };

    /// <summary>
    /// Unknown names, wrong JSON types, missing required params and references to data that does not exist.
    /// <paramref name="schema"/> null: the mechanic declares no schema and its params are not checked.
    /// </summary>
    public static IEnumerable<string> Validate(string mechanicId, IReadOnlyList<MechanicParam>? schema, MechanicEffect effect, GameData data)
    {
        if (schema == null) yield break;
        var byName = schema.ToDictionary(p => p.Name, StringComparer.Ordinal);
        foreach (var name in effect.Params.Keys.OrderBy(k => k, StringComparer.Ordinal))
            if (!byName.ContainsKey(name))
                yield return schema.Count == 0
                    ? $"механика «{mechanicId}» не принимает параметров, а задан «{name}»"
                    : $"механика «{mechanicId}»: неизвестный параметр «{name}» (допустимы: {string.Join(", ", schema.Select(p => p.Name))})";

        foreach (var p in schema)
        {
            if (!effect.Params.TryGetValue(p.Name, out var v))
            {
                if (p.Required) yield return $"механика «{mechanicId}»: не задан обязательный параметр «{p.Name}»";
                continue;
            }
            if (Check(p, v, data) is { } error) yield return $"механика «{mechanicId}»: параметр «{p.Name}» {error}";
        }
    }

    private static string? Check(MechanicParam p, JsonElement v, GameData data)
    {
        if (p.Kind == MechanicParamKind.Number)
            return v.ValueKind == JsonValueKind.Number ? null : "должен быть числом";
        if (p.Kind == MechanicParamKind.SkillList)
        {
            if (v.ValueKind == JsonValueKind.Array)
            {
                if (!v.EnumerateArray().All(x => x.ValueKind == JsonValueKind.String)) return "должен быть строкой или массивом строк";
                var missing = v.EnumerateArray().Select(x => x.GetString()!).Where(s => !data.Skills.ContainsKey(s)).ToList();
                return missing.Count == 0 ? null : $"ссылается на неизвестный скилл «{missing[0]}»";
            }
            if (v.ValueKind != JsonValueKind.String) return "должен быть строкой или массивом строк";
            return data.Skills.ContainsKey(v.GetString()!) ? null : $"ссылается на неизвестный скилл «{v.GetString()}»";
        }
        if (v.ValueKind != JsonValueKind.String) return "должен быть строкой";
        var s = v.GetString()!;
        return p.Kind switch
        {
            MechanicParamKind.Stat when !data.Stats.ContainsKey(s) => $"ссылается на неизвестный стат «{s}»",
            MechanicParamKind.Ailment when !data.Ailments.ContainsKey(s) => $"ссылается на неизвестный недуг «{s}»",
            MechanicParamKind.Skill when !data.Skills.ContainsKey(s) => $"ссылается на неизвестный скилл «{s}»",
            MechanicParamKind.DamageType when !IsDamageType(s) => $"«{s}» — неизвестный тип урона (physical, fire, cold, lightning или poison)",
            MechanicParamKind.Attribute when !Attributes.Contains(s) => $"«{s}» — неизвестный атрибут (str, int, will или dex)",
            MechanicParamKind.Condition when !ConditionCatalog.IsKnown(s) => $"«{s}» — неизвестное условие",
            _ => null,
        };
    }

    private static bool IsDamageType(string s) =>
        s == s.ToLowerInvariant() && Enum.TryParse<DamageType>(s, ignoreCase: true, out var t) && Enum.IsDefined(t) && !int.TryParse(s, out _);
}
