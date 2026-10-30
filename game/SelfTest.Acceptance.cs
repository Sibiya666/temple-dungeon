using Dialoid.Core.Combat;
using Dialoid.Core.Simulation;
using Dialoid.Game.Room;
using Dialoid.Game.Ui;
using Dialoid.Sandbox;
using Godot;

namespace Dialoid.Game;

/// <summary>
/// Acceptance of the training room (plan 4 task 12, spec §10.5): every main reference fights «Тренировочный» for 5 s of game time
/// through synthetic key events, the meter equals the log, a skill on cooldown is refused, a passive taken off in the P window
/// gives a new segment with another DPS. The gamepad items of §10.5 are the gamepad.* steps of the same run, the asset-free
/// run is the same self-test with an empty asset folder. Also: floating numbers never cover the target plate and the plate's
/// chevron goes with the plate (task 14 carry-overs).
/// </summary>
public sealed partial class SelfTest
{
    /// <summary>Game seconds every reference fights in the acceptance scenario (spec §10.5: «несколько секунд»).</summary>
    public const double AcceptanceFightSec = 5;

    private readonly List<string> _roomTable = new();

    private IEnumerable<Step> AcceptanceSteps()
    {
        yield return new("acceptance.room-six-references", AcceptanceRoom);
        yield return new("hud.numbers-clear-of-plate", HudNumbersClearOfPlate);
        yield return new("hud.marker-hides-with-plate", HudMarkerHidesWithPlate);
        yield return new("acceptance.restore", async () =>
        {
            Meter.SetSimMode(SimMode.Roll, 1);
            _main.CloseAllWindows();
            await CombatRestore();
        });
    }

    // ---------- helpers ----------

    /// <summary>
    /// Synthetic keys 1–5 (the same <see cref="InputEventKey"/> a keyboard sends, into <see cref="CombatDriver.HandleInput"/>): every
    /// 0.3 s of game time the previous key goes up and the next filled slot's key goes down; then every key up and, with
    /// <paramref name="close"/>, wait until the segment closes. Returns the number of key presses the fight took.
    /// </summary>
    private async Task<int> KeyFight(double seconds, bool close)
    {
        var slots = Training.Slots.Where(s => s != null).Select(s => s!.Slot).ToList();
        Check(slots.Count > 0, "в билде нет скиллов");
        var next = 0.0;
        var turn = 0;
        int? down = null;
        var taken = 0;
        void SendKey(int slot, bool pressed)
        {
            if (Driver.HandleInput(new InputEventKey { PhysicalKeycode = Key.Key1 + (slot - 1), Keycode = Key.Key1 + (slot - 1), Pressed = pressed }) && pressed) taken++;
        }
        await Run(seconds, t =>
        {
            if (t < next) return;
            next = t + 0.3;
            if (down is { } d) SendKey(d, false);
            down = slots[turn++ % slots.Count];
            SendKey(down.Value, true);
        });
        if (down is { } last) SendKey(last, false);
        if (close) await Run(TrainingRoom.SegmentIdleSec + 40, until: () => Training.Current == null);
        if (close) Check(Training.Current == null, $"сегмент не закрылся: последний урон в {Training.Current?.End:0.0} с");
        return taken;
    }

    /// <summary>A fight from zero (mode «среднее», seed 1): the hero next to the training dummy, keys for <see cref="AcceptanceFightSec"/>.</summary>
    private async Task<FightSegment> KeySegment(bool restart)
    {
        if (restart) Meter.SetSimMode(SimMode.Average, 1);
        Check(Training.Mode == SimMode.Average && Training.State is { Time: 0 }, $"бой не с нуля: режим {Training.Mode}, время {Training.State?.Time}");
        Driver.SetProcess(false);
        Driver.ResetVisuals();
        PlaceNearDummy();
        var segments = Training.Segments.Count;
        var taken = await KeyFight(AcceptanceFightSec, close: true);
        Check(taken > 0, "ни одна клавиша не дошла до боя");
        Check(Training.Segments.Count > 0 && (Training.Segments.Count > segments || Training.Segments.Count == TrainingRoom.MaxSegments), "нового сегмента нет");
        return Training.Segments[^1];
    }

    // ---------- steps ----------

    /// <summary>
    /// Spec §10.5 on all six main references in one step: load, the hero next to «Тренировочный», 5 s of synthetic keys; the log
    /// has the segment's lines, the meter shows DPS &gt; 0 and its total equals the segment's and the log's; a skill on cooldown is
    /// refused (log line and the text over the hero); a passive taken off in the P window and the same keys again give a new
    /// segment «после правки билда» with another DPS. Prints the table «SELFTEST ROOM …».
    /// </summary>
    private async Task AcceptanceRoom()
    {
        var mains = _main.Builds!.References.Where(r => r.Group == ReferenceBuilds.MainGroup).ToList();
        Check(mains.Count == 6, $"основных эталонов {mains.Count}, а не 6");
        var meter = Meter;
        var cooldowns = 0;
        foreach (var info in mains)
        {
            await LoadForCombat(Path.GetFileName(info.Path));
            Check(Training.DummyId == TrainingRoom.DefaultDummyId, $"манекен {Training.DummyId}, а не «Тренировочный»");
            var before = await KeySegment(restart: true);
            var where = $"«{info.Title}»";

            // The log is not empty and adds up to the meter.
            var lines = Training.Log.Where(e => e.Segment == before.Index).ToList();
            Check(lines.Count > 0, $"{where}: в журнале нет строк сегмента");
            // The log keeps the last lines: the segment is whole while older lines are still there.
            Check(!before.Trimmed && (Training.Log.Count < TrainingRoom.MaxLogLines || Training.Log.FirstOrDefault(e => e.Segment != null)?.Segment is { } first && first < before.Index),
                $"{where}: строки сегмента в журнале обрезаны ({Training.Log.Count} строк)");
            Check(before.Dps > 0, $"{where}: DPS сегмента {before.Dps}");
            _main.OpenWindow(WindowIds.Meter);
            _main.OpenWindow(WindowIds.Log);
            meter.SelectSegment(null);
            meter.SetMode(MeterMode.DpsBySkill);
            meter.Refresh();
            await Settle();
            Check(meter.SelectedSegment == before, $"{where}: измеритель показывает не последний сегмент");
            var meterDps = meter.ShownRows.Sum(r => r.Value);
            Check(meterDps > 0, $"{where}: DPS на полосах измерителя {meterDps}");
            meter.SetMode(MeterMode.DamageBySkill);
            meter.Refresh();
            var meterTotal = meter.ShownRows.Sum(r => r.Value);
            Check(Near(meterTotal, before.TotalDamage, 1e-6 * Math.Max(1, before.TotalDamage)), $"{where}: сумма полос {meterTotal:R}, урон сегмента {before.TotalDamage:R}");
            var logTotal = lines.Sum(e => e.Amount ?? 0);
            Check(Math.Abs(logTotal - before.TotalDamage) < 1, $"{where}: журнал {logTotal:R}, измеритель {before.TotalDamage:R}");
            meter.SetMode(MeterMode.DpsBySkill);
            _main.CloseAllWindows();

            // A skill on cooldown: pressed again by its key, it is refused with a log line and the text over the hero.
            var cooldown = "нет скилла с перезарядкой";
            if (Training.Slots.FirstOrDefault(s => s is { CooldownSec: > 0, IsChannel: false }) is { } cd)
            {
                PlaceNearDummy();
                void Tap(int slot)
                {
                    var keycode = Key.Key1 + (slot - 1);
                    Driver.HandleInput(new InputEventKey { PhysicalKeycode = keycode, Keycode = keycode, Pressed = true });
                    Driver.HandleInput(new InputEventKey { PhysicalKeycode = keycode, Keycode = keycode, Pressed = false });
                }
                // Its key, and between its tries the other slots' keys (a skill that costs Fury needs a generator first).
                var others = Training.Slots.Where(s => s != null && s.Slot != cd.Slot && !s.IsChannel).Select(s => s!.Slot).ToList();
                var (next, turn) = (0.0, 0);
                await Run(15, t =>
                {
                    if (t < next || Training.State is not { Busy: false } s || s.CooldownLeftBySlot.ContainsKey(cd.Slot)) return;
                    next = t + 0.3;
                    Tap(turn++ % 2 == 0 || others.Count == 0 ? cd.Slot : others[(turn / 2) % others.Count]);
                }, until: () => Training.State is { Busy: false } s && s.CooldownLeftBySlot.ContainsKey(cd.Slot));
                Check(Training.State!.CooldownLeftBySlot.ContainsKey(cd.Slot), $"{where}: «{cd.Name}» не ушел на перезарядку за 15 с");
                var refusals = RefusalLines(cd.SkillId);
                Tap(cd.Slot);
                await Run(FrameSec);
                Check(RefusalLines(cd.SkillId) == refusals + 1, $"{where}: отказа «{cd.Name}» на перезарядке нет в журнале");
                Check(Room.Hero.RefusalText is { } text && text.StartsWith("Перезарядка", StringComparison.Ordinal), $"{where}: над героем «{Room.Hero.RefusalText}»");
                cooldown = $"«{cd.Name}»: {Room.Hero.RefusalText}";
                cooldowns++;
                Meter.SetSimMode(SimMode.Average, 1); // back to a fight from zero for the edit below
            }

            // A passive taken off in the P window: the same keys from zero give a new segment with another DPS.
            FightSegment? after = null;
            var dropped = await DropPassive(async () =>
            {
                after = await KeySegment(restart: false); // the edit itself restarted the fight
                return Math.Abs(after.Dps / before.Dps - 1) > 5e-3; // a visible change, not a last-bit one
            });
            Check(dropped != null && after != null, $"{where}: ни один узел пассивок не снимается с изменением DPS больше 0,5 %");
            Check(after!.AfterBuildChange, $"{where}: новый сегмент «{after.Label}» не помечен правкой билда");
            var name = _main.Session!.Data.Passives.Nodes[dropped!].Name;
            Check(await ClickPassive(dropped!), $"{where}: узел {dropped} не взят обратно");
            _main.CloseAllWindows();
            _roomTable.Add($"{info.Title} | {lines.Count} | {BreakdownText.Whole(before.Dps)} | {BreakdownText.Whole(meterTotal)} = {BreakdownText.Whole(logTotal)} | {cooldown} | «{name}» → {BreakdownText.Whole(after.Dps)} ({CombatLogText.DpsChange(after.Dps / before.Dps - 1)})");
        }
        Check(cooldowns > 0, "ни у одного эталона нет скилла с перезарядкой: отказ не проверен");
        GD.Print($"SELFTEST ROOM эталон | строк журнала сегмента | DPS ({AcceptanceFightSec:0} с клавишами, «среднее») | урон: измеритель = журнал | отказ на перезарядке | снят узел → DPS нового сегмента");
        foreach (var row in _roomTable) GD.Print("SELFTEST ROOM " + row);
    }

    /// <summary>
    /// On «Рой» with the plate over a figure and numbers flying from all five: after every frame no number's screen box overlaps
    /// the plate (task 14 review: they slid under it).
    /// </summary>
    private async Task HudNumbersClearOfPlate()
    {
        var hud = _main.Hud ?? throw new Failure("нет экрана боя");
        await LoadForCombat(WarriorMelee);
        Device.Set(Dialoid.Game.Input.InputKind.KeyboardMouse);
        Driver.SetDummy("swarm");
        Meter.SetFloatingNumbers(true);
        var dummy = Room.Dummy;
        Room.PlaceHero(dummy.TargetPosition(0) + TowardsCamera * 3.1f + new Vector3(1, 0, -1).Normalized() * 0.9f);
        Room.CameraRig.ResetZoom();
        Room.CameraRig.Follow();
        var cleave = SlotOf("cleave");
        int frames = 0, numbers = 0, worst = 0;
        string? bad = null;
        var shownBefore = Room.Numbers.ShownTotal;
        for (int i = 0; i < 240 && bad == null; i++)
        {
            if (Training.State is { Busy: false }) Driver.PressSlot(cleave);
            Driver.Tick(FrameSec);
            hud.Refresh();
            if (i % 10 == 9) await NextFrame();
            if (!hud.PlateShown) continue;
            frames++;
            var plate = hud.PlateRect;
            var count = 0;
            foreach (var (c, hr, hu) in Room.Numbers.Boxes())
            {
                var corners = new[] { c + hr + hu, c + hr - hu, c - hr + hu, c - hr - hu }.Select(_main.RoomToScreen).ToList();
                if (corners.Any(p => p == null)) continue;
                var box = new Rect2(corners[0]!.Value, Vector2.Zero);
                foreach (var p in corners) box = box.Expand(p!.Value);
                numbers++;
                if (box.Intersects(plate)) { count++; bad ??= $"число {box} на плашке {plate}"; }
            }
            worst = Math.Max(worst, count);
        }
        Check(Room.Numbers.ShownTotal > shownBefore && numbers > 0, "чисел не было: проверка ничего не проверила");
        Check(frames > 0, "плашка ни разу не показана");
        Check(bad == null, $"на кадре {frames}: {bad}");
        GD.Print($"SELFTEST NOTE числа и плашка на «Рое»: {frames} кадров с плашкой, {numbers} положений чисел, на плашке 0 (чисел всего {Room.Numbers.ShownTotal - shownBefore})");
        Driver.SetDummy(TrainingRoom.DefaultDummyId);
    }

    /// <summary>On «Рой» the chevron marks the plate's figure; when the plate goes (its figure behind the camera) the chevron goes too.</summary>
    private async Task HudMarkerHidesWithPlate()
    {
        var hud = _main.Hud ?? throw new Failure("нет экрана боя");
        await LoadForCombat(WarriorMelee);
        Device.Set(Dialoid.Game.Input.InputKind.KeyboardMouse);
        Driver.SetDummy("swarm");
        var dummy = Room.Dummy;
        Room.PlaceHero(dummy.TargetPosition(0) - TowardsCamera * 3.0f); // the pack low on screen: room for the plate and the chevron
        Room.CameraRig.ResetZoom();
        Room.CameraRig.Follow();
        await Settle();
        hud.Refresh();
        Check(hud.PlateShown && hud.MarkerShown, $"на «Рое» плашка {(hud.PlateShown ? "есть" : "нет")}, метка {(hud.MarkerShown ? "есть" : "нет")} (плашка {hud.PlateRect})");
        var camera = Room.CameraRig.Camera;
        try
        {
            dummy.GlobalPosition = camera.GlobalPosition + camera.GlobalBasis.Z * 6f; // behind the camera
            hud.Refresh();
            Check(!hud.PlateShown && !hud.MarkerShown, $"фигура за камерой: плашка {(hud.PlateShown ? "есть" : "нет")}, метка {(hud.MarkerShown ? "осталась" : "нет")}");
        }
        finally
        {
            dummy.Position = Room.DummySpot;
        }
        hud.Refresh();
        Check(hud.PlateShown && hud.MarkerShown, "плашка и метка не вернулись");
        Driver.SetDummy(TrainingRoom.DefaultDummyId);
    }
}
