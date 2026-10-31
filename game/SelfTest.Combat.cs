using Dialoid.Core.Combat;
using Dialoid.Core.Simulation;
using Dialoid.Game.Room;
using Dialoid.Game.Ui;
using Dialoid.Sandbox;
using Godot;

namespace Dialoid.Game;

/// <summary>
/// Self-test steps of the live fight in the 3D room (plan 4 task 9): presses through <see cref="CombatDriver"/> (never the
/// global Input), refusals over the hero, reach and visibility, a channel left by walking away, hit-stop on pictures only, the
/// melee contact frame, DoT ticks without reactions, the target ring on «Рой», the dummy's light, preset looks and the outline.
/// The fight is stepped by <see cref="CombatDriver.Tick"/> with fixed frames (game time, not wall time). They pass with and
/// without assets (capsules).
/// </summary>
public sealed partial class SelfTest
{
    private const string WarriorMelee = "warrior_fire_cleave_crit.json";
    private const string Pyromancer = "sorcerer_pyromancer_fireball.json";
    private const double FrameSec = 1.0 / 60;

    private CombatDriver Driver => _main.Combat ?? throw new Failure("нет CombatDriver");
    private TrainingRoom Training => _main.Training ?? throw new Failure("нет TrainingRoom");

    private IEnumerable<Step> CombatSteps()
    {
        yield return new("combat.skill-animations", CombatSkillAnimations);
        yield return new("combat.live-fight", CombatLiveFight);
        yield return new("combat.refusal-cooldown", CombatRefusalCooldown);
        yield return new("combat.out-of-reach", CombatOutOfReach);
        yield return new("combat.target-not-visible", CombatTargetNotVisible);
        yield return new("combat.channel-walk-away", CombatChannelWalkAway);
        yield return new("combat.hit-stop", CombatHitStop);
        yield return new("combat.fire-particles", CombatFireParticles);
        yield return new("combat.melee-contact", CombatMeleeContact);
        yield return new("combat.projectile-lands-first", CombatProjectileLandsFirst);
        yield return new("combat.ticks-no-reaction", CombatTicks);
        yield return new("combat.swarm-ring", CombatSwarmRing);
        yield return new("combat.dummy-light", CombatDummyLight);
        yield return new("combat.presets-outline", CombatPresets);
        yield return new("combat.input-and-move", CombatInputAndMove);
        yield return new("combat.restore", CombatRestore);
    }

    // ---------- helpers ----------

    private async Task LoadForCombat(string file)
    {
        var info = _main.Builds!.References.FirstOrDefault(r => r.Path.EndsWith(file, StringComparison.Ordinal));
        Check(info != null, $"нет эталона {file}");
        Check(_main.Builds.Load(info!).Ok, $"эталон {file} не загрузился");
        _main.CloseAllWindows();
        await Settle();
        Check(Training.Running, $"бой не начался: {string.Join("; ", Training.Violations.Select(v => v.Message))}");
        if (Driver.Dummy.PresetId != TrainingRoom.DefaultDummyId) Driver.SetDummy(TrainingRoom.DefaultDummyId);
        Driver.ResetVisuals();
        Driver.SetProcess(false); // the steps drive the frames themselves
    }

    private int SlotOf(string skillId)
    {
        var info = Training.Slots.FirstOrDefault(s => s?.SkillId == skillId);
        Check(info != null, $"скилла {skillId} нет в слотах");
        return info!.Slot;
    }

    /// <summary>Places the hero 1 m from the collision edge of the first figure, on the camera side.</summary>
    private void PlaceNearDummy(float edge = 1f)
    {
        var room = Room;
        room.PlaceHero(room.Dummy.TargetPosition(0) + TowardsCamera * (room.Dummy.CollisionRadius(0) + HeroView.Radius + edge));
    }

    /// <summary>Steps the driver <paramref name="seconds"/> of game time in 60 fps frames; <paramref name="each"/> runs before each frame with the elapsed time.</summary>
    private async Task Run(double seconds, Action<double>? each = null, Func<bool>? until = null)
    {
        var frames = (int)Math.Ceiling(seconds / FrameSec);
        for (int i = 0; i < frames; i++)
        {
            each?.Invoke(i * FrameSec);
            Driver.Tick(FrameSec);
            if (until?.Invoke() == true) return;
            if (i % 10 == 9) await NextFrame(); // freed effects leave the tree
        }
    }

    private int RefusalLines(string? skillId) =>
        Training.Log.Where(l => l.Kind == LogKind.Refusal && (skillId == null || l.SkillId == skillId)).Sum(l => l.Repeat);

    // ---------- steps ----------

    /// <summary>The skill → clip table: cleave and whirlwind slash, crush and charge strike, the rest cast; unknown skills by tags.</summary>
    private Task CombatSkillAnimations()
    {
        var table = Driver.Animations;
        Check(table.Skills.Count > 0, "таблица skill_animations.json не загрузилась");
        foreach (var id in new[] { "cleave", "whirlwind" }) Check(table.For(id, default).ClipId == "hero_slash", $"{id} не slash");
        foreach (var id in new[] { "crush", "charge" }) Check(table.For(id, default).ClipId == "hero_attack", $"{id} не attack");
        foreach (var id in new[] { "fireball", "spark", "meteor", "throwing_axe" }) Check(table.For(id, default).ClipId == "hero_cast", $"{id} не casting");
        Check(table.For("unknown_skill", Dialoid.Core.Stats.TagSet.Of("attack", "melee")).ClipId == "hero_slash", "неизвестный ближний скилл не slash");
        Check(table.For("unknown_skill", Dialoid.Core.Stats.TagSet.Of("spell")).ClipId == "hero_cast", "неизвестное заклинание не casting");
        Check(table.Skills.Values.All(a => a.Contact is > 0 and < 1), "доля контакта вне (0, 1)");
        Check(Assets.Table.Clips.Keys.ToHashSet().IsSupersetOf(table.Skills.Values.Select(a => a.ClipId)), "клип таблицы скиллов не из assets.json");
        var a = table.For("cleave", default);
        Check(Mathf.IsEqualApprox((float)a.ContactSec(0.8), (float)(a.Contact * 0.8)), "кадр контакта не масштабируется с временем применения");
        return Task.CompletedTask;
    }

    /// <summary>A reference next to the dummy, synthetic keys for 5 s of game time: log, DPS of the segment, attack clips, flashes, numbers.</summary>
    private async Task CombatLiveFight()
    {
        await LoadForCombat(WarriorMelee);
        PlaceNearDummy();
        var hero = Room.Hero;
        var (attacks, flashes, numbers) = (hero.AttackCount, Room.Dummy.FlashCount, Room.Numbers.ShownTotal);
        int cleave = SlotOf("cleave"), crush = SlotOf("crush");
        var next = 0.0;
        var turn = 0;
        await Run(5.0, t =>
        {
            if (t < next) return;
            next = t + 0.3;
            Driver.PressSlot(turn++ % 3 == 2 ? crush : cleave);
        });
        Check(Training.Log.Count > 0, "журнал пуст");
        var segment = Training.Current ?? Training.Segments.LastOrDefault();
        Check(segment != null && segment.Dps > 0, $"DPS сегмента {segment?.Dps}");
        Check(hero.AttackCount > attacks, "анимация атаки не запускалась");
        if (hero.Model.Player != null && hero.LastAttack is { } last)
            Check(last.Clip is "hero_slash" or "hero_attack" && last.Speed > 0, $"клип атаки {last.Clip}, скорость {last.Speed}");
        Check(Room.Dummy.FlashCount > flashes, "вспышек на манекене нет");
        Check(Room.Numbers.ShownTotal > numbers, "всплывающих чисел нет");
        Check(Room.Numbers.Count <= FloatingNumbers.MaxShown, $"чисел на экране {Room.Numbers.Count}");
        GD.Print($"SELFTEST NOTE живой бой: 5 с, DPS {segment!.Dps:0}, атак {hero.AttackCount - attacks}, вспышек {Room.Dummy.FlashCount - flashes}, " +
                 $"реакций {Room.Dummy.ReactionCount}, hit-stop {Driver.HitStops}, последний клип {hero.LastAttack?.Clip} ×{hero.LastAttack?.Speed:0.##}");
    }

    /// <summary>A skill on cooldown: «перезарядка» in the log and its text over the hero.</summary>
    private async Task CombatRefusalCooldown()
    {
        await LoadForCombat(WarriorMelee);
        PlaceNearDummy();
        var charge = SlotOf("charge");
        Check(Training.Slots[charge - 1]!.CooldownSec > 0, "у Рывка нет перезарядки");
        var first = Driver.PressSlot(charge);
        Check(first.Accepted, $"первое нажатие Рывка отклонено: {first.Refusal}");
        await Run(1.5, until: () => Training.State is { Busy: false } s && s.CooldownLeftBySlot.ContainsKey(charge));
        Check(Training.State!.CooldownLeftBySlot.ContainsKey(charge), "Рывок не ушел на перезарядку");
        var lines = RefusalLines("charge");
        var second = Driver.PressSlot(charge);
        Check(!second.Accepted && second.Refusal == PressRefusal.Cooldown, $"второе нажатие: {second.Refusal}");
        Check(RefusalLines("charge") == lines + 1, "отказа «перезарядка» нет в журнале");
        await Run(FrameSec);
        Check(Room.Hero.RefusalText is { } text && text.StartsWith("Перезарядка", StringComparison.Ordinal), $"текст над героем «{Room.Hero.RefusalText}»");
        GD.Print($"SELFTEST NOTE отказ над героем: «{Room.Hero.RefusalText}»; в журнале: «{Training.Log.Last(l => l.Kind == LogKind.Refusal).Text}»");
    }

    /// <summary>8 m from the dummy a melee skill is refused «Вне досягаемости».</summary>
    private async Task CombatOutOfReach()
    {
        await LoadForCombat(WarriorMelee);
        PlaceNearDummy(8f);
        Check(Driver.HeroDistance() >= 8 - 1e-3, $"до манекена {Driver.HeroDistance():0.##} м");
        var r = Driver.PressSlot(SlotOf("cleave"));
        Check(!r.Accepted && r.Refusal == PressRefusal.OutOfReach, $"ближний скилл в 8 м: {r.Refusal}");
        await Run(FrameSec);
        Check(Room.Hero.RefusalText == "Вне досягаемости", $"текст над героем «{Room.Hero.RefusalText}»");
    }

    /// <summary>The dummy out of the frame: Meteor «Цель не видна», a projectile (Spark) is used.</summary>
    private async Task CombatTargetNotVisible()
    {
        await LoadForCombat(Pyromancer);
        var room = Room;
        room.PlaceHero(room.HeroSpawn);
        try
        {
            room.Dummy.Position = room.DummySpot + new Vector3(0, 0, -40);
            Check(!Driver.TargetOnScreen(), "манекен все еще в кадре");
            // A plain Meteor: the reference's tree may have made it a projectile («Звездопад»); re-slotting resets its tree.
            var meteorSlot = SlotOf("meteor");
            var starfall = Training.CompiledTags(meteorSlot)?.Contains("projectile") == true;
            if (starfall)
            {
                var session = _main.Session!;
                Check(session.SetSlotSkill(meteorSlot - 1, null).Ok && session.SetSlotSkill(meteorSlot - 1, "meteor").Ok, "Метеор не переставлен");
                await Settle();
                Check(Training.CompiledTags(meteorSlot)?.Contains("projectile") == false, "Метеор без узлов все еще снаряд");
            }
            var meteor = Driver.PressSlot(meteorSlot);
            Check(!meteor.Accepted && meteor.Refusal == PressRefusal.TargetNotVisible, $"Метеор вне кадра: {meteor.Refusal}");
            GD.Print($"SELFTEST NOTE вне кадра: Метеор{(starfall ? " (в эталоне снаряд со «Звездопадом», проверен без узлов)" : "")} — {meteor.Refusal}");
            await Run(FrameSec);
            Check(room.Hero.RefusalText == "Цель не видна", $"текст над героем «{room.Hero.RefusalText}»");
            var spark = Driver.PressSlot(SlotOf("spark"));
            Check(spark.Accepted, $"Искра вне кадра отклонена: {spark.Refusal}");
            var uses = room.Hero.AttackCount;
            await Run(0.5);
            Check(room.Hero.AttackCount > uses, "Искра не применилась");
        }
        finally
        {
            room.Dummy.Position = room.DummySpot;
            room.PlaceHero(room.HeroSpawn);
        }
        Check(Driver.TargetOnScreen(), "манекен не вернулся в кадр");
    }

    /// <summary>Whirlwind held, the hero walks away: the channel stops with exactly one refusal line; the release adds nothing.</summary>
    private async Task CombatChannelWalkAway()
    {
        await LoadForCombat(WarriorMelee);
        PlaceNearDummy();
        var ww = SlotOf("whirlwind");
        Check(Training.Slots[ww - 1]!.IsChannel, "Вихрь не канал");
        var r = Driver.PressSlot(ww);
        Check(r.Accepted, $"Вихрь не зажат: {r.Refusal}");
        await Run(0.6);
        Check(Training.State!.HeldSlot == ww, "канал Вихря не держится рядом с манекеном");
        var lines = RefusalLines("whirlwind");
        PlaceNearDummy(8f);
        await Run(0.5);
        Check(Training.State!.HeldSlot == null, "канал не прекратился, когда герой ушел");
        Check(RefusalLines("whirlwind") == lines + 1, $"строк отказа {RefusalLines("whirlwind") - lines}, а не одна");
        Driver.ReleaseSlot(ww);
        await Run(0.2);
        Check(RefusalLines("whirlwind") == lines + 1, "отпускание клавиши добавило отказ");
    }

    /// <summary>Hit-stop freezes animations and effects only: the engine and the tree run, the fight gets the real frame time.</summary>
    private async Task CombatHitStop()
    {
        await LoadForCombat(WarriorMelee);
        PlaceNearDummy();
        var driver = Driver;
        driver.SetProcess(true); // real frames this time
        try
        {
            await NextFrame();
            var t0 = Training.State!.Time;
            var (advanced, frozenAdvanced, visual) = (driver.Advanced, driver.AdvancedWhileFrozen, driver.VisualTime);
            var wall = Time.GetTicksUsec();
            driver.HitStop();
            int frames = 0;
            while (driver.Frozen && frames < 500)
            {
                Check(Engine.TimeScale == 1 && !_main.GetTree().Paused, "hit-stop тронул Engine.TimeScale или паузу дерева");
                if (Room.HeroModel.Player is { } hp) Check(hp.SpeedScale == 0, "анимация героя не замерла");
                foreach (var f in Room.Dummy.Figures)
                    if (f.Model.Player is { } dp) Check(dp.SpeedScale == 0, "анимация манекена не замерла");
                Check(Room.Effects.Paused, "эффекты не на паузе");
                Check(driver.VisualTime == visual, "время картинки шло во время замирания");
                await NextFrame();
                frames++;
            }
            Check(!driver.Frozen, "замирание не кончилось");
            var real = driver.Advanced - advanced;
            var frozen = driver.AdvancedWhileFrozen - frozenAdvanced;
            var core = Training.State!.Time - t0;
            Check(frozen >= CombatDriver.HitStopSec - 1e-9, $"за замирание в бой ушло {frozen * 1000:0.#} мс реального времени");
            Check(Math.Abs(core - real) <= LiveFight.StepSec + 1e-6, $"время боя выросло на {core:0.###} с при реальных {real:0.###} с");
            if (Room.HeroModel.Player is { } p) Check(p.SpeedScale == 1, "анимация героя не ожила");
            GD.Print($"SELFTEST NOTE hit-stop: {frames} кадров, реальное время {real * 1000:0} мс (из них замирание {frozen * 1000:0} мс), " +
                     $"время боя +{core * 1000:0} мс, по часам {(Time.GetTicksUsec() - wall) / 1000.0:0} мс");
        }
        finally
        {
            driver.SetProcess(false);
        }
    }

    /// <summary>A melee hit's flash is shown not before the clip's contact frame and not after the end of the use.</summary>
    private async Task CombatMeleeContact()
    {
        await LoadForCombat(WarriorMelee);
        PlaceNearDummy();
        var stops = Driver.HitStops;
        var reactions = Room.Dummy.ReactionCount;
        var r = Driver.PressSlot(SlotOf("cleave"));
        Check(r.Accepted, $"Рассекающий удар отклонен: {r.Refusal}");
        await Run(2.0, until: () => Driver.LastMelee is { SkillId: "cleave", FlashAt: not null });
        var m = Driver.LastMelee;
        Check(m is { FlashAt: not null }, "вспышка ближнего удара не показана");
        Check(m!.FlashAt >= m.Contact - 1e-9, $"вспышка в {m.FlashAt - m.Start:0.###} с, раньше контакта {m.Contact - m.Start:0.###} с");
        Check(m.FlashAt <= m.End + 1e-9, $"вспышка в {m.FlashAt - m.Start:0.###} с, позже конца применения {m.End - m.Start:0.###} с");
        Check(Driver.HitStops > stops, "на кадре контакта нет hit-stop");
        Check(Room.Dummy.ReactionCount > reactions, "манекен не отреагировал на удар");
        GD.Print($"SELFTEST NOTE контакт: применение {m.End - m.Start:0.###} с, контакт {m.Contact - m.Start:0.###} с, вспышка {m.FlashAt - m.Start:0.###} с");
    }

    /// <summary>
    /// The same projectile cast again while its previous ball is still flying (from ~8.5 m): every projectile's flash and number
    /// come after its own ball landed — the next cast never shows them early.
    /// </summary>
    private async Task CombatProjectileLandsFirst()
    {
        await LoadForCombat(Pyromancer);
        PlaceNearDummy(8.5f); // the longest flight (0.6 s), so the next cast comes while the ball flies
        var driver = Driver;
        var room = Room;
        driver.ProjectileLandings.Clear();
        var fireball = SlotOf("fireball");
        var casts = 0;
        var castsDuringFlight = 0;
        var attacks = room.Hero.AttackCount;
        await Run(4.0, _ =>
        {
            if (room.Hero.AttackCount != attacks)
            {
                attacks = room.Hero.AttackCount;
                casts++;
                if (room.Effects.Projectiles > 0) castsDuringFlight++;
            }
            // Pressed again while busy: the press waits in the queue and the next cast starts right at the end of this one.
            if (casts < 3) driver.PressSlot(fireball);
        });
        await Run(1.0);
        Check(casts >= 2, $"Огненный шар применен {casts} раз");
        Check(castsDuringFlight >= 1, $"ни один каст не пришелся на полет прошлого шара (проверка ничего не проверила): кастов {casts}, применение {Room.Hero.LastAttack?.UseSec:0.###} с, расстояние {Driver.HeroDistance():0.#} м");
        Check(driver.ProjectileLandings.Count >= 2, $"показаны попадания {driver.ProjectileLandings.Count} шаров");
        Check(driver.ProjectileLandings.All(x => x), $"попадание показано до прилета шара: {string.Join(", ", driver.ProjectileLandings)}");
        GD.Print($"SELFTEST NOTE снаряды: {casts} каста подряд с 8,5 м, из них во время полета прошлого шара {castsDuringFlight}; " +
                 $"попаданий после прилета {driver.ProjectileLandings.Count(x => x)} из {driver.ProjectileLandings.Count}");
    }

    /// <summary>A second of ignite ticks starts no reaction; with hits mixed in the dummy reacts at most three times a second.</summary>
    private async Task CombatTicks()
    {
        await LoadForCombat(Pyromancer);
        PlaceNearDummy(3f);
        var dummy = Room.Dummy;
        var reactions = dummy.ReactionCount;
        var flashes = dummy.FlashCount;
        var (numbers, amount) = (Driver.DotNumbers, Driver.DotAmountShown);
        var next = 0.0;
        var injected = 0;
        // About twenty ignite ticks in one second through the feed's path: a few summed small numbers and weak flashes, no reaction.
        await Run(1.0, t =>
        {
            if (t < next - 1e-9) return;
            next = t + 0.05;
            injected++;
            Driver.Show(new VisualEvent(VisualKind.Hit, Training.State!.Time, SlotOf("fireball"), "fireball", 0)
                { DamageType = DamageType.Fire, Amount = 40, IsDot = true });
        });
        Check(dummy.ReactionCount == reactions, $"тики запустили реакцию {dummy.ReactionCount - reactions} раз");
        var shown = Driver.DotNumbers - numbers;
        Check(injected >= 15 && shown is >= 2 and <= 4 && dummy.FlashCount - flashes == shown,
            $"{injected} тиков дали {shown} чисел и {dummy.FlashCount - flashes} вспышек (ждали по одному в {CombatDriver.DotShowSec} с)");
        await Run(CombatDriver.DotShowSec + 0.05);
        Check(Math.Abs(Driver.DotAmountShown - amount - injected * 40) < 1e-6, $"сумма чисел тиков {Driver.DotAmountShown - amount}, а тиков {injected * 40}");
        // A second of fireballs pressed every 0.1 s plus ticks: at most three reactions.
        var fireball = SlotOf("fireball");
        await Run(1.5); // whatever was pending lands before the window
        reactions = dummy.ReactionCount;
        next = 0;
        var nextTick = 0.0;
        await Run(1.0, t =>
        {
            if (t >= next) { next = t + 0.1; Driver.PressSlot(fireball); }
            if (t >= nextTick)
            {
                nextTick = t + 0.05;
                Driver.Show(new VisualEvent(VisualKind.Hit, Training.State!.Time, fireball, "fireball", 0) { DamageType = DamageType.Fire, Amount = 40, IsDot = true });
            }
        });
        var inWindow = dummy.ReactionCount - reactions;
        Check(inWindow <= 3, $"за 1 с реакций {inWindow}");
        GD.Print($"SELFTEST NOTE тики: {injected} тиков → {shown} чисел без реакции, за секунду с ударами реакций {inWindow}");
    }

    /// <summary>On «Рой» the ring stands under the figure of the last hit the core reported.</summary>
    private async Task CombatSwarmRing()
    {
        await LoadForCombat(WarriorMelee);
        Driver.SetDummy("swarm");
        var dummy = Room.Dummy;
        Check(dummy.FigureCount == 5 && dummy.PresetId == "swarm", $"фигур «Роя» {dummy.FigureCount}");
        Check(Training.State!.TargetLife.Count == 5, $"целей в бою {Training.State.TargetLife.Count}");
        PlaceNearDummy();
        var targets = new HashSet<int>();
        VisualEvent? before = null;
        await Run(2.0, t =>
        {
            if (Driver.LastShownHit is { } h && !ReferenceEquals(h, before)) { before = h; if (h.Target is { } tt) targets.Add(tt); }
            if (t % 0.4 < FrameSec) Driver.PressSlot(SlotOf("cleave"));
        });
        var last = Driver.LastShownHit;
        Check(last?.Target != null, "ни одного попадания по «Рою»");
        Check(dummy.RingTarget == dummy.FigureOf(last!.Target!.Value), $"кольцо под фигурой {dummy.RingTarget}, последний удар по цели {last.Target}");
        Check(dummy.Ring.Visible && dummy.Ring.Position.IsEqualApprox(dummy.Figures[dummy.RingTarget!.Value].Root.Position + new Vector3(0, 0.03f, 0)), "кольцо не под фигурой");
        // The ring follows the core's target, whichever it is.
        // A skill id in no use and a slot, so the hit cannot join a pending use: it is shown at once.
        Driver.Show(new VisualEvent(VisualKind.Hit, Training.State.Time, 1, "selftest_marker", 3) { DamageType = DamageType.Physical, Amount = 10 });
        Check(dummy.RingTarget == 3, "кольцо не перешло под цель 3");
        GD.Print($"SELFTEST NOTE «Рой»: попадания по целям {string.Join(", ", targets.OrderBy(x => x))}");
        Driver.SetDummy(TrainingRoom.DefaultDummyId);
    }

    /// <summary>The dummy's own light is on only when the hero is farther than ~8 m; never with a shadow.</summary>
    private Task CombatDummyLight()
    {
        var room = Room;
        PlaceNearDummy();
        Check(!room.DummyLight.Visible, "свет манекена горит, когда герой рядом");
        PlaceNearDummy(9f);
        Check(room.DummyLight.Visible && !room.DummyLight.ShadowEnabled, "свет манекена не включился вдали или с тенью");
        room.PlaceHero(room.HeroSpawn);
        return Task.CompletedTask;
    }

    /// <summary>Every preset: its figures and model, runes only on «Стихийный», the boss ×1.4, the hero outlined, no dummy outlined or glowing.</summary>
    private async Task CombatPresets()
    {
        var room = Room;
        var notes = new List<string>();
        foreach (var id in _main.Session!.Data.Dummies.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            Driver.SetDummy(id);
            await NextFrame();
            var dummy = room.Dummy;
            Check(dummy.PresetId == id && Training.DummyId == id, $"пресет {id} не выбран");
            var (character, count) = id switch
            {
                "armored" => ("orc", 1),
                "swarm" => ("zombie", 5),
                "boss" or "attacking_boss" => ("boss", 1),
                _ => ("zombie", 1),
            };
            Check(dummy.CharacterId == character && dummy.FigureCount == count, $"{id}: {dummy.FigureCount} × {dummy.CharacterId}");
            Check((dummy.Runes != null) == (id == "elemental"), $"{id}: руны {(dummy.Runes != null ? "есть" : "нет")}");
            if (character == "boss") Check(Mathf.IsEqualApprox(dummy.MainModel.TargetHeight, 3.15f) && dummy.CollisionRadius(0) > 0.6f, "босс не орк ×1,4");
            foreach (var f in dummy.Figures)
            {
                Check(f.Model.Meshes.All(m => m.MaterialOverlay == null), $"{id}: у манекена контур");
                if (!f.Model.IsStub)
                    foreach (var m in f.Model.Meshes)
                        for (int s = 0; s < (m.Mesh?.GetSurfaceCount() ?? 0); s++)
                            if (m.GetActiveMaterial(s) is StandardMaterial3D mat) Check(!mat.EmissionEnabled, $"{id}: запеченное свечение врага включено");
            }
            notes.Add($"{id} {dummy.FigureCount}×{dummy.CharacterId}");
        }
        Check(room.HeroModel.Meshes.All(m => m.MaterialOverlay == null), "у героя остался контур тела");
        var halo = room.Hero.GroundHalo;
        var capsule = room.Hero.GetChildren().OfType<CollisionShape3D>().Single().Shape as CapsuleShape3D;
        Check(capsule != null, "коллизия героя не капсула");
        var vertices = halo.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        Check(vertices.Length == 96 * 6 && vertices.All(v => Mathf.IsZeroApprox(v.Y)), "ореол не плоское кольцо");
        Check(capsule != null && vertices.All(v => v.Length() <= capsule.Radius + 0.00001f)
            && Mathf.IsEqualApprox(vertices.Max(v => v.Length()), capsule.Radius), "ореол не по реальной коллизии героя");
        Check(halo.GetParent() == room.Hero && halo.Position == new Vector3(0, HeroView.GroundHaloElevation, 0), "ореол не следует за ногами героя");
        Check(halo.MaterialOverride is StandardMaterial3D { EmissionEnabled: false, NoDepthTest: false }
            && halo.CastShadow == GeometryInstance3D.ShadowCastingSetting.Off, "ореол светится или виден сквозь сцену");
        var rune = DummyView.RuneColor;
        Check(!(rune.B > rune.G && rune.R > rune.G), "руны фиолетовые (цвет тьмы)");
        Driver.SetDummy(TrainingRoom.DefaultDummyId);
        GD.Print("SELFTEST NOTE пресеты: " + string.Join(", ", notes));
    }

    /// <summary>Keys reach the fight as events (not the global Input) and stop while a text field has focus; the hero walks to a point.</summary>
    private async Task CombatInputAndMove()
    {
        await LoadForCombat(WarriorMelee);
        var room = Room;
        PlaceNearDummy();
        var driver = Driver;
        var lines = Training.Log.Count;
        var key = new InputEventKey { PhysicalKeycode = Key.Key1 + (SlotOf("battle_cry") - 1), Pressed = true };
        Check(driver.HandleInput(key), "клавиша слота не дошла до боя");
        driver.HandleInput(new InputEventKey { PhysicalKeycode = key.PhysicalKeycode, Pressed = false });
        await Run(0.5);
        Check(Training.Log.Count > lines, "нажатие клавиши не дало строки в журнале");

        var field = new LineEdit { Name = "SelfTestField" };
        _main.AddChild(field);
        try
        {
            field.GrabFocus();
            await NextFrame();
            Check(driver.KeysBlocked(), "фокус в поле ввода не блокирует бой");
            Check(!driver.HandleInput(new InputEventKey { PhysicalKeycode = Key.Key2, Pressed = true }), "цифра ушла в бой при фокусе в поле");
        }
        finally
        {
            field.ReleaseFocus();
            field.QueueFree();
        }

        // An open window does not block the fight (spec §4 «Ввод»): the held channel is let go by its key with the window open.
        var ww = SlotOf("whirlwind");
        Check(driver.HandleInput(new InputEventKey { PhysicalKeycode = Key.Key1 + (ww - 1), Pressed = true }), "клавиша Вихря не дошла до боя");
        await Run(0.2);
        Check(Training.State!.HeldSlot == ww, "Вихрь не зажат клавишей");
        _main.OpenWindow(WindowIds.Character);
        await NextFrame();
        Check(room.IsShown && !driver.KeysBlocked(), "открытое окно заблокировало бой");
        Check(driver.HandleInput(new InputEventKey { PhysicalKeycode = Key.Key1 + (ww - 1), Pressed = false }), "отпускание клавиши при открытом окне не дошло до боя");
        await Run(0.1);
        Check(Training.State!.HeldSlot == null, "отпускание клавиши при открытом окне не отпустило канал");
        _main.CloseAllWindows();
        await NextFrame();

        room.PlaceHero(room.HeroSpawn);
        var start = room.Hero.Position;
        room.Hero.MoveTo(start + new Vector3(-2f, 0, 0), run: false);
        for (int i = 0; i < 30; i++) await _main.ToSignal(_main.GetTree(), SceneTree.SignalName.PhysicsFrame);
        await Run(FrameSec);
        var moved = room.Hero.Position.DistanceTo(start);
        Check(moved > 0.3f, $"герой прошел {moved:0.##} м за 0,5 с");
        if (room.HeroModel.Player != null && room.Hero.MoveTarget != null)
            Check(room.HeroModel.CurrentClip == HeroView.WalkClip, $"при ходьбе клип {room.HeroModel.CurrentClip}");
        room.PlaceHero(room.HeroSpawn);
    }

    private async Task CombatRestore()
    {
        var room = Room;
        if (room.Dummy.PresetId != TrainingRoom.DefaultDummyId) Driver.SetDummy(TrainingRoom.DefaultDummyId);
        room.Dummy.Position = room.DummySpot;
        room.PlaceHero(room.HeroSpawn);
        Driver.SetProcess(true);
        await Settle();
    }
}

public sealed partial class SelfTest
{
    /// <summary>
    /// Combat screenshots for the art review (window run only): a melee hit on the training dummy, a crit number, Fireball in
    /// flight and its flash, «Рой» with the ring, «Стихийный» with runes, the boss. Frames are stepped by hand, so each shot
    /// catches the moment it names.
    /// </summary>
    private async Task CombatScreenshots()
    {
        var room = Room;
        var driver = Driver;
        var right = new Vector3(1, 0, -1).Normalized(); // the screen's horizontal on the floor
        try
        {
            await LoadForCombat(WarriorMelee);
            room.PlaceHero(room.Dummy.TargetPosition(0) + right * 1.25f + TowardsCamera * 0.35f);
            var cleave = SlotOf("cleave");
            driver.PressSlot(cleave);
            await Run(2.0, until: () => driver.LastMelee is { FlashAt: not null });
            await Run(0.09); // past the 40 ms hit-stop, sparks flying
            await Shot("task-9-melee-training");

            VisualEvent? crit = null;
            await Run(12.0, t =>
            {
                if (Training.State is { Busy: false }) driver.PressSlot(cleave);
            }, until: () => (crit = driver.LastShownHit) is { IsCrit: true });
            Check(crit is { IsCrit: true }, "крит за 12 с не выпал");
            await Run(0.12);
            await Shot("task-9-crit");

            await LoadForCombat(Pyromancer);
            room.PlaceHero(room.Dummy.TargetPosition(0) + TowardsCamera * 5.5f + right * 1.5f);
            var fireball = SlotOf("fireball");
            Check(driver.PressSlot(fireball).Accepted, "Огненный шар не принят");
            await Run(3.0, until: () => room.Effects.Active.OfType<ProjectileView>().Any(p => p.Age >= p.FlightSec * 0.45));
            await Shot("task-9-fireball-projectile");
            await Run(1.0, until: () => driver.LastShownHit is { IsDot: false, DamageType: DamageType.Fire });
            await Run(0.05);
            await Shot("task-9-fireball-flash");

            await LoadForCombat(WarriorMelee);
            driver.SetDummy("swarm");
            room.PlaceHero(room.Dummy.TargetPosition(0) + TowardsCamera * 3.1f + right * 0.9f);
            await Run(3.0, _ => { if (Training.State is { Busy: false }) driver.PressSlot(cleave); });
            await Run(2.0, until: () => Training.State is { Busy: false });
            var previous = driver.LastMelee;
            driver.PressSlot(cleave);
            await Run(2.0, until: () => driver.LastMelee is { FlashAt: not null } m && !ReferenceEquals(m, previous) && driver.VisualTime > m.FlashAt + 0.1);
            await Shot("task-9-swarm");

            await LoadForCombat(Pyromancer);
            driver.SetDummy("elemental");
            room.PlaceHero(room.Dummy.TargetPosition(0) + TowardsCamera * 3.5f + right * 1.2f);
            var spark = SlotOf("spark");
            await Run(1.5, _ => { if (Training.State is { Busy: false }) driver.PressSlot(spark); });
            await Run(2.0, until: () => Training.State is { Busy: false });
            driver.PressSlot(spark);
            await Run(1.5, until: () => room.Effects.Active.Any(e => e.Name.ToString().Contains("Arc")));
            await Run(0.03);
            await Shot("task-9-elemental");

            // Physical: the thrown axe and warm sparks «from metal» on impact.
            await LoadForCombat("warrior_berserker_whirlwind.json");
            room.PlaceHero(room.Dummy.TargetPosition(0) + TowardsCamera * 4f + right * 1.5f);
            Check(driver.PressSlot(SlotOf("throwing_axe")).Accepted, "Метательный топор не принят");
            await Run(2.0, until: () => driver.LastShownHit is { IsDot: false, DamageType: DamageType.Physical });
            await Run(0.06);
            await Shot("task-9-axe-sparks");

            await LoadForCombat(WarriorMelee);
            driver.SetDummy("boss");
            room.PlaceHero(room.Dummy.TargetPosition(0) + right * 1.9f + TowardsCamera * 0.6f);
            driver.PressSlot(cleave);
            await Run(2.0, until: () => driver.LastMelee is { FlashAt: not null });
            await Run(0.09);
            await Shot("task-9-boss");
        }
        finally
        {
            await CombatRestore();
        }
    }
}
