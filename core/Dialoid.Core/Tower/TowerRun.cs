using Dialoid.Core.Simulation;

namespace Dialoid.Core.Tower;

public enum RunStage { Floor, Summit, Won, Exited }

/// <summary>Reward of a cleared floor (<see cref="FloorIndex"/> from 0) or of the summit (null).</summary>
public sealed record FloorReward(int? FloorIndex, QualityBreakdown Quality, IReadOnlyList<(string CurrencyId, int Level, int Count)> Currency);

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

    public int CurrentFloor { get; private set; }
    public RunStage Stage { get; private set; } = RunStage.Floor;
    public int ZoneLevel { get; }
    public IReadOnlyList<FloorReward> Rewards => _rewards;

    public int Deaths(int? floorIndex)
    {
        if (floorIndex is not int i) return _summitDeaths;
        if (i < 0 || i >= _floorDeaths.Length) throw new ArgumentOutOfRangeException(nameof(floorIndex));
        return _floorDeaths[i];
    }

    public static TowerRefusal TryEnter(TowerData d, TowerAssembly assembly, TowerProgress progress, int characterLevel, IRandomSource rng, out TowerRun? run)
    {
        run = null;
        var spec = assembly.Spec;
        if (spec.Rank > progress.HighestUnlocked) return TowerRefusal.RankLocked;
        if (spec.Floors.Any(f => f.TypeId == null)) return TowerRefusal.EmptySlot;

        var towerIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in spec.Floors)
        {
            var floorIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var m in f.Mods)
                if (!ModAllowed(d, spec.Rank, m) || !floorIds.Add(m.ModId)) return TowerRefusal.ModAboveRank;
        }
        foreach (var m in spec.TowerMods)
            if (!ModAllowed(d, spec.Rank, m) || !towerIds.Add(m.ModId)) return TowerRefusal.ModAboveRank;

        if (spec.UberBossId == null) return TowerRefusal.NoUberBoss;

        int zone = RankRules.ZoneLevel(d.Rules, spec.Rank, characterLevel);
        run = new TowerRun(d, spec, assembly.ShuffleBonusActive, zone, rng);
        return TowerRefusal.None;
    }

    private static bool ModAllowed(TowerData d, int rank, ModInstance m)
    {
        if (!d.TryGetMod(m.ModId, out var def)) return false;
        var tier = def.Tiers.FirstOrDefault(t => t.Tier == m.Tier);
        if (tier == null) return false;
        if (m.Value < tier.Min || m.Value > tier.Max) return false;
        if (m.Tier > RankRules.Def(d.Rules, rank).MaxTier) return false;
        return RankRules.CanApply(d.Rules, rank, def.Kind, m.Tier);
    }

    public FloorReward WinFloor(Wallet wallet)
    {
        Require(RunStage.Floor);
        int k = CurrentFloor;
        var quality = Tower.Rewards.FloorQuality(_data, _spec, k, _floorDeaths[k] == 0, _shuffleBonus);
        var currency = Tower.Rewards.RollCurrencyDrop(_data, _spec.Rank, k, _rng);
        var reward = Grant(wallet, new FloorReward(k, quality, currency));
        if (++CurrentFloor >= _data.Rules.Floors) Stage = RunStage.Summit;
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
