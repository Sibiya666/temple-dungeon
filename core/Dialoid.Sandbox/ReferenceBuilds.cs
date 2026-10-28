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

    /// <summary>"warrior_berserker_whirlwind.json" → "Warrior berserker whirlwind".</summary>
    private static string Title(string path)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(path).Replace('_', ' ').Trim();
        return name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];
    }
}
