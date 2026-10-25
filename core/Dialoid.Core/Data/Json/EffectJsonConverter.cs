using System.Text.Json;
using Dialoid.Core.Combat;
using Dialoid.Core.Effects;
using Dialoid.Core.Stats;

namespace Dialoid.Core.Data.Json;

/// <summary>Parses effects from JSON. Problems become DataError entries (Russian), never exceptions.</summary>
public static class EffectJson
{
    public static IReadOnlyList<Effect> ParseList(JsonElement el, Scope defaultScope, MechanicRegistry mechanics,
        string file, string? ownerId, List<DataError> errors)
    {
        var result = new List<Effect>();
        if (el.ValueKind != JsonValueKind.Array)
        {
            errors.Add(new DataError(file, ownerId, "Список эффектов должен быть массивом"));
            return result;
        }
        foreach (var item in el.EnumerateArray())
            if (Parse(item, defaultScope, mechanics, file, ownerId, errors) is { } e) result.Add(e);
        return result;
    }

    public static Effect? Parse(JsonElement el, Scope defaultScope, MechanicRegistry mechanics,
        string file, string? ownerId, List<DataError> errors)
    {
        int before = errors.Count;
        void Err(string msg) => errors.Add(new DataError(file, ownerId, msg));

        if (el.ValueKind != JsonValueKind.Object) { Err("Эффект должен быть объектом"); return null; }
        var type = Str(el, "type");
        if (string.IsNullOrEmpty(type)) { Err("У эффекта не задан «type»"); return null; }

        var scope = defaultScope;
        if (el.TryGetProperty("scope", out var sc))
        {
            var s = ParseScope(sc, Err);
            if (s == null) return null;
            scope = s;
        }

        bool? perPoint = null;
        if (el.TryGetProperty("per_point", out var pp))
        {
            if (pp.ValueKind is JsonValueKind.True or JsonValueKind.False) perPoint = pp.GetBoolean();
            else { Err("«per_point» должен быть true или false"); return null; }
        }

        string? Req(string name)
        {
            var v = Str(el, name);
            if (string.IsNullOrEmpty(v)) Err($"У эффекта «{type}» не задано поле «{name}»");
            return v;
        }
        double Num(string name)
        {
            if (el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number) return v.GetDouble();
            Err($"У эффекта «{type}» не задано числовое поле «{name}»");
            return 0;
        }
        DamageType? Dmg(string name)
        {
            var v = Req(name);
            if (v == null) return null;
            if (!TryDamageType(v, out var d)) { Err($"Неизвестный тип урона «{v}» (ожидается physical, lightning, cold, fire или poison)"); return null; }
            return d;
        }

        Effect? result = null;
        switch (type)
        {
            case "modifier":
            {
                var src = new SourceRef("effect", ownerId ?? "", ownerId ?? "");
                var mod = el.TryGetProperty("modifier", out var inner) ? inner : el;
                var errs = new List<string>();
                var m = ModifierJson.Read(mod, src, errs);
                foreach (var e in errs) Err(e);
                if (m != null) result = new ModifierEffect(m, scope, perPoint ?? true);
                break;
            }
            case "add_tag": { var t = Req("tag"); if (t != null) result = new AddTagEffect(t, scope); break; }
            case "remove_tag": { var t = Req("tag"); if (t != null) result = new RemoveTagEffect(t, scope); break; }
            case "set_behavior":
            {
                var p = Req("param"); double v = Num("value");
                if (p != null && errors.Count == before) result = new SetBehaviorEffect(p, v, scope);
                break;
            }
            case "add_behavior":
            {
                var p = Req("param"); double d = Num("delta");
                if (p != null && errors.Count == before) result = new AddBehaviorEffect(p, d, scope, perPoint ?? true);
                break;
            }
            case "add_conversion":
            {
                var from = Dmg("from"); var to = Dmg("to"); double pct = Num("percent");
                if (from != null && to != null && errors.Count == before)
                {
                    if (to.Value <= from.Value) Err("Конверсия возможна только вправо: physical → lightning → cold → fire → poison");
                    else result = new AddConversionEffect(from.Value, to.Value, pct, scope, perPoint ?? true);
                }
                break;
            }
            case "set_damage_type":
            {
                var to = Dmg("to");
                if (to != null) result = new SetDamageTypeEffect(to.Value, scope);
                break;
            }
            case "set_resource":
            {
                var f = Req("field"); double v = Num("value");
                if (f == null) break;
                if (f == "cost") { if (errors.Count == before) result = new SetResourceEffect(ResourceField.Cost, v, scope); }
                else if (f == "gain") { if (errors.Count == before) result = new SetResourceEffect(ResourceField.Gain, v, scope); }
                else Err($"Неизвестное поле ресурса «{f}» (ожидается cost или gain)");
                break;
            }
            case "grant_proc":
            {
                Trigger? trig = el.TryGetProperty("trigger", out var tj) ? ParseTrigger(tj, Err) : MissingField<Trigger>("trigger");
                ProcAction? act = el.TryGetProperty("action", out var aj) ? ParseAction(aj, Err, file, ownerId, errors) : MissingField<ProcAction>("action");
                if (trig != null && act != null && errors.Count == before) result = new GrantProcEffect(trig, act, scope);
                break;
            }
            case "mechanic":
            {
                var id = Req("id");
                if (id == null) break;
                if (!mechanics.Contains(id)) { Err($"Неизвестная механика «{id}»"); break; }
                var prms = new Dictionary<string, JsonElement>();
                if (el.TryGetProperty("params", out var pj))
                {
                    if (pj.ValueKind != JsonValueKind.Object) { Err("«params» механики должен быть объектом"); break; }
                    foreach (var prop in pj.EnumerateObject()) prms[prop.Name] = prop.Value.Clone();
                }
                result = new MechanicEffect(id, prms, scope);
                break;
            }
            default:
                Err($"Неизвестный тип эффекта «{type}»");
                break;
        }
        return errors.Count > before ? null : result;

        T? MissingField<T>(string name) where T : class { Err($"У эффекта «{type}» не задано поле «{name}»"); return null; }
    }

    private static Scope? ParseScope(JsonElement sc, Action<string> err)
    {
        if (sc.ValueKind == JsonValueKind.String)
        {
            var s = sc.GetString()!;
            if (s == "global") return new Scope.Global();
            if (s.StartsWith("skill:", StringComparison.Ordinal) && s.Length > 6) return new Scope.Skill(s[6..]);
            err($"Неизвестная область действия «{s}» (ожидается global, skill:<id> или {{\"tags\":[...]}})");
            return null;
        }
        if (sc.ValueKind == JsonValueKind.Object && sc.TryGetProperty("tags", out var t) && t.ValueKind == JsonValueKind.Array
            && t.EnumerateArray().All(x => x.ValueKind == JsonValueKind.String))
            return new Scope.Tags(TagSet.Of(t.EnumerateArray().Select(x => x.GetString()!).ToArray()));
        err("Неверная область действия: ожидается строка global / skill:<id> или объект с массивом «tags»");
        return null;
    }

    private static Trigger? ParseTrigger(JsonElement t, Action<string> err)
    {
        if (t.ValueKind == JsonValueKind.String)
        {
            switch (t.GetString())
            {
                case "on_use": return new Trigger.OnUse();
                case "on_hit": return new Trigger.OnHit();
                case "on_crit": return new Trigger.OnCrit();
                case "on_kill": return new Trigger.OnKill();
            }
        }
        else if (t.ValueKind == JsonValueKind.Object)
        {
            foreach (var (name, make) in new (string, Func<int, Trigger>)[] { ("every_n_uses", n => new Trigger.EveryNUses(n)), ("every_n_hits", n => new Trigger.EveryNHits(n)) })
            {
                if (!t.TryGetProperty(name, out var n)) continue;
                if (n.ValueKind == JsonValueKind.Number && n.TryGetInt32(out var i) && i > 0) return make(i);
                err($"«{name}» должен быть положительным целым числом");
                return null;
            }
        }
        err("Неизвестный триггер (ожидается on_use, on_hit, on_crit, on_kill, {\"every_n_uses\":N} или {\"every_n_hits\":N})");
        return null;
    }

    private static ProcAction? ParseAction(JsonElement a, Action<string> err, string file, string? ownerId, List<DataError> errors)
    {
        if (a.ValueKind != JsonValueKind.Object) { err("Действие должно быть объектом"); return null; }
        if (a.TryGetProperty("apply_ailment", out var ail))
        {
            if (ail.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(ail.GetString())) return new ProcAction.ApplyAilment(ail.GetString()!);
            err("«apply_ailment» должен быть строкой с id недуга");
            return null;
        }
        if (a.TryGetProperty("cast_skill", out var cs))
        {
            var id = Str(cs, "id");
            if (string.IsNullOrEmpty(id) || !cs.TryGetProperty("damage_percent", out var dp) || dp.ValueKind != JsonValueKind.Number)
            { err("«cast_skill» требует «id» и числовое «damage_percent»"); return null; }
            return new ProcAction.CastSkill(id, dp.GetDouble());
        }
        if (a.TryGetProperty("apply_buff", out var ab))
        {
            var id = Str(ab, "id");
            if (string.IsNullOrEmpty(id) || !ab.TryGetProperty("duration", out var d) || d.ValueKind != JsonValueKind.Number)
            { err("«apply_buff» требует «id» и числовое «duration»"); return null; }
            var mods = new List<Modifier>();
            if (ab.TryGetProperty("modifiers", out var ms))
            {
                if (ms.ValueKind != JsonValueKind.Array) { err("«modifiers» баффа должен быть массивом"); return null; }
                var src = new SourceRef("buff", id, id);
                foreach (var m in ms.EnumerateArray())
                {
                    var errs = new List<string>();
                    var mod = ModifierJson.Read(m, src, errs);
                    foreach (var e in errs) err(e);
                    if (mod != null) mods.Add(mod);
                }
            }
            return new ProcAction.ApplyBuff(id, d.GetDouble(), mods);
        }
        err("Неизвестное действие (ожидается apply_buff, apply_ailment или cast_skill)");
        return null;
    }

    private static bool TryDamageType(string s, out DamageType d)
    {
        switch (s)
        {
            case "physical": d = DamageType.Physical; return true;
            case "lightning": d = DamageType.Lightning; return true;
            case "cold": d = DamageType.Cold; return true;
            case "fire": d = DamageType.Fire; return true;
            case "poison": d = DamageType.Poison; return true;
        }
        d = default; return false;
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
