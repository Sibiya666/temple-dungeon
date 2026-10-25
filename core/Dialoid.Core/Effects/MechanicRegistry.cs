using System.Diagnostics.CodeAnalysis;
using Dialoid.Core.Mechanics;

namespace Dialoid.Core.Effects;

public sealed class MechanicRegistry
{
    private static readonly Lazy<MechanicRegistry> DefaultInstance = new(() =>
    {
        var r = new MechanicRegistry();
        MechanicCatalog.RegisterAll(r);
        return r;
    });

    private readonly Dictionary<string, IMechanic> _items = new();

    /// <summary>Every mechanic implemented in code (<see cref="MechanicCatalog"/>).</summary>
    public static MechanicRegistry Default => DefaultInstance.Value;

    public void Register(IMechanic m)
    {
        if (!_items.TryAdd(m.Id, m))
            throw new ArgumentException($"Mechanic '{m.Id}' is already registered", nameof(m));
    }

    public bool Contains(string id) => _items.ContainsKey(id);

    public bool TryGet(string id, [MaybeNullWhen(false)] out IMechanic mechanic) => _items.TryGetValue(id, out mechanic);

    public IMechanic Get(string id) =>
        _items.TryGetValue(id, out var m) ? m : throw new KeyNotFoundException($"Unknown mechanic '{id}'");
}
