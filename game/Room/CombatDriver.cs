using Dialoid.Core.Simulation;
using Dialoid.Game.Input;
using Dialoid.Core.Stats;
using Dialoid.Sandbox;
using Godot;

namespace Dialoid.Game.Room;

/// <summary>
/// Wires the live fight (<see cref="TrainingRoom"/>) to the 3D room (plan 4 task 9). Every frame: the hero's distance to the
/// collision edge of the nearest dummy figure and whether a figure is on screen go to <see cref="TrainingRoom.UpdateReach"/>,
/// then <see cref="TrainingRoom.Advance"/> gets the <b>real</b> frame time, then the feed becomes pictures: a use plays the
/// hero's clip stretched to the core's use time and turns him to the dummy; its hits wait for the clip's contact frame (melee:
/// flash, number, reaction and a 40 ms hit-stop; a projectile flies from there and its hits land on arrival, ≤ 0.6 s; lightning
/// draws its ribbon; poison its cloud); DoT ticks flash at once; a refusal shows its text over the hero. The core's numbers never
/// change: only the moment they are shown does.
/// <para>Hit-stop stops only pictures: the hero's and the dummy's AnimationPlayer speed_scale is 0 and effects pause. The engine's
/// time scale and the tree are never touched; the fight keeps its own clock.</para>
/// <para>Keys 1–5 press slots (a channel skill: <see cref="TrainingRoom.StartChannel"/> on press, <see cref="TrainingRoom.StopChannel"/>
/// on release, one call per press), WASD and the left button move the hero. Input arrives only as events the interface did not
/// take (<see cref="_UnhandledInput"/> inside the room's viewport: a window over the room stops mouse events in the GUI, so a
/// click or the wheel over a window never reaches here), and no key press while a text field (LineEdit, SpinBox, TextEdit) has
/// focus; releases always pass. An open window by itself does not block the fight (spec §4 «Ввод»).</para>
/// </summary>
public partial class CombatDriver : Node
{
    public const double HitStopSec = 0.04;
    public const double MaxFlightSec = 0.6;
    public const float ProjectileSpeed = 14f;
    public const double HoldToRunSec = 0.25;

    /// <summary>DoT ticks of a target (the core sends one per step) become one weak flash and one small number this often (spec §5: ticks are small numbers, no reaction).</summary>
    public const double DotShowSec = 0.4;

    /// <summary>Hits of a use never wait longer than its end plus this (a lost projectile, a skipped frame).</summary>
    public const double MaxHitDelaySec = MaxFlightSec + 0.3;

    /// <summary>How the hits of a use are shown.</summary>
    public enum UseVisual
    {
        /// <summary>At the contact frame on the target, with reaction and hit-stop.</summary>
        Melee,
        /// <summary>A ball flies from the contact frame; hits land on arrival.</summary>
        Projectile,
        /// <summary>A lightning ribbon at the contact frame.</summary>
        Arc,
        /// <summary>A ball falls on the target from above (Meteor).</summary>
        Fall,
        /// <summary>A poison cloud at the target's feet.</summary>
        Cloud,
        /// <summary>At the contact frame, no projectile (other spells, shouts).</summary>
        Instant,
    }

    private sealed class PendingUse
    {
        public required string SkillId { get; init; }
        public required UseVisual Visual { get; init; }
        public required EffectKind Kind { get; init; }
        public required bool Channel { get; init; }
        public required double Start { get; init; }
        public required double Contact { get; init; }
        public required double End { get; init; }
        public bool Released { get; set; }
        public bool Delivered { get; set; }
        public ProjectileView? Projectile { get; set; }
        public List<VisualEvent> Hits { get; } = new();
    }

    /// <summary>DoT ticks of one target waiting for their next number: summed, the largest type, when the last number was shown.</summary>
    private sealed class DotSum
    {
        public double Amount;
        public double Largest;
        public Dialoid.Core.Combat.DamageType? Type;
        public double ShownAt = double.NegativeInfinity;
    }

    /// <summary>A melee use's timeline in visual seconds, for checks: the flash must fall between the contact frame and the use's end.</summary>
    public sealed record MeleeTimeline(string SkillId, double Start, double Contact, double End, double? FlashAt);

    private readonly List<PendingUse> _uses = new();
    private readonly HashSet<string> _moveKeys = new();
    private readonly Dictionary<int, int> _slotHeld = new();
    private Vector2 _stick;
    private readonly Dictionary<int, DotSum> _dots = new();
    private double _visualTime;
    private double _freezeLeft;
    private int? _channelSlot;
    private bool _leftHeld;
    private double _leftHeldFor;
    private Vector2 _mouse;
    private double _lastFightTime;

    public RoomView Room { get; private set; } = null!;
    public TrainingRoom Training { get; private set; } = null!;
    public SkillAnimations Animations { get; private set; } = SkillAnimations.Defaults;
    public HeroView Hero => Room.Hero;
    public DummyView Dummy => Room.Dummy;
    public EffectLayer Effects => Room.Effects;
    public FloatingNumbers Numbers => Room.Numbers;

    /// <summary>An extra hook: true when the interface owns the keys now; a focused text field always blocks.</summary>
    public Func<bool>? InputBlocked { get; set; }

    /// <summary>Moves where a floating number starts (the combat screen lifts it above the dummy plate, which would cover it).</summary>
    public Func<Vector3, Vector3>? NumberLift { get; set; }

    private Vector3 Lift(Vector3 at) => NumberLift?.Invoke(at) ?? at;

    /// <summary>A press was refused (the combat screen blinks its slot; the text over the hero is shown here).</summary>
    public event Action<VisualEvent>? Refused;

    /// <summary>Visual seconds: real time minus the hit-stops.</summary>
    public double VisualTime => _visualTime;

    /// <summary>A hit-stop is running.</summary>
    public bool Frozen => _freezeLeft > 0;

    public int HitStops { get; private set; }

    /// <summary>Small tick numbers shown and the damage they carried (for checks: the sums equal the core's ticks).</summary>
    public int DotNumbers { get; private set; }
    public double DotAmountShown { get; private set; }

    /// <summary>Real seconds passed to <see cref="TrainingRoom.Advance"/> in total and while frozen (for checks).</summary>
    public double Advanced { get; private set; }
    public double AdvancedWhileFrozen { get; private set; }

    /// <summary>For each projectile use whose hits were shown: whether its ball had landed by then (true unless the
    /// <see cref="MaxHitDelaySec"/> fallback fired). The latest 64, for checks.</summary>
    public List<bool> ProjectileLandings { get; } = new();

    /// <summary>The last melee use's timeline.</summary>
    public MeleeTimeline? LastMelee { get; private set; }

    /// <summary>The last hit shown (after its contact frame or projectile).</summary>
    public VisualEvent? LastShownHit { get; private set; }

    public void Setup(RoomView room, TrainingRoom training, SkillAnimations animations)
    {
        Room = room;
        Training = training;
        Animations = animations;
        Hero.Rig = room.CameraRig;
        Effects.Camera = room.CameraRig.Camera;
        Numbers.Camera = room.CameraRig.Camera;
        Effects.LightsAllowed = () => room.ProjectileLights;
        SyncDummy();
    }

    public override void _Process(double delta) => Tick(delta);

    /// <summary>One frame of <paramref name="delta"/> real seconds (the self-test calls it directly with fixed steps).</summary>
    public void Tick(double delta)
    {
        if (Room == null || delta <= 0) return;
        var frozenNow = Frozen;
        if (frozenNow) _freezeLeft -= delta;
        var visualDt = frozenNow ? 0 : delta;
        SetFrozen(Frozen);

        // A restarted fight (build edit, dummy or mode change) starts at 0: what the old one left pending is gone.
        var fightTime = Training.State?.Time ?? 0;
        if (fightTime < _lastFightTime) ResetVisuals();
        _lastFightTime = fightTime;
        SyncDummy();
        if ((_moveKeys.Count > 0 || _channelSlot != null || _stick != Vector2.Zero || _slotHeld.Count > 0) && KeysBlocked()) ReleaseAll(); // focus moved to a text field or a window
        Training.UpdateReach(HeroDistance(), TargetOnScreen());
        Training.Advance(delta); // the real frame time: hit-stop never slows the fight
        Advanced += delta;
        if (frozenNow) AdvancedWhileFrozen += delta;
        foreach (var e in Training.Feed.DrainVisual()) Show(e);

        _visualTime += visualDt;
        UpdateUses();
        ShowDots();
        if (_leftHeld)
        {
            _leftHeldFor += delta;
            if (_leftHeldFor >= HoldToRunSec && FloorUnder(_mouse) is { } p) Hero.MoveTo(p, run: true);
        }
        Hero.Advance(visualDt);
        Effects.Advance(visualDt);
        Numbers.Advance(visualDt); // the dummy turns its runes in its own _Process (stopped while frozen)
    }

    // ---------- reach ----------

    /// <summary>Metres from the hero to the collision edge of the nearest dummy figure.</summary>
    public double HeroDistance() => Dummy.Nearest(Hero.Position).Distance;

    /// <summary>At least one dummy figure is inside the camera's frame.</summary>
    public bool TargetOnScreen()
    {
        for (int i = 0; i < Dummy.FigureCount; i++)
            if (Room.CameraRig.IsOnScreen(Dummy.TargetPosition(i))) return true;
        return false;
    }

    // ---------- presses ----------

    /// <summary>Key N down: <see cref="TrainingRoom.Press"/>, or <see cref="TrainingRoom.StartChannel"/> for a channel skill.</summary>
    public PressResult PressSlot(int slot)
    {
        var info = slot >= 1 && slot <= Training.Slots.Count ? Training.Slots[slot - 1] : null;
        if (info is { IsChannel: true })
        {
            var r = Training.StartChannel(slot, HeroDistance(), TargetOnScreen());
            if (r.Accepted) _channelSlot = slot;
            return r;
        }
        return Training.Press(slot, HeroDistance(), TargetOnScreen());
    }

    /// <summary>Key N up: releases the channel held by that key.</summary>
    public void ReleaseSlot(int slot)
    {
        if (_channelSlot != slot) return;
        _channelSlot = null;
        Training.StopChannel();
    }

    /// <summary>Chooses a dummy preset: the fight restarts in the core and the figures change.</summary>
    public void SetDummy(string presetId)
    {
        Training.SetDummy(presetId);
        SyncDummy();
    }

    private void SyncDummy()
    {
        if (Dummy.PresetId == Training.DummyId) return;
        Dummy.SetPreset(Training.DummyId);
        SelectedTarget = null;
        ResetVisuals();
    }

    /// <summary>Forgets what the previous fight left: pending hits, tick sums, effects, numbers, the refusal text.</summary>
    public void ResetVisuals()
    {
        _lastFightTime = Training.State?.Time ?? 0;
        _uses.Clear();
        _dots.Clear();
        LastMelee = null;
        LastShownHit = null;
        Hero.ClearRefusal();
        _channelSlot = null;
        Effects.Clear();
        Numbers.Clear();
    }

    // ---------- feed ----------

    /// <summary>One event of the feed (public for the self-test, which may inject ticks).</summary>
    public void Show(VisualEvent e)
    {
        switch (e.Kind)
        {
            case VisualKind.SkillUsed:
                OnUse(e);
                break;
            case VisualKind.Hit when e.IsDot:
                AddDot(e);
                break;
            case VisualKind.Hit:
                var use = PendingFor(e);
                if (use != null) use.Hits.Add(e);
                else Deliver(e, melee: false, channel: false);
                break;
            case VisualKind.Refused:
                if (e.RefusalText is { } text) Hero.ShowRefusal(text);
                Refused?.Invoke(e);
                break;
        }
    }

    private void OnUse(VisualEvent e)
    {
        // A use without a slot is a triggered copy (Fireball's explosion): the hero did not swing; its hits join the latest use.
        if (e.SkillId is not { } skillId || e.Slot is not { } slot) return;
        var tags = Training.CompiledTags(slot) ?? TagSet.Empty;
        var useSec = e.UseSec ?? 0.5;
        // An older use of the same skill that has not reached its contact frame reaches it now (a channel tick replaces the
        // previous one); a ball already flying keeps flying: its hits land on arrival (or after MaxHitDelaySec).
        foreach (var old in _uses.Where(u => u.SkillId == skillId && !u.Released).ToList()) Release(old);

        var anim = Animations.For(skillId, tags);
        var channel = tags.Contains("channel");
        var visual = VisualOf(tags);
        Hero.FaceTowards(Dummy.TargetPosition(Dummy.RingTarget ?? 0)); // the target the core hit last (on «Рой» not always figure 0)
        Hero.PlayAttack(anim.ClipId, useSec);
        var start = _visualTime;
        var use = new PendingUse
        {
            SkillId = skillId,
            Visual = visual,
            Kind = DamageVisuals.Of(tags),
            Channel = channel,
            Start = start,
            Contact = start + anim.ContactSec(useSec),
            End = start + useSec,
        };
        _uses.Add(use);
        if (visual == UseVisual.Melee && !channel) LastMelee = new MeleeTimeline(skillId, use.Start, use.Contact, use.End, null);
    }

    /// <summary>How a skill's hits are shown, by its compiled tags (spec §5: lightning has no projectile, only a ribbon).</summary>
    public static UseVisual VisualOf(TagSet tags)
    {
        if (tags.Contains("projectile")) return tags.Contains("lightning") ? UseVisual.Arc : UseVisual.Projectile;
        if (tags.Contains("melee")) return UseVisual.Melee;
        if (tags.Contains("poison")) return UseVisual.Cloud;
        if (tags.Contains("fire") && tags.Contains("area")) return UseVisual.Fall;
        if (tags.Contains("lightning")) return UseVisual.Arc;
        return UseVisual.Instant;
    }

    // A hit waits for the latest undelivered use of its skill; a hit of a skill in no slot (Fireball's explosion) for the latest use.
    private PendingUse? PendingFor(VisualEvent hit)
    {
        for (int i = _uses.Count - 1; i >= 0; i--)
            if (!_uses[i].Delivered && _uses[i].SkillId == hit.SkillId) return _uses[i];
        if (hit.Slot == null)
            for (int i = _uses.Count - 1; i >= 0; i--)
                if (!_uses[i].Delivered) return _uses[i];
        return null;
    }

    private void UpdateUses()
    {
        for (int i = 0; i < _uses.Count; i++)
        {
            var u = _uses[i];
            if (!u.Released && _visualTime >= u.Contact) Release(u);
            if (!u.Delivered && _visualTime >= u.End + MaxHitDelaySec) Deliver(u);
            if (u.Delivered && _visualTime >= u.End) _uses.RemoveAt(i--);
        }
    }

    /// <summary>The contact frame (once): melee and instant hits land, a projectile or ribbon starts.</summary>
    private void Release(PendingUse u)
    {
        if (u.Released) return;
        u.Released = true;
        var target = u.Hits.FirstOrDefault()?.Target ?? Dummy.RingTarget ?? 0;
        var to = Dummy.HitPoint(target);
        if (u.Visual is UseVisual.Melee or UseVisual.Instant)
        {
            Deliver(u);
            return;
        }
        var from = Hero.Position + new Vector3(0, 1.3f, 0) + Hero.Basis.Z * 0.4f;
        switch (u.Visual)
        {
            case UseVisual.Projectile:
            {
                var flight = Math.Min(MaxFlightSec, from.DistanceTo(to) / ProjectileSpeed);
                var p = u.Projectile = Effects.Projectile(u.Kind, from, to, flight);
                p.Landed = () => Deliver(u);
                break;
            }
            case UseVisual.Fall:
            {
                var p = u.Projectile = Effects.Projectile(u.Kind, to + new Vector3(0.6f, 6f, 0.6f), to, 0.35);
                p.Landed = () => Deliver(u);
                break;
            }
            case UseVisual.Arc:
                Effects.Arc(from, to);
                Deliver(u);
                break;
            case UseVisual.Cloud:
                Effects.Cloud(Dummy.TargetPosition(target));
                Deliver(u);
                break;
        }
    }

    private void Deliver(PendingUse u)
    {
        if (u.Delivered) return;
        u.Delivered = true;
        u.Released = true;
        var melee = u.Visual == UseVisual.Melee;
        var shown = false;
        foreach (var hit in u.Hits) shown |= Deliver(hit, melee, u.Channel);
        u.Hits.Clear();
        if (u.Projectile != null && shown)
        {
            ProjectileLandings.Add(u.Projectile.Arrived);
            if (ProjectileLandings.Count > 64) ProjectileLandings.RemoveAt(0);
        }
        if (shown && melee && !u.Channel && LastMelee is { } m && m.SkillId == u.SkillId && m.Start == u.Start)
            LastMelee = m with { FlashAt = _visualTime };
        if (melee && !u.Channel && shown) HitStop();
    }

    /// <summary>Flash, number and (for a hit, not a tick or a channel tick) the reaction; true when a hit was shown.</summary>
    private bool Deliver(VisualEvent hit, bool melee, bool channel)
    {
        var target = hit.Target ?? 0;
        var kind = DamageVisuals.Of(hit.DamageType);
        Dummy.MarkTarget(target);
        Dummy.Flash(Effects, target, kind, hit.IsCrit, hit.IsDot);
        if (!hit.IsDot && !channel) Dummy.React(target, _visualTime);
        // Tick numbers start lower than hit numbers so the two streams do not cover each other.
        if (hit.Amount > 0) Numbers.Show(Lift(Dummy.HitPoint(target) + new Vector3(0, hit.IsDot ? 0.05f : 0.6f, 0)), hit.Amount, kind, hit.IsCrit, hit.IsDot);
        LastShownHit = hit;
        return !hit.IsDot;
    }

    /// <summary>A DoT tick: summed per target; <see cref="ShowDots"/> turns the sum into one weak flash and one small number at most every <see cref="DotShowSec"/>.</summary>
    private void AddDot(VisualEvent tick)
    {
        // On a pack («Рой») the ticks of every figure become one number over the pack centre.
        var target = Dummy.FigureCount > 1 ? PackKey : tick.Target ?? 0;
        if (!_dots.TryGetValue(target, out var sum)) _dots[target] = sum = new DotSum();
        sum.Amount += tick.Amount;
        if (tick.Amount >= sum.Largest) (sum.Largest, sum.Type) = (tick.Amount, tick.DamageType);
    }

    /// <summary>Shows the summed ticks of each target whose last tick number is older than <see cref="DotShowSec"/>.</summary>
    private void ShowDots()
    {
        foreach (var (target, sum) in _dots)
        {
            if (sum.Amount <= 0 || _visualTime - sum.ShownAt < DotShowSec) continue;
            if (target == PackKey)
            {
                var kind = DamageVisuals.Of(sum.Type);
                var centre = Dummy.PackHitPoint();
                Effects.Impact(kind, centre, crit: false, tick: true);
                Numbers.Show(Lift(centre + new Vector3(0, 0.05f, 0)), sum.Amount, kind, crit: false, tick: true);
            }
            else
            {
                var hit = new VisualEvent(VisualKind.Hit, 0, null, null, target) { DamageType = sum.Type, Amount = sum.Amount, IsDot = true };
                Deliver(hit, melee: false, channel: false);
            }
            DotNumbers++;
            DotAmountShown += sum.Amount;
            (sum.Amount, sum.Largest, sum.ShownAt) = (0, 0, _visualTime);
        }
    }

    /// <summary>The key of the pack-wide tick sum.</summary>
    private const int PackKey = -1;

    /// <summary>Lets go of held move keys and the held channel (focus moved to a text field).</summary>
    private void ReleaseHeldKeys()
    {
        _moveKeys.Clear();
        Hero.SetMoveInput(Vector2.Zero);
        if (_channelSlot is { } slot) ReleaseSlot(slot);
    }

    // ---------- hit-stop ----------

    /// <summary>Freezes the pictures for <see cref="HitStopSec"/> real seconds (animations and effects; never the fight or the engine).</summary>
    public void HitStop()
    {
        _freezeLeft = HitStopSec;
        HitStops++;
        SetFrozen(true);
    }

    private void SetFrozen(bool frozen)
    {
        if (Hero.Frozen != frozen) Hero.Frozen = frozen;
        if (Dummy.Frozen != frozen) Dummy.Frozen = frozen;
        if (Effects.Paused != frozen) Effects.Paused = frozen;
    }

    // ---------- input ----------

    public override void _UnhandledInput(InputEvent e)
    {
        if (HandleInput(e)) GetViewport().SetInputAsHandled();
    }

    /// <summary>The fight's reaction to one input event the interface did not take; true when it used it.</summary>
    public bool HandleInput(InputEvent e)
    {
        if (Room == null) return false;
        // A hidden room takes no presses, but releases always pass (a held channel or move key is never stuck).
        if (!Room.IsShown && e.IsPressed()) return false;
        switch (e)
        {
            case InputEventKey or InputEventJoypadButton or InputEventJoypadMotion:
                // Releases always pass, so a held channel or a move key is never stuck when the focus moved meanwhile.
                if (e.IsPressed() && KeysBlocked()) return false;
                return HandleAction(e);
            case InputEventMouseMotion motion:
                _mouse = motion.Position;
                return false;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                _mouse = button.Position;
                if (!button.Pressed)
                {
                    _leftHeld = false;
                    return Room.IsShown;
                }
                if (KeysBlocked()) ReleaseFocus();
                Click(button.Position);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// The fight's actions (<see cref="InputActions"/>), from the keyboard or the gamepad alike: slots 1–5 (X, A, B, Y, RT; the
    /// trigger presses at 0.5 and lets go at 0.3), movement (WASD runs; the left stick has a round dead zone of 0.2, walks up to
    /// 0.6 and runs past it, relative to the camera), approach the target (F, LB), next target (Tab, RB). A slot held on both
    /// devices is let go when both let go. Presses are refused while the interface owns the input; releases always pass.
    /// </summary>
    private bool HandleAction(InputEvent e)
    {
        var pressed = e.IsPressed() && !e.IsEcho();
        for (int slot = 1; slot <= 5; slot++)
        {
            var action = InputActions.Slot(slot);
            if (!e.IsAction(action)) continue;
            var source = e is InputEventJoypadButton or InputEventJoypadMotion ? PadSource : KeySource;
            if (e is InputEventJoypadMotion trigger)
            {
                var down = (_slotHeld.GetValueOrDefault(slot) & PadSource) != 0;
                if (!down && trigger.AxisValue >= InputActions.TriggerPress) HoldSlot(slot, source);
                else if (down && trigger.AxisValue <= InputActions.TriggerRelease) LetGoSlot(slot, source);
                return true;
            }
            if (pressed) HoldSlot(slot, source);
            else if (!e.IsPressed()) LetGoSlot(slot, source);
            return true;
        }
        if (e is InputEventJoypadMotion stick && stick.Axis is JoyAxis.LeftX or JoyAxis.LeftY &&
            (e.IsAction(InputActions.MoveLeft) || e.IsAction(InputActions.MoveRight) || e.IsAction(InputActions.MoveUp) || e.IsAction(InputActions.MoveDown)))
        {
            var value = KeysBlocked() ? 0 : stick.AxisValue;
            _stick = stick.Axis == JoyAxis.LeftX ? _stick with { X = value } : _stick with { Y = value };
            UpdateMove();
            return true;
        }
        foreach (var action in MoveActions)
        {
            if (!e.IsAction(action)) continue;
            if (pressed && !KeysBlocked()) _moveKeys.Add(action);
            else if (!e.IsPressed()) _moveKeys.Remove(action);
            UpdateMove();
            return true;
        }
        if (e.IsAction(InputActions.ApproachTarget))
        {
            if (pressed && !KeysBlocked()) ApproachDummy(SelectedTarget ?? Dummy.Nearest(Hero.Position).Target);
            return true;
        }
        if (e.IsAction(InputActions.NextTarget))
        {
            if (pressed && !KeysBlocked()) SelectNextTarget();
            return true;
        }
        return false;
    }

    private static readonly string[] MoveActions = [InputActions.MoveLeft, InputActions.MoveRight, InputActions.MoveUp, InputActions.MoveDown];
    private const int KeySource = 1, PadSource = 2;

    private void HoldSlot(int slot, int source)
    {
        if (KeysBlocked()) return;
        var held = _slotHeld.GetValueOrDefault(slot);
        _slotHeld[slot] = held | source;
        if (_channelSlot == slot) return; // the channel is already held by the other device
        PressSlot(slot);
    }

    private void LetGoSlot(int slot, int source)
    {
        var held = _slotHeld.GetValueOrDefault(slot) & ~source;
        if (held == 0)
        {
            _slotHeld.Remove(slot);
            ReleaseSlot(slot);
        }
        else _slotHeld[slot] = held;
    }

    /// <summary>Keys run; the stick walks below <see cref="InputActions.StickRun"/> and runs past it; the keys win when both move.</summary>
    private void UpdateMove()
    {
        var keys = new Vector2(
            (_moveKeys.Contains(InputActions.MoveRight) ? 1 : 0) - (_moveKeys.Contains(InputActions.MoveLeft) ? 1 : 0),
            (_moveKeys.Contains(InputActions.MoveUp) ? 1 : 0) - (_moveKeys.Contains(InputActions.MoveDown) ? 1 : 0));
        if (keys != Vector2.Zero)
        {
            Hero.SetMoveInput(keys);
            return;
        }
        var stick = new Vector2(_stick.X, -_stick.Y);
        var length = Mathf.Min(1, stick.Length());
        if (length < InputActions.StickDeadzone) Hero.SetMoveInput(Vector2.Zero);
        else Hero.SetMoveInput(stick.Normalized(), run: length > InputActions.StickRun);
    }

    /// <summary>The figure RB (or Tab) chose for LB / F, or null (then the nearest one).</summary>
    public int? SelectedTarget { get; private set; }

    /// <summary>RB: the next figure (on «Рой»; the only one otherwise) gets the ring and becomes the target of LB.</summary>
    public void SelectNextTarget()
    {
        if (Dummy.FigureCount == 0) return;
        var from = SelectedTarget ?? Dummy.RingTarget ?? -1;
        SelectedTarget = (from + 1) % Dummy.FigureCount;
        Dummy.MarkTarget(SelectedTarget.Value);
    }

    /// <summary>The gamepad was lost or the interface took the input: nothing stays held.</summary>
    public void ReleaseAll()
    {
        _slotHeld.Clear();
        _stick = Vector2.Zero;
        _leftHeld = false;
        ReleaseHeldKeys();
    }

    /// <summary>Gamepad mode: the cursor is hidden, the run after the held cursor stops.</summary>
    public void OnDeviceChanged(InputKind kind)
    {
        if (kind == InputKind.Gamepad) _leftHeld = false;
    }

    /// <summary>A click on the floor walks there (held: runs after the cursor); on a dummy figure — walks into melee reach of it.</summary>
    public void Click(Vector2 screen)
    {
        _leftHeld = true;
        _leftHeldFor = 0;
        var camera = Room.CameraRig.Camera;
        var origin = camera.ProjectRayOrigin(screen);
        var dir = camera.ProjectRayNormal(screen);
        if (Dummy.Pick(origin, dir) is { } figure)
        {
            ApproachDummy(figure);
            _leftHeld = false;
            return;
        }
        if (FloorUnder(screen) is { } p) Hero.MoveTo(p, run: false);
    }

    /// <summary>Walks to the given figure until well inside melee reach.</summary>
    public void ApproachDummy(int target)
    {
        var feet = Dummy.TargetPosition(target);
        var away = new Vector3(Hero.Position.X - feet.X, 0, Hero.Position.Z - feet.Z);
        if (away.LengthSquared() < 1e-4f) away = new Vector3(1, 0, 1);
        var stand = feet + away.Normalized() * (Dummy.CollisionRadius(target) + (float)ReachRules.MeleeReachMeters * 0.6f);
        Hero.MoveTo(stand, run: true);
    }

    private Vector3? FloorUnder(Vector2 screen)
    {
        var camera = Room.CameraRig.Camera;
        var origin = camera.ProjectRayOrigin(screen);
        var dir = camera.ProjectRayNormal(screen);
        if (dir.Y > -1e-4f) return null;
        var p = origin + dir * (-origin.Y / dir.Y);
        return new Vector3(Mathf.Clamp(p.X, -RoomView.HalfX + 0.5f, RoomView.HalfX - 0.5f), 0, Mathf.Clamp(p.Z, -RoomView.HalfZ + 0.5f, RoomView.HalfZ - 0.5f));
    }

    /// <summary>Keys go to the interface: a text field has focus (spec §4 «Ввод»), or the windows say so.</summary>
    public bool KeysBlocked()
    {
        if (InputBlocked?.Invoke() == true) return true;
        var focus = GetTree()?.Root.GuiGetFocusOwner();
        return focus is LineEdit or TextEdit or SpinBox;
    }

    private void ReleaseFocus() => GetTree()?.Root.GuiGetFocusOwner()?.ReleaseFocus();
}
