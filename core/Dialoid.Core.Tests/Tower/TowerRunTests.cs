using Dialoid.Core.Simulation;
using Dialoid.Core.Tests.Fixtures;
using Dialoid.Core.Tower;

namespace Dialoid.Core.Tests.Tower;

public class TowerRunTests
{
    private static readonly TowerData D = TowerDataLoader.Load(FixturePaths.RealTowerData).Data!;

    private static IRandomSource Rng(int seed = 1) => RandomSources.Create(SimMode.Roll, seed);

    private static FloorSpec Floor(string type = "clear", params ModInstance[] mods) =>
        new(type, null, D.Biomes[0], mods);

    private static ModInstance Max(string id, int tier = 1) => new(id, tier, D.Mod(id).Tiers.First(t => t.Tier == tier).Max);

    private static TowerSpec Spec(int rank, IEnumerable<FloorSpec>? floors = null, string? uber = "boss", params ModInstance[] towerMods) =>
        new(rank, (floors ?? Enumerable.Range(0, 5).Select(_ => Floor())).ToList(), towerMods, uber);

    private static TowerAssembly Asm(TowerSpec s) => TowerAssembly.FromSpec(D, s, Rng());

    private static TowerProgress Unlocked(int rank)
    {
        var p = new TowerProgress();
        for (int r = 1; r < rank; r++) p.RecordUberKill(r);
        Assert.True(rank == 1 || p.TryUnlock(D.Rules, 60));
        return p;
    }

    private static TowerRun Enter(TowerSpec s, int level = 60, int seed = 7)
    {
        var refusal = TowerRun.TryEnter(D, Asm(s), Unlocked(s.Rank), level, Rng(seed), out var run);
        Assert.Equal(TowerRefusal.None, refusal);
        return run!;
    }

    private static string KindMod(ModKind k) => D.Mods.First(m => m.Kind == k).Id;

    [Fact]
    public void Enter_refuses_empty_slot_locked_rank_and_mod_above_rank()
    {
        var empty = new FloorSpec(null, null, null, Array.Empty<ModInstance>());
        var floors = new[] { Floor(), Floor(), empty, Floor(), Floor() };
        Assert.Equal(TowerRefusal.EmptySlot, TowerRun.TryEnter(D, Asm(Spec(1, floors)), new TowerProgress(), 10, Rng(), out var run));
        Assert.Null(run);

        Assert.Equal(TowerRefusal.RankLocked, TowerRun.TryEnter(D, Asm(Spec(2)), new TowerProgress(), 20, Rng(), out run));
        Assert.Null(run);

        // rank check goes first, then empty slot
        Assert.Equal(TowerRefusal.RankLocked, TowerRun.TryEnter(D, Asm(Spec(2, floors)), new TowerProgress(), 20, Rng(), out run));

        // floor_type mod is not allowed on rank 1
        var ft = KindMod(ModKind.FloorType);
        var def = D.Mod(ft);
        var ftFloors = new[] { new FloorSpec(def.FloorTypes[0], null, D.Biomes[0], new[] { Max(ft) }), Floor(), Floor(), Floor(), Floor() };
        Assert.Equal(TowerRefusal.ModAboveRank, TowerRun.TryEnter(D, Asm(Spec(1, ftFloors)), new TowerProgress(), 10, Rng(), out run));
        Assert.Null(run);

        // tower mod is not allowed on rank 1 either
        var tm = KindMod(ModKind.Tower);
        Assert.Equal(TowerRefusal.ModAboveRank, TowerRun.TryEnter(D, Asm(Spec(1, null, "boss", Max(tm))), new TowerProgress(), 10, Rng(), out run));

        // tier above the rank cap
        var gen = KindMod(ModKind.General);
        var hi = D.Mod(gen).Tiers.First(t => t.Tier == 2);
        var tierFloors = new[] { Floor("clear", new ModInstance(gen, 2, hi.Max)), Floor(), Floor(), Floor(), Floor() };
        Assert.Equal(TowerRefusal.ModAboveRank, TowerRun.TryEnter(D, Asm(Spec(1, tierFloors)), new TowerProgress(), 10, Rng(), out run));

        // no uber boss
        Assert.Equal(TowerRefusal.NoUberBoss, TowerRun.TryEnter(D, Asm(Spec(1, null, null)), new TowerProgress(), 10, Rng(), out run));
        Assert.Null(run);

        Assert.Equal(TowerRefusal.None, TowerRun.TryEnter(D, Asm(Spec(1)), new TowerProgress(), 10, Rng(), out run));
        Assert.NotNull(run);
        Assert.Equal(RunStage.Floor, run!.Stage);
        Assert.Equal(0, run.CurrentFloor);
    }

    [Fact]
    public void Enter_refuses_value_out_of_range_and_duplicate_mods()
    {
        var gen = D.Mods.Where(m => m.Kind == ModKind.General && m.FloorTypes.Count == 0).Take(2).ToList();
        string a = gen[0].Id, b = gen[1].Id;
        var tier = D.Mod(a).Tiers.First(t => t.Tier == 1);

        TowerRefusal Try(TowerSpec s) => TowerRun.TryEnter(D, Asm(s), Unlocked(5), 60, Rng(), out _);
        TowerSpec WithFloor0(params ModInstance[] mods) =>
            Spec(5, new[] { Floor("clear", mods), Floor(), Floor(), Floor(), Floor() });

        Assert.Equal(TowerRefusal.None, Try(WithFloor0(new ModInstance(a, 1, tier.Min))));
        Assert.Equal(TowerRefusal.ModAboveRank, Try(WithFloor0(new ModInstance(a, 1, tier.Max + 1))));
        Assert.Equal(TowerRefusal.ModAboveRank, Try(WithFloor0(new ModInstance(a, 1, tier.Min - 1))));

        Assert.Equal(TowerRefusal.None, Try(WithFloor0(Max(a), Max(b))));
        Assert.Equal(TowerRefusal.ModAboveRank, Try(WithFloor0(Max(a), Max(a))));

        // the same mod on two different floors is fine
        Assert.Equal(TowerRefusal.None, Try(Spec(5, new[] { Floor("clear", Max(a)), Floor("clear", Max(a)), Floor(), Floor(), Floor() })));

        var tm = D.Mods.Where(m => m.Kind == ModKind.Tower).Take(2).ToList();
        Assert.Equal(TowerRefusal.None, Try(Spec(5, null, "boss", Max(tm[0].Id), Max(tm[1].Id))));
        Assert.Equal(TowerRefusal.ModAboveRank, Try(Spec(5, null, "boss", Max(tm[0].Id), Max(tm[0].Id))));
    }

    [Fact]
    public void Run_keeps_snapshot_of_assembly()
    {
        var a = Asm(Spec(5));
        Assert.Equal(TowerRefusal.None, TowerRun.TryEnter(D, a, Unlocked(5), 60, Rng(), out var run));
        Assert.False(a.ShuffleBonusActive);

        a.Shuffle(new Wallet(D));
        Assert.True(a.ShuffleBonusActive);
        a.SetUberBoss("other");

        var first = run!.WinFloor(new Wallet(D));
        Assert.Equal(1.0, first.Quality.Shuffle, 12);

        var control = Enter(Spec(5), 60, 7);
        Assert.Equal(control.WinFloor(new Wallet(D)).Quality, first.Quality);
    }

    [Fact]
    public void Floor_reward_is_granted_immediately()
    {
        var run = Enter(Spec(5));
        var wallet = new Wallet(D);
        var r = run.WinFloor(wallet);
        Assert.Equal(0, r.FloorIndex);
        Assert.Single(run.Rewards);
        Assert.Same(r, run.Rewards[0]);
        Assert.Equal(1, run.CurrentFloor);
        foreach (var (id, level, count) in r.Currency)
            Assert.True(wallet.Levels(id)[level] >= count);

        for (int i = 1; i < 5; i++) run.WinFloor(wallet);
        Assert.Equal(RunStage.Summit, run.Stage);
        Assert.Equal(5, run.Rewards.Count);
    }

    [Fact]
    public void Death_restarts_floor_keeps_mods_and_rewards()
    {
        var run = Enter(Spec(5));
        var wallet = new Wallet(D);
        run.WinFloor(wallet);
        run.Die();
        Assert.Equal(1, run.CurrentFloor);
        Assert.Equal(RunStage.Floor, run.Stage);
        Assert.Equal(1, run.Deaths(1));
        Assert.Equal(0, run.Deaths(0));
        Assert.Single(run.Rewards);
        var r = run.WinFloor(wallet);
        Assert.Equal(1, r.FloorIndex);
        Assert.Equal(2, run.CurrentFloor);
    }

    [Fact]
    public void Death_removes_no_death_bonus_only_for_that_floor()
    {
        var run = Enter(Spec(5));
        var wallet = new Wallet(D);
        run.Die();
        var f0 = run.WinFloor(wallet);
        var f1 = run.WinFloor(wallet);
        Assert.Equal(1.0, f0.Quality.NoDeath, 12);
        Assert.True(f1.Quality.NoDeath > 1.0);
    }

    [Fact]
    public void Exit_before_summit_loses_summit()
    {
        var run = Enter(Spec(5));
        var wallet = new Wallet(D);
        for (int i = 0; i < 3; i++) run.WinFloor(wallet);
        var preview = run.PreviewExit();
        Assert.True(preview.LostSummit);
        Assert.Equal(new[] { 3, 4 }, preview.LostFloors);
        Assert.Equal(RunStage.Floor, run.Stage);

        var loss = run.Exit();
        Assert.True(loss.LostSummit);
        Assert.Equal(new[] { 3, 4 }, loss.LostFloors);
        Assert.Equal(RunStage.Exited, run.Stage);
        Assert.Equal(3, run.Rewards.Count);
    }

    [Fact]
    public void Exit_at_summit_loses_only_summit()
    {
        var run = Enter(Spec(5));
        for (int i = 0; i < 5; i++) run.WinFloor(new Wallet(D));
        var loss = run.PreviewExit();
        Assert.True(loss.LostSummit);
        Assert.Empty(loss.LostFloors);
        run.Exit();
        Assert.Equal(RunStage.Exited, run.Stage);
    }

    [Fact]
    public void Summit_death_keeps_floor_rewards()
    {
        var run = Enter(Spec(5));
        var wallet = new Wallet(D);
        var progress = new TowerProgress();
        for (int i = 0; i < 5; i++) run.WinFloor(wallet);
        run.Die();
        Assert.Equal(RunStage.Summit, run.Stage);
        Assert.Equal(1, run.Deaths(null));
        Assert.Equal(5, run.Rewards.Count);
        var s = run.WinSummit(wallet, progress);
        Assert.Null(s.FloorIndex);
        Assert.Equal(1.0, s.Quality.NoDeath, 12);
        Assert.Equal(RunStage.Won, run.Stage);
        Assert.Equal(6, run.Rewards.Count);
        Assert.True(progress.UberKilled(5));
    }

    [Fact]
    public void Shuffle_bonus_reaches_rewards()
    {
        var plain = Asm(Spec(5));
        TowerRun.TryEnter(D, plain, Unlocked(5), 60, Rng(), out var r1);
        var shuffled = Asm(Spec(5));
        shuffled.Shuffle(new Wallet(D));
        shuffled.SetUberBoss("boss");
        Assert.True(shuffled.ShuffleBonusActive);
        Assert.Equal(TowerRefusal.None, TowerRun.TryEnter(D, shuffled, Unlocked(5), 60, Rng(), out var r2));

        Assert.Equal(1.0, r1!.WinFloor(new Wallet(D)).Quality.Shuffle, 12);
        Assert.True(r2!.WinFloor(new Wallet(D)).Quality.Shuffle > 1.0);
    }

    [Fact]
    public void Rank_unlocks_after_uber_even_if_level_comes_later()
    {
        var p = new TowerProgress();
        Assert.Equal(1, p.HighestUnlocked);
        Assert.False(p.TryUnlock(D.Rules, 60));

        p.RecordUberKill(1);
        Assert.True(p.TryUnlock(D.Rules, 12));
        Assert.Equal(2, p.HighestUnlocked);

        Assert.Equal(TowerRefusal.None, TowerRun.TryEnter(D, Asm(Spec(2)), p, 22, Rng(), out var run));
        for (int i = 0; i < 5; i++) run!.WinFloor(new Wallet(D));
        run!.WinSummit(new Wallet(D), p);
        Assert.True(p.UberKilled(2));
        Assert.False(p.TryUnlock(D.Rules, 22));
        Assert.Equal(2, p.HighestUnlocked);
        Assert.True(p.TryUnlock(D.Rules, 24));
        Assert.Equal(3, p.HighestUnlocked);
        Assert.False(p.TryUnlock(D.Rules, 60));
    }

    [Fact]
    public void Unlock_loops_over_several_ranks_and_stops_at_last()
    {
        var p = new TowerProgress();
        for (int r = 1; r <= D.Rules.Ranks.Count; r++) p.RecordUberKill(r);
        Assert.True(p.TryUnlock(D.Rules, 60));
        Assert.Equal(D.Rules.Ranks.Count, p.HighestUnlocked);
        Assert.False(p.TryUnlock(D.Rules, 60));
    }

    [Fact]
    public void Zone_level_fixed_for_the_run()
    {
        var run = Enter(Spec(3), 30);
        Assert.Equal(RankRules.ZoneLevel(D.Rules, 3, 30), run.ZoneLevel);
        int z = run.ZoneLevel;
        run.WinFloor(new Wallet(D));
        run.Die();
        Assert.Equal(z, run.ZoneLevel);
    }

    [Fact]
    public void Same_seed_gives_same_rewards()
    {
        var w1 = new Wallet(D);
        var w2 = new Wallet(D);
        var a = Enter(Spec(5), 60, 99);
        var b = Enter(Spec(5), 60, 99);
        for (int i = 0; i < 5; i++)
        {
            var x = a.WinFloor(w1);
            var y = b.WinFloor(w2);
            Assert.Equal(x.Quality, y.Quality);
            Assert.Equal(x.Currency, y.Currency);
        }
    }

    [Fact]
    public void Wrong_stage_throws()
    {
        var run = Enter(Spec(5));
        var wallet = new Wallet(D);
        var progress = new TowerProgress();
        Assert.Throws<InvalidOperationException>(() => run.WinSummit(wallet, progress));
        for (int i = 0; i < 5; i++) run.WinFloor(wallet);
        Assert.Throws<InvalidOperationException>(() => run.WinFloor(wallet));
        run.WinSummit(wallet, progress);
        Assert.Throws<InvalidOperationException>(() => run.WinSummit(wallet, progress));
        Assert.Throws<InvalidOperationException>(() => run.Die());
        Assert.Throws<InvalidOperationException>(() => run.Exit());
        Assert.Throws<InvalidOperationException>(() => run.PreviewExit());

        var run2 = Enter(Spec(5));
        run2.Exit();
        Assert.Throws<InvalidOperationException>(() => run2.WinFloor(wallet));
        Assert.Throws<InvalidOperationException>(() => run2.Die());
        Assert.Throws<InvalidOperationException>(() => run2.Exit());
    }
}
