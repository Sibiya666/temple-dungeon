namespace Dialoid.Sandbox;

/// <summary>A reference build file. Group: <c>main</c> for data/builds/*.json, otherwise the subdirectory name.</summary>
public sealed record ReferenceBuildInfo(string Path, string Title, string Group);

/// <summary>The reference builds under data/builds (README and other non-JSON files are skipped).</summary>
public static class ReferenceBuilds
{
    public const string MainGroup = "main";
    private static readonly string[] KnownGroups = { MainGroup, "l20", "parity", "keystones", "attributes" };

    /// <summary>All reference builds: main first, then the known groups, then any other subdirectory; files by name.</summary>
    public static IReadOnlyList<ReferenceBuildInfo> List(string dataDirectory)
    {
        var root = System.IO.Path.Combine(dataDirectory, "builds");
        if (!Directory.Exists(root)) return Array.Empty<ReferenceBuildInfo>();

        var result = new List<ReferenceBuildInfo>();
        void AddFiles(string dir, string group) =>
            result.AddRange(Directory.GetFiles(dir, "*.json").OrderBy(f => f, StringComparer.Ordinal)
                .Select(f => new ReferenceBuildInfo(f, Title(f), group)));

        AddFiles(root, MainGroup);
        var subdirs = Directory.GetDirectories(root).Select(d => System.IO.Path.GetFileName(d))
            .OrderBy(g => Array.IndexOf(KnownGroups, g) is var i and >= 0 ? i : KnownGroups.Length)
            .ThenBy(g => g, StringComparer.Ordinal);
        foreach (var group in subdirs) AddFiles(System.IO.Path.Combine(root, group), group);
        return result;
    }

    /// <summary>Loads the file into the session (same rules as <see cref="SandboxSession.Import"/>).</summary>
    public static CommandResult Load(SandboxSession session, ReferenceBuildInfo info)
    {
        string text;
        try { text = File.ReadAllText(info.Path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return CommandResult.Fail(Texts.ReferenceUnreadable(info.Title, e is FileNotFoundException or DirectoryNotFoundException
                ? "файл не найден" : "ошибка чтения файла"));
        }
        var r = session.Import(text);
        return r.Ok ? r : CommandResult.Fail(Texts.ReferenceRefused(info.Title, r.Error ?? ""));
    }

    /// <summary>
    /// Russian titles of the known reference files by file name (the build JSON stays unchanged; the group is shown by the
    /// tree, so main and l20 versions share a title).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Titles = new Dictionary<string, string>
    {
        ["warrior_berserker_whirlwind"] = "Воитель, Берсерк — Вихрь",
        ["warrior_thunderer_crush"] = "Воитель, Громовержец — Сокрушение",
        ["warrior_fire_cleave_crit"] = "Воитель, Громовержец — огненный Рассекающий удар с критом",
        ["sorcerer_pyromancer_fireball"] = "Чародей, Пироман — Огненный шар",
        ["sorcerer_cryomancer_ice_lance"] = "Чародей, Криомант — Ледяное копье",
        ["sorcerer_spark_hybrid"] = "Чародей, Криомант — Искра (гибрид)",
        ["warrior_l1"] = "Воитель, 1 уровень",
        ["warrior_l20"] = "Воитель, 20 уровень",
        ["warrior_l60"] = "Воитель, 60 уровень",
        ["sorcerer_l1"] = "Чародей, 1 уровень",
        ["sorcerer_l20"] = "Чародей, 20 уровень",
        ["sorcerer_l60"] = "Чародей, 60 уровень",
        ["avatar_of_fire_fit"] = "Аватар пламени — подходящий билд",
        ["avatar_of_fire_unfit"] = "Аватар пламени — неподходящий билд",
        ["blade_dance_fit"] = "Танец клинков — подходящий билд",
        ["blade_dance_unfit"] = "Танец клинков — неподходящий билд",
        ["blood_pact_fit"] = "Кровавый договор — подходящий билд",
        ["blood_pact_unfit"] = "Кровавый договор — неподходящий билд",
        ["inevitability_fit"] = "Неотвратимость — подходящий билд",
        ["inevitability_unfit"] = "Неотвратимость — неподходящий билд",
        ["stone_skin_fit"] = "Каменная кожа — подходящий билд",
        ["stone_skin_unfit"] = "Каменная кожа — неподходящий билд",
        ["titan_will_fit"] = "Воля титана — подходящий билд",
        ["titan_will_unfit"] = "Воля титана — неподходящий билд",
        ["warrior_no_attribute_nodes"] = "Воитель без атрибутных узлов",
        ["sorcerer_no_attribute_nodes"] = "Чародей без атрибутных узлов",
    };

    /// <summary>The Russian title of a known file; an unknown file falls back to its name ("my_build.json" → "My build").</summary>
    private static string Title(string path)
    {
        var file = System.IO.Path.GetFileNameWithoutExtension(path);
        if (Titles.TryGetValue(file, out var title)) return title;
        var name = file.Replace('_', ' ').Trim();
        return name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];
    }
}
