using Dialoid.Core.Combat;
using Dialoid.Core.Simulation;

namespace Dialoid.Core.Data.Json;

internal sealed class DummyAttackJson
{
    public Dictionary<string, double>? DamagePerHit { get; set; }
    public double IntervalSec { get; set; }
}

internal sealed class DummyJson
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public int PackSize { get; set; } = 1;
    public double PackRadius { get; set; } = 4;
    /// <summary>null = immortal.</summary>
    public double? LifePerTarget { get; set; }
    public bool Respawn { get; set; }
    public double Armor { get; set; }
    public Dictionary<string, double>? Resists { get; set; }
    public double FreezeDurationMultiplier { get; set; } = 1;
    public DummyAttackJson? Attack { get; set; }

    /// <summary>Returns null and reports Russian messages when the entry is invalid.</summary>
    public DummyDefinition? ToDefinition(string id, Action<string> err)
    {
        bool ok = true;
        void Err(string m) { err(m); ok = false; }
        if (string.IsNullOrWhiteSpace(Name)) Err("Не задано «name»");
        if (PackSize is < 1 or > DummyDefinition.MaxPackSize) Err($"«pack_size» должен быть от 1 до {DummyDefinition.MaxPackSize}");
        if (PackRadius <= 0) Err("«pack_radius» должен быть положительным");
        if (LifePerTarget is { } life && !(life > 0)) Err("«life_per_target» должен быть положительным (null — бессмертный)");
        if (Armor < 0) Err("«armor» не может быть отрицательной");
        if (FreezeDurationMultiplier < 0) Err("«freeze_duration_multiplier» не может быть отрицательным");
        var resists = ParseTypes(Resists, "resists", Err);
        foreach (var (type, value) in resists.Where(r => r.Value is < -100 or > 100))
            Err($"Сопротивление «{type.ToString().ToLowerInvariant()}» должно быть от −100 до 100 (а задано {value}): при 100+ попадания лечили бы");

        DummyAttack? attack = null;
        if (Attack != null)
        {
            if (Attack.IntervalSec <= 0) Err("«attack.interval_sec» должен быть положительным");
            var dmg = ParseTypes(Attack.DamagePerHit, "attack.damage_per_hit", Err);
            if (dmg.Values.Any(v => v < 0)) Err("Урон атаки манекена не может быть отрицательным");
            attack = new DummyAttack(dmg, Attack.IntervalSec);
        }
        if (!ok) return null;
        return new DummyDefinition(id, Name!, PackSize, PackRadius, LifePerTarget ?? double.PositiveInfinity, Respawn, Armor,
            resists, FreezeDurationMultiplier, attack);
    }

    private static SortedDictionary<DamageType, double> ParseTypes(Dictionary<string, double>? src, string field, Action<string> err)
    {
        var result = new SortedDictionary<DamageType, double>();
        foreach (var (key, value) in src ?? new())
        {
            if (key == key.ToLowerInvariant() && Enum.TryParse<DamageType>(key, ignoreCase: true, out var t) && !int.TryParse(key, out _))
                result[t] = value;
            else err($"Неизвестный тип урона «{key}» в «{field}»");
        }
        return result;
    }
}
