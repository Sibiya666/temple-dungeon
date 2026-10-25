namespace Dialoid.Core.Stats;

public enum ModKind { Flat, Increased, More, Override }

/// <summary>Where a modifier came from. Kind: base|class|level|attribute|passive|ascendancy|keystone|skill_node|weapon|item|buff|mechanic.</summary>
public sealed record SourceRef(string Kind, string Id, string Name);

public sealed record Condition(string Id, double? Param = null, string? Ref = null);

/// <summary>Scales a modifier by (value of Stat / Every), at most Cap (applied to the resulting value).</summary>
public sealed record PerStat(string Stat, double Every, double? Cap);

public sealed record Modifier(string Stat, ModKind Kind, double Value, TagSet RequiredTags,
    IReadOnlyList<Condition> Conditions, PerStat? Per, SourceRef Source, int Priority = 0);
