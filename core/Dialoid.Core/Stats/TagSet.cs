using System.Collections.Immutable;

namespace Dialoid.Core.Stats;

/// <summary>Immutable set of tag strings with value equality.</summary>
public readonly record struct TagSet
{
    private readonly ImmutableSortedSet<string>? _items;

    private TagSet(ImmutableSortedSet<string> items) => _items = items;

    public static TagSet Empty => default;

    public static TagSet Of(params string[] tags) =>
        tags.Length == 0 ? Empty : new TagSet(ImmutableSortedSet.CreateRange(StringComparer.Ordinal, tags));

    private ImmutableSortedSet<string> Set => _items ?? ImmutableSortedSet<string>.Empty.WithComparer(StringComparer.Ordinal);

    public IReadOnlyCollection<string> Items => Set;

    public bool Contains(string t) => Set.Contains(t);

    public bool ContainsAll(TagSet other)
    {
        foreach (var t in other.Set)
            if (!Set.Contains(t)) return false;
        return true;
    }

    public TagSet With(string t) => new(Set.Add(t));

    public TagSet Without(string t) => new(Set.Remove(t));

    public TagSet Union(TagSet o) => new(Set.Union(o.Set));

    public bool Equals(TagSet other) => Set.SetEquals(other.Set);

    public override int GetHashCode()
    {
        var h = new HashCode();
        foreach (var t in Set) h.Add(t, StringComparer.Ordinal);
        return h.ToHashCode();
    }

    public override string ToString() => "{" + string.Join(",", Set) + "}";
}
