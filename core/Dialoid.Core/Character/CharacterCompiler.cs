using Dialoid.Core.Data;
using Dialoid.Core.Effects;
using Dialoid.Core.Skills;
using Dialoid.Core.Stats;

namespace Dialoid.Core.Character;

public sealed class CharacterCompiler
{
    private static readonly Attribute[] AllAttributes = { Attribute.Str, Attribute.Int, Attribute.Will, Attribute.Dex };

    private readonly GameData _data;

    public CharacterCompiler(GameData data) => _data = data;

    public CompiledCharacter Compile(Build build)
    {
        if (!_data.Classes.TryGetValue(build.ClassId, out var cls))
            throw new ArgumentException($"Unknown class '{build.ClassId}'.", nameof(build));
        if (build.Level < 1)
            throw new ArgumentException($"Level must be at least 1, got {build.Level}.", nameof(build));

        var mods = new List<Modifier>();
        var classSrc = new SourceRef("class", cls.Id, cls.Name);
        var levelSrc = new SourceRef("level", "level", $"Уровень {build.Level}");
        int grow = build.Level - 1;

        // Attributes: class base, +1 all per level, +1 primary per level.
        foreach (var a in AllAttributes)
        {
            var stat = a.StatId();
            if (cls.BaseAttributes.TryGetValue(a, out var baseValue) && baseValue != 0)
                mods.Add(new Modifier(stat, ModKind.Flat, baseValue, TagSet.Empty, Array.Empty<Condition>(), null, classSrc));
            if (grow > 0)
                mods.Add(new Modifier(stat, ModKind.Flat, grow, TagSet.Empty, Array.Empty<Condition>(), null, levelSrc));
        }
        if (grow > 0)
            mods.Add(new Modifier(cls.Primary.StatId(), ModKind.Flat, grow, TagSet.Empty, Array.Empty<Condition>(), null, levelSrc));

        // General attribute bonuses and the primary attribute damage multiplier (data-driven).
        var rules = _data.AttributeRules;
        foreach (var b in rules.Bonuses)
        {
            var src = new SourceRef("attribute", b.Attribute.StatId(), $"Атрибут: {b.Attribute.StatId()}");
            mods.Add(new Modifier(b.Stat, b.Kind, b.ValuePerPoint, TagSet.Empty, Array.Empty<Condition>(),
                new PerStat(b.Attribute.StatId(), 1, null), src));
        }
        if (rules.PrimaryDamageMorePerPoint != 0)
        {
            var p = cls.Primary.StatId();
            mods.Add(new Modifier("damage", ModKind.More, rules.PrimaryDamageMorePerPoint, TagSet.Empty,
                Array.Empty<Condition>(), new PerStat(p, 1, null), new SourceRef("attribute", p, $"Основной атрибут: {p}")));
        }

        // Life: 100 + 11 per level, then the class multiplier as a More modifier.
        mods.Add(new Modifier("life", ModKind.Flat, 100 + 11 * build.Level, TagSet.Empty, Array.Empty<Condition>(), null, levelSrc));
        if (cls.LifeMultiplier != 1)
            mods.Add(new Modifier("life", ModKind.More, (cls.LifeMultiplier - 1) * 100, TagSet.Empty,
                Array.Empty<Condition>(), null, classSrc));

        // Resource.
        var res = cls.Resource;
        var resSrc = new SourceRef("class", cls.Id, $"{cls.Name}: {res.Name}");
        mods.Add(new Modifier("resource_max", ModKind.Flat, res.BaseMax, TagSet.Empty, Array.Empty<Condition>(), null, resSrc));
        if (res.MaxPer is { } maxPer)
            mods.Add(new Modifier("resource_max", ModKind.Flat, 1, TagSet.Empty, Array.Empty<Condition>(), maxPer, resSrc));
        if (res.RegenPerSec != 0)
            mods.Add(new Modifier("resource_regen", ModKind.Flat, res.RegenPerSec, TagSet.Empty, Array.Empty<Condition>(), null, resSrc));

        mods.AddRange(cls.ClassBonuses);

        return new CompiledCharacter(build, cls, new StatEvaluator(_data.Stats, mods), mods,
            Array.Empty<CompiledSkill>(), Array.Empty<MechanicEffect>());
    }
}
