namespace Dialoid.Core.Effects;

public sealed class MechanicRegistry
{
    private static readonly Lazy<MechanicRegistry> DefaultInstance = new(() => new MechanicRegistry());
    private readonly Dictionary<string, IMechanic> _items = new();

    public static MechanicRegistry Default => DefaultInstance.Value;

    public void Register(IMechanic m)
    {
        if (!_items.TryAdd(m.Id, m))
            throw new ArgumentException($"Mechanic '{m.Id}' is already registered", nameof(m));
    }

    public bool Contains(string id) => _items.ContainsKey(id);

    public IMechanic Get(string id) =>
        _items.TryGetValue(id, out var m) ? m : throw new KeyNotFoundException($"Unknown mechanic '{id}'");
}
