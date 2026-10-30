using Dialoid.Core.Simulation;

namespace Dialoid.Core.Tower;

public enum RunStage { Floor, Summit, Won, Exited }

/// <summary>Reward of a cleared floor (<see cref="FloorIndex"/> from 0) or of the summit (null).</summary>
public sealed record FloorReward(int? FloorIndex, QualityBreakdown Quality, IReadOnlyList<CurrencyDrop> Currency);

/// <summary>What leaving the run loses: floors not yet cleared (indices from 0) and the summit.</summary>
public sealed record ExitLoss(bool LostSummit, IReadOnlyList<int> LostFloors);

/// <summary>Opened ranks of the player. Rank N+1 opens after the uber boss of rank N is killed and the character level allows it.</summary>
public sealed class TowerProgress
{
    private readonly HashSet<int> _uberKilled = new();

    public int HighestUnlocked { get; private set; } = 1;

    public bool UberKilled(int rank) => _uberKilled.Contains(rank);

    internal void RecordUberKill(int rank) => _uberKilled.Add(rank);

    /// <summary>Opens following ranks while the uber boss of the highest one is dead and the level allows the next. True if at least one opened.</summary>
    public bool TryUnlock(TowerRules r, int characterLevel)
    {
        bool opened = false;
        while (HighestUnlocked < r.Ranks.Count && UberKilled(HighestUnlocked) && RankRules.LevelAllows(r, HighestUnlocked + 1, characterLevel))
        {
            HighestUnlocked++;
            opened = true;
        }
        return opened;
    }
}

/// <summary>One run through a tower. The spec is a snapshot taken at entry; rewards are given right after each floor.</summary>
public sealed class TowerRun
{
    private readonly TowerData _data;
    private readonly TowerSpec _spec;
    private readonly bool _shuffleBonus;
    private readonly IRandomSource _rng;
    private readonly int[] _floorDeaths;
    private int _summitDeaths;
    private readonly List<FloorReward> _rewards = new();

    private TowerRun(TowerData data, TowerSpec spec, bool shuffleBonus, int zoneLevel, IRandomSource rng)
    {
        _data = data;
        _spec = spec;
        _shuffleBonus = shuffleBonus;
        ZoneLevel = zoneLevel;
        _rng = rng;
        _floorDeaths = new int[data.Rules.Floors];
    }

    /// <summary>Floor being played, 0–4 (0..Floors − 1). On the summit and after it stays on the last floor.</summary>
    public int CurrentFloor { get; private set; }
    /// <summary>The tower as it was at entry (for the in-run list of active mods); never changes during the run.</summary>
    public TowerSpec Spec => _spec;
    public RunStage Stage { get; private set; } = RunStage.Floor;
    public int ZoneLevel { get; }
    public IReadOnlyList<FloorReward> Rewards => _rewards;

    public int Deaths(int? floorIndex)
    {
        if (floorIndex is not int i) return _summitDeaths;
        if (i < 0 || i >= _floorDeaths.Length) throw new ArgumentOutOfRangeException(nameof(floorIndex));
        return _floorDeaths[i];
    }

    /// <summary>
    /// Entry check, the last filter for hand-built and imported towers. Order: <see cref="TowerRefusal.RankLocked"/>,
    /// <see cref="TowerRefusal.EmptySlot"/>, <see cref="TowerRefusal.InvalidMods"/> (slot overflow, unknown id, mod of the wrong kind or
    /// not fitting the floor type and variant, tier that does not exist, value outside the tier, duplicate id or group on one target),
    /// <see cref="TowerRefusal.ModAboveRank"/> (kind or tier the rank does not allow), <see cref="TowerRefusal.NoUberBoss"/>.
    /// </summary>
    public static TowerRefusal TryEnter(TowerData d, TowerAssembly assembly, TowerProgress progress, int characterLevel, IRandomSource rng, out TowerRun? run)
    {
        run = null;
        var spec = assembly.Spec;
        if (spec.Rank > progress.HighestUnlocked) return TowerRefusal.RankLocked;
        if (spec.Floors.Any(f => f.TypeId == null)) return TowerRefusal.EmptySlot;

        foreach (var f in spec.Floors)
            if (!d.TryGetFloorType(f.TypeId!, out var type) || !FloorModsValid(d, type, f)) return TowerRefusal.InvalidMods;
        if (!ModsValid(d, spec.TowerMods, d.Rules.TowerSlots, def => def.Kind == ModKind.Tower)) return TowerRefusal.InvalidMods;

        if (spec.Floors.SelectMany(f => f.Mods).Concat(spec.TowerMods).Any(m => !RankRules.CanApply(d.Rules, spec.Rank, d.Mod(m.ModId).Kind, m.Tier)))
            return TowerRefusal.ModAboveRank;

        if (spec.UberBossId == null) return TowerRefusal.NoUberBoss;

        int zone = RankRules.ZoneLevel(d.Rules, spec.Rank, characterLevel);
        run = new TowerRun(d, spec, assembly.ShuffleBonusActive, zone, rng);
        return TowerRefusal.None;
    }

    /// <summary>Floor mods: not tower mods, fit the floor's type and, for a type mod, its variant.</summary>
    private static bool FloorModsValid(TowerData d, FloorTypeDef type, FloorSpec f) =>
        ModsValid(d, f.Mods, d.Rules.FloorSlots,
            def => def.Kind != ModKind.Tower && def.FitsType(type.Id) && (def.Variant == null || def.Variant == f.Variant));

    /// <summary>
    /// Rank-independent validity of one target's mods: no more than <paramref name="slots"/>, known ids, each fits the target,
    /// a tier that exists and a value inside it, no id twice, no group twice.
    /// </summary>
    private static bool ModsValid(TowerData d, IReadOnlyList<ModInstance> mods, int slots, Func<FloorModDef, bool> fits)
    {
        if (mods.Count > slots) return false;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var groups = new HashSet<string>(StringComparer.Ordinal);
        foreach (var m in mods)
        {
            if (!d.TryGetMod(m.ModId, out var def) || !fits(def)) return false;
            var tier = def.Tiers.FirstOrDefault(t => t.Tier == m.Tier);
            if (tier == null || !(m.Value >= tier.Min && m.Value <= tier.Max)) return false;
            if (!ids.Add(def.Id)) return false;
            if (def.Group != null && !groups.Add(def.Group)) return false;
        }
        return true;
    }

    /// <summary>Clears the current floor. After the last floor <see cref="CurrentFloor"/> stays on it and the stage becomes the summit.</summary>
    public FloorReward WinFloor(Wallet wallet)
    {
        Require(RunStage.Floor);
        int k = CurrentFloor;
        var quality = Tower.Rewards.FloorQuality(_data, _spec, k, _floorDeaths[k] == 0, _shuffleBonus);
        var currency = Tower.Rewards.RollCurrencyDrop(_data, _spec.Rank, k, _rng);
        var reward = Grant(wallet, new FloorReward(k, quality, currency));
        if (k + 1 >= _data.Rules.Floors) Stage = RunStage.Summit;
        else CurrentFloor = k + 1;
        return reward;
    }

    public void Die()
    {
        if (Stage == RunStage.Floor) _floorDeaths[CurrentFloor]++;
        else if (Stage == RunStage.Summit) _summitDeaths++;
        else throw Wrong(nameof(Die));
    }

    public FloorReward WinSummit(Wallet wallet, TowerProgress progress)
    {
        Require(RunStage.Summit);
        var quality = Tower.Rewards.SummitQuality(_data, _spec, _summitDeaths == 0, _shuffleBonus);
        var currency = Tower.Rewards.RollCurrencyDrop(_data, _spec.Rank, null, _rng);
        var reward = Grant(wallet, new FloorReward(null, quality, currency));
        Stage = RunStage.Won;
        progress.RecordUberKill(_spec.Rank);
        return reward;
    }

    public ExitLoss PreviewExit()
    {
        if (Stage == RunStage.Summit) return new ExitLoss(true, Array.Empty<int>());
        if (Stage != RunStage.Floor) throw Wrong(nameof(PreviewExit));
        var lost = Enumerable.Range(CurrentFloor, _data.Rules.Floors - CurrentFloor).ToArray();
        return new ExitLoss(true, lost);
    }

    public ExitLoss Exit()
    {
        var loss = PreviewExit();
        Stage = RunStage.Exited;
        return loss;
    }

    private FloorReward Grant(Wallet wallet, FloorReward reward)
    {
        foreach (var (id, level, count) in reward.Currency) wallet.Add(id, level, count);
        _rewards.Add(reward);
        return reward;
    }

    private void Require(RunStage stage)
    {
        if (Stage != stage) throw Wrong(stage.ToString());
    }

    private InvalidOperationException Wrong(string what) => new($"Действие недоступно на стадии {Stage} ({what})");
}
