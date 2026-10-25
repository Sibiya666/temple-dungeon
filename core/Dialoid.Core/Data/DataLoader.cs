using System.Text.Json;
using System.Text.Json.Serialization;
using Dialoid.Core.Character;
using Dialoid.Core.Data.Json;
using Dialoid.Core.Stats;

namespace Dialoid.Core.Data;

public static class DataLoader
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false) },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static LoadResult Load(string dataDirectory)
    {
        var errors = new List<DataError>();
        var b = new GameData.Builder();

        int errorsBeforeStats = errors.Count;
        foreach (var (id, s) in ReadCatalog<StatJson>(dataDirectory, "stats.json", x => x.Id, errors))
            b.Stats[id] = s.ToDefinition();
        bool statsOk = errors.Count == errorsBeforeStats;
        int errorsBeforeTags = errors.Count;
        foreach (var (id, _) in ReadCatalog<TagJson>(dataDirectory, "tags.json", x => x.Id, errors))
            b.Tags.Add(id);

        bool tagsOk = errors.Count == errorsBeforeTags;
        // Cross-references are only checked against catalogs that loaded cleanly, to avoid cascading errors.
        foreach (var (id, a) in ReadCatalog<AilmentJson>(dataDirectory, "ailments.json", x => x.Id, errors))
        {
            const string F = "ailments.json";
            bool ok = true;
            void Err(string m) { errors.Add(new DataError(F, id, m)); ok = false; }
            if (a.SourceType is null) Err("Не задан «source_type»");
            if (a.DurationSec is not > 0) Err("«duration_sec» должен быть положительным");
            if (a.StackRule is null) Err("Не задан «stack_rule»");
            if (a.DamageFraction < 0) Err("«damage_fraction» не может быть отрицательным");
            if (string.IsNullOrEmpty(a.ChanceStat) || (statsOk && !b.Stats.ContainsKey(a.ChanceStat))) Err($"Неизвестный стат шанса «{a.ChanceStat}»");
            if (string.IsNullOrEmpty(a.MaxStacksStat) || (statsOk && !b.Stats.ContainsKey(a.MaxStacksStat))) Err($"Неизвестный стат максимума стаков «{a.MaxStacksStat}»");
            if (string.IsNullOrEmpty(a.Tag) || (tagsOk && !b.Tags.Contains(a.Tag))) Err($"Неизвестный тег «{a.Tag}»");
            if (ok)
                b.Ailments[id] = new AilmentDefinition(id, a.SourceType!.Value, a.ChanceStat!, a.DamageFraction,
                    a.DurationSec!.Value, a.MaxStacksStat!, a.StackRule!.Value, a.Tag!);
        }

        LoadAttributes(dataDirectory, b, errors, statsOk);
        LoadClasses(dataDirectory, b, errors, statsOk, tagsOk);

        return new LoadResult(errors.Count == 0 ? b.Build() : null, errors);
    }

    private static void LoadAttributes(string dir, GameData.Builder b, List<DataError> errors, bool statsOk)
    {
        const string F = "attributes.json";
        var path = Path.Combine(dir, F);
        if (!File.Exists(path)) return; // optional catalog
        AttributesFileJson? file;
        try { file = JsonSerializer.Deserialize<AttributesFileJson>(File.ReadAllText(path), Options); }
        catch (JsonException ex) { errors.Add(new DataError(F, null, $"Некорректный JSON: {ex.Message}")); return; }
        catch (IOException ex) { errors.Add(new DataError(F, null, $"Не удалось прочитать файл: {ex.Message}")); return; }
        if (file == null) { errors.Add(new DataError(F, null, "Файл пуст")); return; }

        int before = errors.Count;
        if (file.PrimaryDamageMorePerPoint < 0) errors.Add(new DataError(F, null, "«primary_damage_more_per_point» не может быть отрицательным"));
        var bonuses = new List<AttributeBonus>();
        int index = 0;
        foreach (var x in file.Bonuses ?? new())
        {
            index++;
            if (x.Attribute is null) { errors.Add(new DataError(F, null, $"Бонус №{index}: не задан «attribute» (str, int, will или dex)")); continue; }
            if (string.IsNullOrEmpty(x.Stat) || (statsOk && !b.Stats.ContainsKey(x.Stat))) { errors.Add(new DataError(F, null, $"Бонус №{index}: неизвестный стат «{x.Stat}»")); continue; }
            if (x.Kind is null or ModKind.Override) { errors.Add(new DataError(F, null, $"Бонус №{index}: «kind» должен быть flat, increased или more")); continue; }
            if (x.PerPoint is null) { errors.Add(new DataError(F, null, $"Бонус №{index}: не задано «per_point»")); continue; }
            bonuses.Add(new AttributeBonus(x.Attribute.Value, x.Stat, x.Kind.Value, x.PerPoint.Value));
        }
        if (errors.Count == before)
            b.AttributeRules = new AttributeRules(bonuses, file.PrimaryDamageMorePerPoint);
    }

    private static void LoadClasses(string dir, GameData.Builder b, List<DataError> errors, bool statsOk, bool tagsOk)
    {
        const string F = "classes.json";
        if (!File.Exists(Path.Combine(dir, F))) return; // optional catalog
        foreach (var (id, c) in ReadCatalog<ClassJson>(dir, F, x => x.Id, errors))
        {
            bool ok = true;
            void Err(string m) { errors.Add(new DataError(F, id, m)); ok = false; }
            if (string.IsNullOrWhiteSpace(c.Name)) Err("Не задано «name»");
            if (c.Primary is null) Err("Не задан «primary» (str, int, will или dex)");
            var baseAttrs = new Dictionary<Character.Attribute, int>();
            foreach (var a in new[] { Character.Attribute.Str, Character.Attribute.Int, Character.Attribute.Will, Character.Attribute.Dex })
            {
                if (c.BaseAttributes != null && c.BaseAttributes.TryGetValue(a.StatId(), out var v))
                {
                    if (v < 0) Err($"База атрибута «{a.StatId()}» не может быть отрицательной");
                    baseAttrs[a] = v;
                }
                else Err($"В «base_attributes» не задан атрибут «{a.StatId()}»");
            }
            if (c.BaseAttributes != null)
                foreach (var key in c.BaseAttributes.Keys)
                    if (key is not ("str" or "int" or "will" or "dex")) Err($"Неизвестный атрибут «{key}» в «base_attributes»");
            if (c.LifeMultiplier <= 0) Err("«life_multiplier» должен быть положительным");

            ResourceDefinition? resource = null;
            var r = c.Resource;
            if (r == null) Err("Не задан «resource»");
            else
            {
                bool rok = true;
                if (string.IsNullOrWhiteSpace(r.Id)) { Err("У ресурса не задан «id»"); rok = false; }
                if (string.IsNullOrWhiteSpace(r.Name)) { Err("У ресурса не задано «name»"); rok = false; }
                if (r.BaseMax is not > 0) { Err("«base_max» ресурса должен быть положительным"); rok = false; }
                if (r.RegenPerSec < 0 || r.DecayPerSec < 0 || r.DecayDelaySec < 0) { Err("Регенерация, затухание и задержка ресурса не могут быть отрицательными"); rok = false; }
                PerStat? maxPer = null;
                if (r.MaxPer != null)
                {
                    if (string.IsNullOrEmpty(r.MaxPer.Stat) || (statsOk && !b.Stats.ContainsKey(r.MaxPer.Stat))) { Err($"Неизвестный стат в «max_per»: «{r.MaxPer.Stat}»"); rok = false; }
                    else if (r.MaxPer.Every <= 0) { Err("«max_per.every» должен быть положительным"); rok = false; }
                    else maxPer = new PerStat(r.MaxPer.Stat, r.MaxPer.Every, r.MaxPer.Cap);
                }
                if (rok) resource = new ResourceDefinition(r.Id!, r.Name!, r.BaseMax!.Value, r.RegenPerSec, r.DecayPerSec, r.DecayDelaySec, maxPer);
            }

            var bonuses = new List<Modifier>();
            var src = new SourceRef("class", id, c.Name ?? id);
            int bi = 0;
            foreach (var el in c.ClassBonuses ?? new())
            {
                bi++;
                var errs = new List<string>();
                var m = ModifierJson.Read(el, src, errs);
                foreach (var e in errs) Err($"Классовый бонус №{bi}: {e}");
                if (m == null) continue;
                if (statsOk && !b.Stats.ContainsKey(m.Stat)) Err($"Классовый бонус №{bi}: неизвестный стат «{m.Stat}»");
                if (m.Per != null && statsOk && !b.Stats.ContainsKey(m.Per.Stat)) Err($"Классовый бонус №{bi}: неизвестный стат «{m.Per.Stat}» в «per»");
                if (tagsOk) foreach (var t in m.RequiredTags.Items)
                    if (!b.Tags.Contains(t)) Err($"Классовый бонус №{bi}: неизвестный тег «{t}»");
                bonuses.Add(m);
            }

            if (ok)
                b.Classes[id] = new ClassDefinition(id, c.Name!, c.Primary!.Value, baseAttrs, c.LifeMultiplier, resource!, bonuses,
                    c.PassiveStartNode ?? "", c.Skills ?? new(), c.Ascendancies ?? new());
        }
    }

    /// <summary>Reads a JSON array file; each element is deserialized independently so one bad entry does not hide the others.</summary>
    internal static List<(string Id, T Item)> ReadCatalog<T>(string dir, string file, Func<T, string?> idOf, List<DataError> errors)
        where T : class
    {
        var result = new List<(string, T)>();
        var path = Path.Combine(dir, file);
        if (!File.Exists(path)) { errors.Add(new DataError(file, null, "Файл не найден")); return result; }

        JsonDocument doc;
        try { doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }); }
        catch (JsonException ex) { errors.Add(new DataError(file, null, $"Некорректный JSON: {ex.Message}")); return result; }
        catch (IOException ex) { errors.Add(new DataError(file, null, $"Не удалось прочитать файл: {ex.Message}")); return result; }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                errors.Add(new DataError(file, null, "Корень файла должен быть массивом"));
                return result;
            }
            var seen = new HashSet<string>();
            int index = 0;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                index++;
                T? item;
                try { item = el.Deserialize<T>(Options); }
                catch (JsonException ex) { errors.Add(new DataError(file, null, $"Запись №{index}: некорректный JSON: {ex.Message}")); continue; }
                var id = item == null ? null : idOf(item);
                if (string.IsNullOrWhiteSpace(id)) { errors.Add(new DataError(file, null, $"Запись №{index}: не задан «id»")); continue; }
                if (!seen.Add(id)) { errors.Add(new DataError(file, id, $"Повторяющийся id «{id}»")); continue; }
                result.Add((id, item!));
            }
        }
        return result;
    }
}
