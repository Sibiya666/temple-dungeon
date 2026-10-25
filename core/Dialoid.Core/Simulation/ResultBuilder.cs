using Dialoid.Core.Character;

namespace Dialoid.Core.Simulation;

internal static class ResultBuilder
{
    /// <summary>DPS = damage / elapsed; DpsSingleTarget counts target 0 only, DpsPack every target.</summary>
    public static SimResult Build(DamageLedger ledger, SimLog log, double elapsed, double? timeToKill, double? timeToDeath,
        IReadOnlyDictionary<Combat.DamageType, double>? damageTaken = null)
    {
        double pack = 0;
        foreach (var d in ledger.ByTarget) pack += d;
        var single = ledger.ByTarget.Length > 0 ? ledger.ByTarget[0] : 0;
        return new SimResult(single / elapsed, pack / elapsed,
            new SortedDictionary<string, double>(ledger.BySkill, StringComparer.Ordinal),
            new SortedDictionary<Combat.DamageType, double>(ledger.ByType),
            ledger.Hit, ledger.Dot, timeToKill, timeToDeath, log.Events.ToList(), Array.Empty<RuleViolation>(),
            new SortedDictionary<Combat.DamageType, double>(
                (damageTaken ?? new Dictionary<Combat.DamageType, double>()).ToDictionary(kv => kv.Key, kv => kv.Value)));
    }
}
