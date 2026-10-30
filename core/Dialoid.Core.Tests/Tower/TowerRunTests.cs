using Dialoid.Core.Simulation;
using Dialoid.Core.Tests.Fixtures;
using Dialoid.Core.Tower;

namespace Dialoid.Core.Tests.Tower;

public class TowerRunTests
{
    private static readonly TowerData D = TowerDataLoader.Load(FixturePaths.RealTowerData).Data!;

    private static IRandomSource Rng(int seed = 1) => RandomSources.Create(SimMode.Roll, seed);

    private static FloorSpec Floor(string type = "clear", params ModInstance[] mods) =>
        new(type, null, D.Biomes[0].Id, mods);

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
        var ftFloors = new[] { new FloorSpec(def.FloorTypes[0], null, D.Biomes[0].Id, new[] { Max(ft) }), Floor(), Floor(), Floor(), Floor() };
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
    public void Enter_refuses_value_out_of_range_and_duplicate_mods_as_invalid()
    {
        var gen = D.Mods.Where(m => m.Kind == ModKind.General && m.FloorTypes.Count == 0).Take(2).ToList();
        string a = gen[0].Id, b = gen[1].Id;
        var tier = D.Mod(a).Tiers.First(t => t.Tier == 1);

        TowerRefusal Try(TowerSpec s) => TowerRun.TryEnter(D, Asm(s), Unlocked(5), 60, Rng(), out _);
        TowerSpec WithFloor0(params ModInstance[] mods) =>
            Spec(5, new[] { Floor("clear", mods), Floor(), Floor(), Floor(), Floor() });

        Assert.Equal(TowerRefusal.None, Try(WithFloor0(new ModInstance(a, 1, tier.Min))));
        Assert.Equal(TowerRefusal.InvalidMods, Try(WithFloor0(new ModInstance(a, 1, tier.Max + 1))));
        Assert.Equal(TowerRefusal.InvalidMods, Try(WithFloor0(new ModInstance(a, 1, tier.Min - 1))));
        Assert.Equal(TowerRefusal.InvalidMods, Try(WithFloor0(new ModInstance(a, 1, double.NaN))));
        Assert.Equal(TowerRefusal.InvalidMods, Try(WithFloor0(new ModInstance(a, 6, tier.Min))));
        Assert.Equal(TowerRefusal.InvalidMods, Try(WithFloor0(new ModInstance("nope", 1, 0))));

        Assert.Equal(TowerRefusal.None, Try(WithFloor0(Max(a), Max(b))));
        Assert.Equal(TowerRefusal.InvalidMods, Try(WithFloor0(Max(a), Max(a))));

        // the same mod on two different floors is fine
        Assert.Equal(TowerRefusal.None, Try(Spec(5, new[] { Floor("clear", Max(a)), Floor("clear", Max(a)), Floor(), Floor(), Floor() })));

        var tm = D.Mods.Where(m => m.Kind == ModKind.Tower).Take(2).ToList();
        Assert.Equal(TowerRefusal.None, Try(Spec(5, null, "boss", Max(tm[0].Id), Max(tm[1].Id))));
        Assert.Equal(TowerRefusal.InvalidMods, Try(Spec(5, null, "boss", Max(tm[0].Id), Max(tm[0].Id))));
    }

    private static TowerSpec WithFloor0(int rank, FloorSpec floor0, params ModInstance[] towerMods) =>
        Spec(rank, new[] { floor0, Floor(), Floor(), Floor(), Floor() }, "boss", towerMods);

    private static TowerRefusal TryAt5(TowerSpec s, TowerData? data = null) =>
        TowerRun.TryEnter(data ?? D, TowerAssembly.FromSpec(data ?? D, s, Rng()), Unlocked(5), 60, Rng(), out _);

    [Fact]
    public void Enter_refuses_group_conflict_on_floor_and_tower()
    {
        Assert.Equal("density", D.Mod("more_packs").Group);
        Assert.Equal("density", D.Mod("dense_packs").Group);
        Assert.Equal(TowerRefusal.InvalidMods, TryAt5(WithFloor0(5, Floor("clear", Max("more_packs"), Max("dense_packs")))));
        // the same group on two different floors is fine
        Assert.Equal(TowerRefusal.None, TryAt5(Spec(5, new[] { Floor("clear", Max("more_packs")), Floor("clear", Max("dense_packs")), Floor(), Floor(), Floor() })));

        // real data has no two tower mods of one group: give tower_elite_aura the group of tower_more_packs
        var mods = D.Mods.Select(m => m.Id == "tower_elite_aura" ? m with { Group = "density" } : m).ToList();
        var data = new TowerData(D.Rules, D.FloorTypes, D.Biomes, mods, D.Currencies);
        Assert.Equal(TowerRefusal.None, TryAt5(Spec(5, null, "boss", Max("tower_more_packs")), data));
        Assert.Equal(TowerRefusal.InvalidMods, TryAt5(Spec(5, null, "boss", Max("tower_more_packs"), Max("tower_elite_aura")), data));
        // a tower mod and a floor mod of one group do not conflict: they sit on different targets
        Assert.Equal(TowerRefusal.None, TryAt5(WithFloor0(5, Floor("clear", Max("more_packs")), Max("tower_more_packs"))));
    }

    [Fact]
    public void Enter_refuses_more_mods_than_slots()
    {
        string[] general = { "monster_life", "monster_damage", "rare_leaders", "elite_explode", "reward_quantity", "no_life_regen" };
        Assert.Equal(D.Rules.FloorSlots + 1, general.Length);
        Assert.Equal(TowerRefusal.None, TryAt5(WithFloor0(5, Floor("clear", general.Take(D.Rules.FloorSlots).Select(id => Max(id)).ToArray()))));
        Assert.Equal(TowerRefusal.InvalidMods, TryAt5(WithFloor0(5, Floor("clear", general.Select(id => Max(id)).ToArray()))));

        var tower = D.Mods.Where(m => m.Kind == ModKind.Tower).Select(m => Max(m.Id)).ToArray();
        Assert.Equal(D.Rules.TowerSlots + 1, tower.Length);
        Assert.Equal(TowerRefusal.None, TryAt5(Spec(5, null, "boss", tower.Take(D.Rules.TowerSlots).ToArray())));
        Assert.Equal(TowerRefusal.InvalidMods, TryAt5(Spec(5, null, "boss", tower)));
    }

    [Fact]
    public void Enter_refuses_mods_that_do_not_fit_floor_or_target()
    {
        // type mod of another floor type
        Assert.Equal(TowerRefusal.InvalidMods, TryAt5(WithFloor0(5, Floor("clear", Max("extra_wave")))));
        Assert.Equal(TowerRefusal.None, TryAt5(WithFloor0(5, Floor("waves", Max("extra_wave")))));
        // general mod limited to some types
        Assert.Equal(TowerRefusal.InvalidMods, TryAt5(WithFloor0(5, Floor("bosses", Max("more_packs")))));
        // trial mod of another variant
        var hold = Max("hold_decay");
        Assert.Equal(TowerRefusal.InvalidMods, TryAt5(WithFloor0(5, new FloorSpec("trial", "escort", D.Biomes[0].Id, new[] { hold }))));
        Assert.Equal(TowerRefusal.None, TryAt5(WithFloor0(5, new FloorSpec("trial", "hold", D.Biomes[0].Id, new[] { hold }))));
        // kind mismatch: a tower mod on a floor, a floor mod on the tower
        Assert.Equal(TowerRefusal.InvalidMods, TryAt5(WithFloor0(5, Floor("clear", Max("tower_reward")))));
        Assert.Equal(TowerRefusal.InvalidMods, TryAt5(Spec(5, null, "boss", Max("monster_life"))));
        Assert.Equal(TowerRefusal.InvalidMods, TryAt5(Spec(5, null, "boss", Max("extra_wave"))));
    }

    [Fact]
    public void Enter_check_order_is_rank_empty_invalid_above_rank_uber()
    {
        var empty = new FloorSpec(null, null, null, Array.Empty<ModInstance>());
        var gen = KindMod(ModKind.General);
        var tier2 = new ModInstance(gen, 2, D.Mod(gen).Tiers[1].Max);
        TowerRefusal Try1(TowerSpec s) => TowerRun.TryEnter(D, Asm(s), new TowerProgress(), 10, Rng(), out _);

        // invalid (duplicate) mods above the rank on rank 1: InvalidMods wins over ModAboveRank
        var dupHigh = Spec(1, new[] { Floor("clear", tier2, tier2), Floor(), Floor(), Floor(), Floor() }, null);
        Assert.Equal(TowerRefusal.InvalidMods, Try1(dupHigh));
        // empty slot wins over invalid mods
        Assert.Equal(TowerRefusal.EmptySlot, Try1(dupHigh with { Floors = dupHigh.Floors.Take(4).Append(empty).ToList() }));
        // above rank wins over no uber boss
        Assert.Equal(TowerRefusal.ModAboveRank, Try1(Spec(1, new[] { Floor("clear", tier2), Floor(), Floor(), Floor(), Floor() }, null)));
        // tower mod on rank 1 is a rank reason, not an invalid tower
        Assert.Equal(TowerRefusal.ModAboveRank, Try1(Spec(1, null, "boss", Max("tower_reward"))));
    }

    [Fact]
    public void Current_floor_stays_on_last_floor_at_summit()
    {
        var run = Enter(Spec(5));
        var wallet = new Wallet(D);
        for (int i = 0; i < 4; i++) run.WinFloor(wallet);
        Assert.Equal(4, run.CurrentFloor);
        Assert.Equal(RunStage.Floor, run.Stage);
        run.WinFloor(wallet);
        Assert.Equal(RunStage.Summit, run.Stage);
        Assert.Equal(4, run.CurrentFloor);
        run.Die();
        Assert.Equal(4, run.CurrentFloor);
        Assert.Equal(0, run.Deaths(4));
        run.WinSummit(wallet, new TowerProgress());
        Assert.Equal(4, run.CurrentFloor);
    }

    [Fact]
    public void Run_exposes_entry_spec()
    {
        var spec = WithFloor0(5, Floor("clear", Max("monster_life")), Max("tower_reward"));
        var a = Asm(spec);
        Assert.Equal(TowerRefusal.None, TowerRun.TryEnter(D, a, Unlocked(5), 60, Rng(), out var run));
        var atEntry = run!.Spec;
        Assert.Equal(TowerCodec.ToJson(a.Spec), TowerCodec.ToJson(atEntry));

        a.SetFloorType(0, "waves");
        a.SetUberBoss("other");
        Assert.Same(atEntry, run.Spec);
        Assert.Equal("clear", run.Spec.Floors[0].TypeId);
        Assert.Equal("boss", run.Spec.UberBossId);
        Assert.Equal(new[] { "tower_reward" }, run.Spec.TowerMods.Select(m => m.ModId));
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
