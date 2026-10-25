using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dialoid.Core.Character;
using Dialoid.Core.Data;

namespace Dialoid.Core.Builds;

/// <summary>
/// Build sharing codes: <c>"DLD1:" + base64url(gzip(UTF-8 JSON of Build))</c>.
/// JSON uses public fields in snake_case (same convention as the data files), enums as snake_case strings.
/// Import checks only that ids exist; legality (point budgets, caps) is <see cref="BuildRules"/>' job.
/// </summary>
public static class BuildCodec
{
    public const string Prefix = "DLD1:";
    private const int MaxJsonBytes = 1_000_000;

    private static readonly JsonSerializerOptions Options = new()
    {
        IncludeFields = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = null,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false) },
    };

    public static string Export(Build build)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(build, Options);
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.SmallestSize, leaveOpen: true)) gz.Write(json);
        return Prefix + Convert.ToBase64String(ms.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static bool TryImport(string code, GameData data, out Build? build, out string? error)
    {
        build = null;
        try
        {
            error = Decode(code, out var parsed);
            if (error != null) return false;
            error = CheckIds(parsed!, data);
            if (error != null) return false;
            build = parsed;
            return true;
        }
        catch (Exception)
        {
            build = null;
            error = "Код билда повреждён: не удалось разобрать содержимое";
            return false;
        }
    }

    private static string? Decode(string? code, out Build? build)
    {
        build = null;
        code = code?.Trim();
        if (string.IsNullOrEmpty(code)) return "Код билда пуст";
        if (!code.StartsWith(Prefix, StringComparison.Ordinal))
        {
            if (code.Length > 3 && code.StartsWith("DLD", StringComparison.Ordinal) && char.IsDigit(code[3]) && code.Contains(':'))
                return $"Неподдерживаемая версия кода билда «{code[..code.IndexOf(':')]}», ожидается DLD1";
            return "Неверный префикс кода билда: ожидается «DLD1:»";
        }

        byte[] compressed;
        try
        {
            var b64 = code[Prefix.Length..].Replace('-', '+').Replace('_', '/');
            b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
            compressed = Convert.FromBase64String(b64);
        }
        catch (FormatException) { return "Код билда повреждён: некорректная кодировка base64"; }
        if (compressed.Length == 0) return "Код билда пуст после префикса";

        byte[] json;
        try
        {
            using var gz = new GZipStream(new MemoryStream(compressed), CompressionMode.Decompress);
            using var outMs = new MemoryStream();
            var buf = new byte[8192];
            int n;
            while ((n = gz.Read(buf, 0, buf.Length)) > 0)
            {
                outMs.Write(buf, 0, n);
                if (outMs.Length > MaxJsonBytes) return "Код билда повреждён: слишком большой объём данных";
            }
            json = outMs.ToArray();
        }
        catch (Exception e) when (e is InvalidDataException or EndOfStreamException or IOException)
        {
            return "Код билда повреждён или обрезан: ошибка распаковки gzip";
        }

        try
        {
            build = JsonSerializer.Deserialize<Build>(Encoding.UTF8.GetString(json), Options);
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException)
        {
            return "Код билда повреждён: некорректный JSON билда";
        }
        if (build == null) return "Код билда повреждён: пустой JSON билда";
        build.ClassId ??= "";
        build.AscendancyNodes ??= new();
        build.PassiveNodes ??= new();
        build.AttributeChoices ??= new();
        build.Slots ??= new();
        build.ItemIds ??= new();
        build.Rotation ??= new();
        if (build.Slots.Any(s => s == null)) { build = null; return "Код билда повреждён: пустой слот скилла"; }
        foreach (var s in build.Slots) { s.SkillId ??= ""; s.NodePoints ??= new(); }
        var hasNull = build.PassiveNodes.Any(x => x == null) || build.AscendancyNodes.Any(x => x == null)
            || build.ItemIds.Any(x => x == null) || build.Rotation.Any(x => x == null)
            || build.Slots.Any(sl => sl.NodePoints.Keys.Any(k => k == null));
        if (hasNull) { build = null; return "Код билда повреждён: пустое значение в списке id"; }
        return null;
    }

    private static string? CheckIds(Build b, GameData data)
    {
        var unknown = new List<string>();
        if (!data.Classes.ContainsKey(b.ClassId)) unknown.Add($"класс «{b.ClassId}»");
        foreach (var id in b.PassiveNodes.Distinct().Where(id => !data.Passives.Nodes.ContainsKey(id)))
            unknown.Add($"пассивный узел «{id}»");
        if (b.AscendancyId != null && !data.Ascendancies.TryGetValue(b.AscendancyId, out _))
            unknown.Add($"асцендант «{b.AscendancyId}»");
        else if (b.AscendancyNodes.Count > 0)
        {
            var known = b.AscendancyId != null ? data.Ascendancies[b.AscendancyId].Notables.Select(n => n.Id).ToHashSet() : new HashSet<string>();
            foreach (var id in b.AscendancyNodes.Distinct().Where(id => !known.Contains(id)))
                unknown.Add($"узел асцендента «{id}»");
        }
        if (b.WeaponId != null && !data.Weapons.ContainsKey(b.WeaponId)) unknown.Add($"оружие «{b.WeaponId}»");
        foreach (var id in b.ItemIds.Distinct().Where(id => !data.Items.ContainsKey(id))) unknown.Add($"предмет «{id}»");
        foreach (var slot in b.Slots)
        {
            if (!data.Skills.TryGetValue(slot.SkillId, out var skill)) { unknown.Add($"скилл «{slot.SkillId}»"); continue; }
            foreach (var id in slot.NodePoints.Keys.Where(id => skill.Tree.All(n => n.Id != id)))
                unknown.Add($"узел «{id}» в дереве скилла «{skill.Id}»");
        }
        return unknown.Count == 0 ? null : "В коде билда неизвестные id: " + string.Join(", ", unknown);
    }
}
