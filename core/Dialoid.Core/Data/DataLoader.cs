using System.Text.Json;
using System.Text.Json.Serialization;
using Dialoid.Core.Data.Json;

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

        return new LoadResult(errors.Count == 0 ? b.Build() : null, errors);
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
