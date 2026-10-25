using Dialoid.Core.Character;
using Dialoid.Core.Combat;
using Dialoid.Core.Passives;
using Dialoid.Core.Skills;
using Dialoid.Core.Stats;

namespace Dialoid.Core.Data;

public enum StackRule { ReplaceWeakest, StrongestOnly, Unlimited }

public sealed record AilmentDefinition(string Id, DamageType SourceType, string ChanceStat, double DamageFraction,
    double DurationSec, string MaxStacksStat, StackRule StackRule, string Tag);

/// <summary>Immutable, validated game content. Later tasks add more catalogs.</summary>
public sealed class GameData
{
    public IReadOnlyDictionary<string, StatDefinition> Stats { get; }
    public IReadOnlySet<string> Tags { get; }
    public IReadOnlyDictionary<string, AilmentDefinition> Ailments { get; }
    public IReadOnlyDictionary<string, ClassDefinition> Classes { get; }
    public AttributeRules AttributeRules { get; }
    public PassiveTree Passives { get; }
    public IReadOnlyDictionary<string, Ascendancy> Ascendancies { get; }
    public IReadOnlyDictionary<string, SkillDefinition> Skills { get; }
    public IReadOnlyDictionary<string, WeaponStub> Weapons { get; }
    public IReadOnlyDictionary<string, ItemStub> Items { get; }
    public ProgressionTable Progression { get; }

    internal GameData(Builder b)
    {
        Stats = new Dictionary<string, StatDefinition>(b.Stats);
        Tags = new HashSet<string>(b.Tags);
        Ailments = new Dictionary<string, AilmentDefinition>(b.Ailments);
        Classes = new Dictionary<string, ClassDefinition>(b.Classes);
        AttributeRules = b.AttributeRules;
        Passives = new PassiveTree(b.PassiveNodes.Values, b.PassiveLinks);
        Ascendancies = new Dictionary<string, Ascendancy>(b.Ascendancies);
        Skills = new Dictionary<string, SkillDefinition>(b.Skills);
        Weapons = new Dictionary<string, WeaponStub>(b.Weapons);
        Items = new Dictionary<string, ItemStub>(b.Items);
        Progression = b.Progression;
    }

    internal sealed class Builder
    {
        public Dictionary<string, StatDefinition> Stats { get; } = new();
        public HashSet<string> Tags { get; } = new();
        public Dictionary<string, AilmentDefinition> Ailments { get; } = new();
        public Dictionary<string, ClassDefinition> Classes { get; } = new();
        public AttributeRules AttributeRules { get; set; } = AttributeRules.Empty;
        public Dictionary<string, PassiveNode> PassiveNodes { get; } = new();
        public Dictionary<string, IReadOnlyList<string>> PassiveLinks { get; } = new();
        public Dictionary<string, Ascendancy> Ascendancies { get; } = new();
        public Dictionary<string, SkillDefinition> Skills { get; } = new();
        public Dictionary<string, WeaponStub> Weapons { get; } = new();
        public Dictionary<string, ItemStub> Items { get; } = new();
        public ProgressionTable Progression { get; set; } = ProgressionTable.Empty;
        public GameData Build() => new(this);
    }
}
