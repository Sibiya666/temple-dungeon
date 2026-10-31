using System.Text.Json;
using Dialoid.Core.Stats;
using Godot;

namespace Dialoid.Game.Room;

/// <summary>The hero clip of a skill and the fraction of the use where the blow lands (<see cref="Contact"/>, 0–1).</summary>
public sealed record SkillAnimation(string ClipId, double Contact)
{
    /// <summary>Seconds from the start of a use stretched to <paramref name="useSec"/> to the contact frame.</summary>
    public double ContactSec(double useSec) => Contact * useSec;
}

/// <summary>
/// The table «skill → hero clip» of game/data/skill_animations.json (spec §5: game layer data, not core): cleave and whirlwind
/// slash, crush and charge strike from above, everything else casts. A skill not in the table goes by its compiled tags
/// (<c>melee</c> → the melee entry, else the default one). A missing or broken file gives the same built-in defaults.
/// </summary>
public sealed class SkillAnimations
{
    public const string TablePath = "res://data/skill_animations.json";

    public static readonly SkillAnimation DefaultMelee = new("hero_slash", 0.45);
    public static readonly SkillAnimation DefaultCast = new("hero_cast", 0.55);

    private readonly Dictionary<string, SkillAnimation> _skills;

    public SkillAnimations(IReadOnlyDictionary<string, SkillAnimation> skills, SkillAnimation melee, SkillAnimation fallback)
    {
        _skills = new Dictionary<string, SkillAnimation>(skills, StringComparer.Ordinal);
        Melee = melee;
        Default = fallback;
    }

    public static SkillAnimations Defaults { get; } = new(new Dictionary<string, SkillAnimation>(), DefaultMelee, DefaultCast);

    /// <summary>The entry for a melee skill missing from the table.</summary>
    public SkillAnimation Melee { get; }

    /// <summary>The entry for any other skill missing from the table.</summary>
    public SkillAnimation Default { get; }

    public IReadOnlyDictionary<string, SkillAnimation> Skills => _skills;

    /// <summary>The skill's entry, else by tags (<c>melee</c> → <see cref="Melee"/>, else <see cref="Default"/>).</summary>
    public SkillAnimation For(string skillId, TagSet tags) =>
        _skills.TryGetValue(skillId, out var a) ? a : tags.Contains("melee") ? Melee : Default;

    /// <summary>Reads <see cref="TablePath"/>; a missing or broken file prints one line and gives <see cref="Defaults"/>.</summary>
    public static SkillAnimations Load()
    {
        if (!Godot.FileAccess.FileExists(TablePath))
        {
            GD.Print($"{AssetLibrary.MissingMarker} таблица {TablePath}, анимации скиллов по тегам");
            return Defaults;
        }
        try
        {
            return Parse(Godot.FileAccess.GetFileAsString(TablePath));
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException)
        {
            GD.Print($"{AssetLibrary.MissingMarker} таблица {TablePath} не читается ({e.Message}), анимации скиллов по тегам");
            return Defaults;
        }
    }

    /// <summary>Parses the JSON text of the table; throws on a broken one (unknown short clip name, contact outside 0–1).</summary>
    public static SkillAnimations Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var clips = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in root.GetProperty("clips").EnumerateObject()) clips[p.Name] = p.Value.GetString()!;

        SkillAnimation Entry(JsonElement e)
        {
            var name = e.GetProperty("clip").GetString()!;
            if (!clips.TryGetValue(name, out var clipId)) throw new ArgumentException($"неизвестный клип «{name}»");
            var contact = e.GetProperty("contact").GetDouble();
            if (contact is < 0 or > 1) throw new ArgumentException($"доля контакта {contact} вне 0–1");
            return new SkillAnimation(clipId, contact);
        }

        var skills = new Dictionary<string, SkillAnimation>(StringComparer.Ordinal);
        foreach (var p in root.GetProperty("skills").EnumerateObject()) skills[p.Name] = Entry(p.Value);
        var tags = root.GetProperty("tags");
        return new SkillAnimations(skills, Entry(tags.GetProperty("melee")), Entry(tags.GetProperty("default")));
    }
}
