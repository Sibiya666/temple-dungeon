using Dialoid.Core.Combat;
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

    internal GameData(Builder b)
    {
        Stats = new Dictionary<string, StatDefinition>(b.Stats);
        Tags = new HashSet<string>(b.Tags);
        Ailments = new Dictionary<string, AilmentDefinition>(b.Ailments);
    }

    internal sealed class Builder
    {
        public Dictionary<string, StatDefinition> Stats { get; } = new();
        public HashSet<string> Tags { get; } = new();
        public Dictionary<string, AilmentDefinition> Ailments { get; } = new();
        public GameData Build() => new(this);
    }
}
