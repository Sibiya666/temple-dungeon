using Dialoid.Core.Combat;
using Dialoid.Core.Simulation;
using Dialoid.Game.Input;
using Dialoid.Game.Room;
using Dialoid.Game.Ui;
using Dialoid.Sandbox;
using Godot;

namespace Dialoid.Game;

/// <summary>
/// The final acceptance shots and the load run (plan 4 task 12, spec §10.7 and §10.8).
/// <para>Shots: in a real window the frame is 2560×1440 (<see cref="FinalFrame"/>). When the screen is smaller than that the
/// window cannot be, so the root viewport renders 2560×1440 and is scaled into the window (content scale «viewport»): the GPU
/// draws every one of the 2560×1440 pixels, the PNG is the frame itself. Combat shots have the log and the meter open.</para>
/// <para>Load: <c>-- --selftest --perf [--quality low]</c> holds «Рой» with the most frequent skill of the six references and
/// every effect (numbers, ailments, projectile lights, torch shadows, the log and the meter open) for <see cref="PerfSec"/> s,
/// v-sync off, and prints «SELFTEST PERF …»: the frame size, average FPS and the worst 1 % (the mean of the slowest 1 % of frames).
/// High is measured at 2560×1440, Low (the minimum spec's settings) at 1920×1080.</para>
/// </summary>
public sealed partial class SelfTest
{
    /// <summary>User argument (after "--selftest"): run the load measurement only, in a real window.</summary>
    public const string PerfArg = "--perf";

    /// <summary>Subfolder of the screenshot folder for the task review shots (not committed).</summary>
    public const string ReviewSubdir = "review";

    /// <summary>Frame of the acceptance shots and of the High load run (spec §4 «Бюджет кадра»: 1440p).</summary>
    public static readonly Vector2I FinalFrame = new(2560, 1440);

    /// <summary>Frame of the Low load run (the minimum spec: 1080p on low settings).</summary>
    public static readonly Vector2I LowFrame = new(1920, 1080);

    public const double PerfWarmupSec = 3;
    public const double PerfSec = 20;

    private readonly bool _perf;

    // ---------- frame size ----------

    /// <summary>
    /// Makes the frame <paramref name="size"/>: the window itself when the screen allows it, else the root viewport renders at that
    /// size and is scaled into the window. Returns how (for the notes).
    /// </summary>
    private async Task<string> UseFrame(Vector2I size)
    {
        var root = _main.GetTree().Root;
        if (DisplayServer.GetName() == "headless")
        {
            root.Size = size;
            await Settle();
            return $"headless {size.X}×{size.Y}";
        }
        root.ContentScaleMode = Window.ContentScaleModeEnum.Disabled;
        DisplayServer.WindowSetSize(size);
        await Settle();
        await Settle();
        var window = DisplayServer.WindowGetSize();
        string how;
        if (window == size) how = $"окно {size.X}×{size.Y}";
        else
        {
            root.ContentScaleMode = Window.ContentScaleModeEnum.Viewport;
            root.ContentScaleAspect = Window.ContentScaleAspectEnum.Keep;
            root.ContentScaleSize = size;
            await Settle();
            await Settle();
            var screen = DisplayServer.ScreenGetSize();
            how = $"экран {screen.X}×{screen.Y}, окно {window.X}×{window.Y}: кадр рендерится в {size.X}×{size.Y} и масштабируется в окно";
        }
        var visible = _main.GetViewport().GetVisibleRect().Size;
        Check(visible == (Vector2)size && _main.RoomViewport!.Size == size, $"кадр {visible}, комната {_main.RoomViewport!.Size}, а нужно {size} ({how})");
        GD.Print($"SELFTEST NOTE кадр {size.X}×{size.Y}: {how}");
        return how;
    }

    /// <summary>Back to the plain window of the screenshot run (<see cref="ScreenshotWindow"/>).</summary>
    private async Task RestoreFrame()
    {
        var root = _main.GetTree().Root;
        root.ContentScaleMode = Window.ContentScaleModeEnum.Disabled;
        root.ContentScaleSize = Vector2I.Zero;
        DisplayServer.WindowSetSize(ScreenshotWindow);
        await Settle();
        await Settle();
        UseSelfTestWindowConfig(); // window places from the default edges of this size
    }

    // ---------- acceptance shots ----------

    /// <summary>A combat shot's setup: the reference, the preset, the log and the meter open (no focus), default zoom, mouse mode.</summary>
    private async Task CombatShotSetup(string file, string dummy = TrainingRoom.DefaultDummyId)
    {
        Device.Set(InputKind.KeyboardMouse);
        await LoadForCombat(file);
        if (dummy != TrainingRoom.DefaultDummyId) Driver.SetDummy(dummy);
        Meter.SetFloatingNumbers(true);
        UseSelfTestWindowConfig();
        _main.OpenWindow(WindowIds.Log);
        _main.OpenWindow(WindowIds.Meter);
        Room.CameraRig.ResetZoom();
    }

    /// <summary>Presses <paramref name="slot"/> until a new melee use shows its flash, then lets the sparks fly 0.09 s.</summary>
    private async Task MeleeMoment(int slot)
    {
        var driver = Driver;
        await Run(2.0, until: () => Training.State is { Busy: false });
        var previous = driver.LastMelee;
        await Run(3.0, _ => { if (Training.State is { Busy: false }) driver.PressSlot(slot); },
            until: () => driver.LastMelee is { FlashAt: not null } m && !ReferenceEquals(m, previous));
        Check(driver.LastMelee is { FlashAt: not null } now && !ReferenceEquals(now, previous), "вспышки ближнего удара нет");
        await Run(0.09);
    }

    /// <summary>
    /// The §10.7 set at 2560×1440, combat shots with the log and the meter open: zoom default, closest (on «Босс», not cut at the
    /// top) and widest; the fire skill; «Рой»; «Рой» with every effect in crossed torch shadows; each preset; one flash per damage
    /// type on «Бронированный»; the charts; the passives over the room; the gamepad mode.
    /// </summary>
    private async Task AcceptanceScreenshots()
    {
        var room = Room;
        var driver = Driver;
        var right = new Vector3(1, 0, -1).Normalized(); // the screen's horizontal on the floor
        var notes = new List<string>();
        try
        {
            // Zoom: default, closest on the boss (its head stays under the top bar), widest.
            await CombatShotSetup(WarriorMelee);
            room.PlaceHero(room.Dummy.TargetPosition(0) + right * 1.25f + TowardsCamera * 0.35f);
            await Fight(3.0, close: false);
            await MeleeMoment(SlotOf("cleave"));
            await Shot("10-combat-zoom-default", full: true);

            await CombatShotSetup(WarriorMelee, "boss");
            room.PlaceHero(room.Dummy.TargetPosition(0) + right * 1.9f + TowardsCamera * 0.6f);
            room.CameraRig.Zoom = room.CameraRig.MinZoom;
            room.CameraRig.Follow();
            await Fight(2.0, close: false);
            await MeleeMoment(SlotOf("cleave"));
            var head = _main.RoomToScreen(room.Dummy.TargetPosition(0) + new Vector3(0, room.Dummy.MainModel.TargetHeight, 0));
            Check(head is { } h && h.Y > TopBar.Height + 4, $"на ближнем зуме голова босса в {head}, под верхней панелью");
            notes.Add($"ближний зум {room.CameraRig.Zoom:0.##} м: голова босса на y={head!.Value.Y:0}");
            await Shot("11-combat-zoom-closest-boss", full: true);

            await CombatShotSetup(WarriorMelee);
            room.PlaceHero(room.Dummy.TargetPosition(0) + right * 1.25f + TowardsCamera * 0.35f);
            room.CameraRig.Zoom = room.CameraRig.MaxZoom;
            room.CameraRig.Follow();
            await Fight(2.0, close: false);
            await MeleeMoment(SlotOf("cleave"));
            notes.Add($"дальний зум {room.CameraRig.Zoom:0.##} м");
            await Shot("12-combat-zoom-widest", full: true);
            room.CameraRig.ResetZoom();

            // The fire skill: a fireball in flight towards the burning dummy.
            await CombatShotSetup(Pyromancer);
            room.PlaceHero(room.Dummy.TargetPosition(0) + TowardsCamera * 5.5f + right * 1.5f);
            await Fight(3.0, close: false);
            await Run(3.0, until: () => Training.State is { Busy: false });
            Check(driver.PressSlot(SlotOf("fireball")).Accepted, "Огненный шар не принят");
            await Run(3.0, until: () => room.Effects.Active.OfType<ProjectileView>().Any(p => p.Age >= p.FlightSec * 0.55));
            await Shot("13-fire-skill", full: true);

            // «Рой».
            await CombatShotSetup(WarriorMelee, "swarm");
            room.PlaceHero(room.Dummy.TargetPosition(0) + TowardsCamera * 3.1f + right * 0.9f);
            await Fight(3.0, close: false);
            await MeleeMoment(SlotOf("cleave"));
            await Shot("14-swarm", full: true);

            // «Рой» with every effect (ailments, projectiles with lights, numbers) where the torch shadows cross on the hero and the pack.
            await CombatShotSetup(Pyromancer, "swarm");
            room.PlaceHero(room.Dummy.TargetPosition(0) + TowardsCamera * 4.5f - right * 0.8f);
            await Fight(4.0, close: false);
            await Run(3.0, until: () => Training.State is { Busy: false });
            driver.PressSlot(SlotOf("fireball"));
            await Run(3.0, until: () => room.Effects.Active.OfType<ProjectileView>().Any(p => p.Age >= p.FlightSec * 0.5));
            var torches = room.ShadowLights.Count(l => l != room.HeroLight);
            Check(room.Quality == GraphicsQuality.Low || torches >= 2, $"теней от факелов {torches}: перекрестных теней нет");
            notes.Add($"«Рой» со всеми эффектами: теневых факелов {torches}, снарядов {room.Effects.Projectiles}");
            await Shot("15-swarm-all-effects-torch-shadows", full: true);

            // Each preset of the dummy (the elemental one under Spark's lightning, the carry-over check of its fill light).
            var presets = _main.Session!.Data.Dummies.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
            var n = 20;
            foreach (var id in presets)
            {
                if (id == "elemental")
                {
                    await CombatShotSetup(Pyromancer, id);
                    room.PlaceHero(room.Dummy.TargetPosition(0) + TowardsCamera * 3.5f + right * 1.2f);
                    var spark = SlotOf("spark");
                    await Fight(2.0, close: false);
                    await Run(2.0, until: () => Training.State is { Busy: false });
                    driver.PressSlot(spark);
                    await Run(1.5, until: () => room.Effects.Active.Any(e => e.Name.ToString().Contains("Arc")));
                    await Run(0.03);
                }
                else
                {
                    await CombatShotSetup(WarriorMelee, id);
                    var big = room.Dummy.CollisionRadius(0) > 0.6f;
                    room.PlaceHero(room.Dummy.TargetPosition(0) + right * (big ? 1.9f : 1.25f) + TowardsCamera * (id == "swarm" ? 3.1f : 0.5f));
                    await Fight(2.0, close: false);
                    await MeleeMoment(SlotOf("cleave"));
                }
                await Shot($"{n++}-preset-{id.Replace('_', '-')}", full: true);
            }

            // One flash per damage type on «Бронированный»: a hit of that type shown through the driver's own path (the visuals of
            // a core hit; no reference has a poison skill, so all five are shown the same way). The number is a stand-in.
            var types = new[] { (DamageType.Fire, "fire"), (DamageType.Cold, "cold"), (DamageType.Lightning, "lightning"), (DamageType.Physical, "physical"), (DamageType.Poison, "poison") };
            n = 30;
            foreach (var (type, name) in types)
            {
                await CombatShotSetup(WarriorMelee, "armored");
                room.PlaceHero(room.Dummy.TargetPosition(0) + right * 1.9f + TowardsCamera * 0.9f);
                await Run(0.5);
                driver.Show(new VisualEvent(VisualKind.Hit, Training.State!.Time, 1, "selftest_marker", 0) { DamageType = type, Amount = 1234 });
                await Run(0.06);
                await Shot($"{n++}-flash-{name}-armored", full: true);
            }

            // The charts window after a fight; the passives over the room mid-fight.
            await CombatShotSetup(WarriorMelee);
            PlaceNearDummy();
            await Fight(4.0, close: true);
            _main.CloseAllWindows();
            _main.OpenWindow(WindowIds.Charts);
            await Settle();
            await Shot("40-charts", full: true);

            await CombatShotSetup(WarriorMelee);
            room.PlaceHero(room.Dummy.TargetPosition(0) + right * 1.25f + TowardsCamera * 0.35f);
            await Fight(2.0, close: false);
            _main.CloseAllWindows();
            _main.OpenWindow(WindowIds.Passives);
            await Settle();
            _main.Passives!.View.FitToView();
            await Shot("41-passives-over-room", full: true);

            // The gamepad mode: glyphs on the slots, the focus frame in the passive tree, the top bar after Start.
            await CombatShotSetup(WarriorMelee);
            await PadMode();
            room.PlaceHero(room.Dummy.TargetPosition(0) + right * 1.3f + TowardsCamera * 0.4f);
            await Fight(2.0, close: false);
            await Shot("50-gamepad-combat", full: true);
            PadTap(JoyButton.DpadLeft);
            await Settle();
            var view = _main.Passives!.View;
            view.FitToView();
            view.GrabFocus();
            if (view.Nodes.FirstOrDefault(x => x.Caption != null && view.StateOf(x.Id) == TreeNodeState.Available) is { } node) view.PadSelect(node.Id);
            view.PadFocusZoom();
            await Shot("51-gamepad-passives-focus", full: true);
            await PadMode();
            PadTap(JoyButton.Start);
            await Shot("52-gamepad-top-bar", full: true);
            PadTap(JoyButton.B);
            GD.Print("SELFTEST NOTE итоговые снимки: " + string.Join("; ", notes));
        }
        finally
        {
            Host.FocusCombat();
            _main.CloseAllWindows();
            Device.Set(InputKind.KeyboardMouse);
            room.CameraRig.ResetZoom();
            await CombatRestore();
        }
    }

    // ---------- load ----------

    /// <summary>
    /// Spec §10.8 on «Рой» with every effect, v-sync off, two scenarios over the six references: the most frequent skill (the
    /// shortest use time without a cooldown) and the most frequent projectile skill drawn as a ball with its own light (not lightning, a ribbon). Each:
    /// <see cref="PerfSec"/> s of real frames after <see cref="PerfWarmupSec"/> s of warm-up, one «SELFTEST PERF …» line.
    /// </summary>
    private async Task Perf()
    {
        Check(DisplayServer.GetName() != "headless", "замер нагрузки нужен в окне, а не в --headless");
        var size = Room.Quality == GraphicsQuality.Low ? LowFrame : FinalFrame;
        var frame = await UseFrame(size);
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = 0;

        var mains = _main.Builds!.References.Where(r => r.Group == ReferenceBuilds.MainGroup).ToList();
        (ReferenceBuildInfo Info, SlotInfo Slot)? frequent = null, projectile = null;
        foreach (var info in mains)
        {
            Check(_main.Builds.Load(info).Ok, $"эталон {info.Title} не загрузился");
            await Settle();
            foreach (var s in Training.Slots)
            {
                if (s is not { CooldownSec: 0 } || s.UseTimeSec <= 0) continue;
                if (frequent == null || s.UseTimeSec < frequent.Value.Slot.UseTimeSec) frequent = (info, s);
                // A ball with its own light: a projectile that is not lightning (lightning is a ribbon, spec §5).
                if (Training.CompiledTags(s.Slot) is { } tags && tags.Contains("projectile") && !tags.Contains("lightning")
                    && (projectile == null || s.UseTimeSec < projectile.Value.Slot.UseTimeSec)) projectile = (info, s);
            }
        }
        Check(frequent != null && projectile != null, "нет скилла без перезарядки или снаряда");
        await PerfScenario("самый частый скилл", frequent!.Value.Info, frequent.Value.Slot, size, frame);
        await PerfScenario("самый частый снаряд со светом", projectile!.Value.Info, projectile.Value.Slot, size, frame);
    }

    private async Task PerfScenario(string scenario, ReferenceBuildInfo pick, SlotInfo slot, Vector2I size, string frame)
    {
        var room = Room;
        var driver = Driver;
        Device.Set(InputKind.KeyboardMouse);
        await LoadForCombat(Path.GetFileName(pick.Path));
        Driver.SetDummy("swarm");
        Meter.SetFloatingNumbers(true);
        UseSelfTestWindowConfig();
        _main.OpenWindow(WindowIds.Log);
        _main.OpenWindow(WindowIds.Meter);
        room.CameraRig.ResetZoom();
        PlaceNearDummy(slot.Tags.Contains("melee") ? 0.6f : 3f);
        var others = Training.Slots.Where(s => s != null && s.Slot != slot.Slot && !s.IsChannel).Select(s => s!.Slot).ToList();
        var turn = 0;
        var refusals = new Dictionary<string, int>();
        bool Try(int s)
        {
            var r = driver.PressSlot(s);
            if (!r.Accepted) refusals[r.Refusal.ToString()] = refusals.GetValueOrDefault(r.Refusal.ToString()) + 1;
            return r.Accepted;
        }
        void Press()
        {
            if (Training.State is not { } st) return;
            var low = st.ResourceShortSlots.Contains(slot.Slot);
            if (slot.IsChannel && st.HeldSlot == slot.Slot)
            {
                if (!low) return; // the channel ticks on
                driver.ReleaseSlot(slot.Slot);
            }
            if (st.Busy) return;
            // Out of resource: the other slots in turn (generators, buffs) keep every effect going.
            if ((low || !Try(slot.Slot)) && others.Count > 0) Try(others[turn++ % others.Count]);
        }

        var dts = new List<double>();
        int uses = 0, numbers = 0, maxProjectiles = 0, maxShadows = 0, maxNumbers = 0;
        double damage = 0, fightSec = 0;
        var blocked = false;
        var gcs = (0, 0);
        driver.SetProcess(true);
        try
        {
            var warm = Time.GetTicksUsec() + (ulong)(PerfWarmupSec * 1e6);
            while (Time.GetTicksUsec() < warm)
            {
                Press();
                await NextFrame();
            }
            var (attacks, shown, damage0, time0) = (room.Hero.AttackCount, room.Numbers.ShownTotal, Training.Segments.Sum(s => s.TotalDamage), Training.State?.Time ?? 0);
            var gc0 = (GC.CollectionCount(0), GC.CollectionCount(2));
            var start = Time.GetTicksUsec();
            var last = start;
            var end = start + (ulong)(PerfSec * 1e6);
            while (last < end)
            {
                Press();
                await NextFrame();
                var now = Time.GetTicksUsec();
                dts.Add((now - last) / 1e6);
                last = now;
                maxProjectiles = Math.Max(maxProjectiles, room.Effects.Projectiles);
                maxShadows = Math.Max(maxShadows, room.ShadowLights.Count());
                maxNumbers = Math.Max(maxNumbers, room.Numbers.Count);
            }
            uses = room.Hero.AttackCount - attacks;
            numbers = room.Numbers.ShownTotal - shown;
            damage = Training.Segments.Sum(s => s.TotalDamage) - damage0;
            fightSec = (Training.State?.Time ?? 0) - time0;
            blocked = driver.KeysBlocked();
            gcs = (GC.CollectionCount(0) - gc0.Item1, GC.CollectionCount(2) - gc0.Item2);
        }
        finally
        {
            driver.ReleaseAll();
            driver.SetProcess(false);
        }
        Check(dts.Count > 10, $"кадров {dts.Count}");
        var total = dts.Sum();
        var avg = dts.Count / total;
        var sorted = dts.OrderByDescending(x => x).ToList();
        var worstCount = Math.Max(1, dts.Count / 100);
        var worst1 = worstCount / sorted.Take(worstCount).Sum();
        var p99 = sorted[Math.Min(sorted.Count - 1, dts.Count / 100)];
        GD.Print($"SELFTEST PERF «{scenario}» {size.X}x{size.Y} quality={room.Quality.ToString().ToLowerInvariant()} avg={avg:0.0} fps worst1%={worst1:0.0} fps " +
                 $"(p99 кадр {p99 * 1000:0.00} мс, худший {sorted[0] * 1000:0.00} мс, кадров {dts.Count} за {total:0.0} с; дольше 50 мс: {dts.Count(x => x > 0.05)}, сборок мусора gen0 {gcs.Item1}, gen2 {gcs.Item2})");
        GD.Print($"SELFTEST NOTE нагрузка: {frame}; эталон «{pick.Title}», скилл «{slot.Name}» ({slot.UseTimeSec:0.###} с на применение{(slot.IsChannel ? ", канал" : "")}), «Рой»; " +
                 $"применений {uses / total:0.0}/с, чисел {numbers} (на экране до {maxNumbers}), снарядов до {maxProjectiles}, теневых источников до {maxShadows}, " +
                 $"урон {damage:0} за {fightSec:0.0} с боя, отказы: {(refusals.Count == 0 ? "нет" : string.Join(", ", refusals.Select(kv => $"{kv.Key} ×{kv.Value}")))}{(blocked ? ", КЛАВИШИ БОЯ ЗАБЛОКИРОВАНЫ" : "")}; " +
                 "журнал и измеритель открыты; v-sync выключен");
        Check(avg > 0 && worst1 > 0, "нет кадров");
        Check(damage > 0 && numbers > 0, $"боя не было: урон {damage}, чисел {numbers} — замер без нагрузки не годится");
        if (Training.CompiledTags(slot.Slot) is { } t && t.Contains("projectile") && !t.Contains("lightning"))
            Check(maxProjectiles > 0, $"«{slot.Name}»: снарядов в кадре не было");
    }
}
