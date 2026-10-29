using Dialoid.Core.Character;
using Dialoid.Core.Combat;
using Dialoid.Core.Effects;
using Dialoid.Core.Simulation;
using Dialoid.Core.Stats;

namespace Dialoid.Sandbox;

/// <summary>
/// The training room without the 3D scene (spec 6–8): a <see cref="LiveFight"/> of the session's build against a dummy preset,
/// the reach rule (<see cref="ReachRules"/>), fight segments for the meter and the Russian combat log. Single-threaded: call
/// everything on the session's thread. The game layer calls <see cref="Advance"/> every frame and sends key presses with the
/// hero's distance to the dummy and whether the dummy is on the screen.
/// <para>A build edit (<see cref="SandboxSession.Changed"/>, after <see cref="Attach"/>) restarts the fight with the new build
/// (spec 6.2b): the current segment closes, held and queued keys are gone, and the log says so. Changing the dummy or the mode
/// restarts it too. An illegal build or an unknown dummy starts no fight (<see cref="Running"/> is false,
/// <see cref="Violations"/> say why) until the next edit.</para>
/// <para>Slots are the player's (1-based, keys 1–5, as the session numbers them, gaps included): key N presses the skill in
/// session slot N even when an earlier slot is empty. The fight's build lists only the filled slots, so the room translates.</para>
/// </summary>
public sealed class TrainingRoom
{
    public const double SegmentIdleSec = 5;
    public const int MaxSegments = 10;
    /// <summary>Most engine steps one <see cref="Advance"/> makes; the rest of a long frame is dropped (no spiral of death).</summary>
    public const int MaxCatchUpSteps = 20;
    /// <summary>The log window keeps this many latest lines; the meter counts the whole segment regardless.</summary>
    public const int MaxLogLines = 2000;
    public const string DefaultDummyId = "training";

    private const double Eps = 1e-9;

    private readonly SandboxSession _session;
    private readonly CombatLogText _text;
    private readonly List<FightSegment> _segments = new();
    private readonly List<string> _log = new(); // up to 2 × MaxLogLines; trimmed in batches, shown through _logTail
    private readonly LogTail _logTail;
    private LiveFight? _fight;
    private Build _build = new();
    // Player slot (index, 1-based) → the fight's slot (1-based among the filled ones), 0 for an empty slot; and back.
    // Taken together with the fight's build at the restart.
    private int[] _fightSlotOf = Array.Empty<int>();
    private int[] _playerSlotOf = Array.Empty<int>();
    private int? _heldSlot;
    private IReadOnlyList<RuleViolation> _violations = Array.Empty<RuleViolation>();
    private int _revision;
    private long _steps;
    private double _pendingDt;
    private int _nextIndex = 1;
    private bool _attached;

    /// <summary>Starts a fight with the session's current build against the training dummy in roll mode, seed 1 (spec 6.2: real
    /// crits and ailment rolls; <see cref="SetMode"/> switches to average). Does not follow build edits until <see cref="Attach"/>.</summary>
    public TrainingRoom(SandboxSession session)
    {
        _session = session;
        _text = new CombatLogText(session.Data);
        _logTail = new LogTail(_log);
        Restart(null);
    }

    public string DummyId { get; private set; } = DefaultDummyId;
    public SimMode Mode { get; private set; } = SimMode.Roll;
    public int Seed { get; private set; } = 1;

    /// <summary>A fight is going on (false for an illegal build or an unknown dummy).</summary>
    public bool Running => _fight != null;

    /// <summary>Why no fight is going on; empty while <see cref="Running"/>.</summary>
    public IReadOnlyList<RuleViolation> Violations => _violations;

    /// <summary>What the HUD shows (cooldowns by player slot, resource, buffs); null without a fight.</summary>
    public LiveState? State
    {
        get
        {
            if (_fight?.State is not { } s) return null;
            var cooldowns = new SortedDictionary<int, double>();
            foreach (var (fightSlot, left) in s.CooldownLeftBySlot)
                if (fightSlot < _playerSlotOf.Length) cooldowns[_playerSlotOf[fightSlot]] = left;
            return s with { CooldownLeftBySlot = cooldowns };
        }
    }

    /// <summary>Kept segments, oldest first, at most <see cref="MaxSegments"/>; the last one may be <see cref="Current"/>.</summary>
    public IReadOnlyList<FightSegment> Segments => _segments;

    /// <summary>The segment collecting damage now, or null between fights.</summary>
    public FightSegment? Current => _segments.Count > 0 && _segments[^1].IsOpen ? _segments[^1] : null;

    /// <summary>The latest Russian log lines, oldest first, at most <see cref="MaxLogLines"/>.</summary>
    public IReadOnlyList<string> LogLines => _logTail;

    /// <summary>Raised after anything shown changed: steps made, a restart, a logged refusal, a meter reset.</summary>
    public event Action? Updated;

    /// <summary>Tags of the compiled skill in a slot (1-based), null for an empty slot or without a fight.</summary>
    public TagSet? CompiledTags(int slot) => _fight?.CompiledTags(FightSlot(slot));

    /// <summary>Follow build edits from now on. Calling it twice is harmless.</summary>
    public void Attach()
    {
        if (_attached) return;
        _session.Changed += OnSessionChanged;
        _attached = true;
        if (_session.Revision != _revision) OnSessionChanged();
    }

    /// <summary>Stop following build edits (call before dropping the room). Calling it twice is harmless.</summary>
    public void Detach()
    {
        if (!_attached) return;
        _session.Changed -= OnSessionChanged;
        _attached = false;
    }

    /// <summary>Choose a dummy preset; restarts the fight.</summary>
    public void SetDummy(string dummyId)
    {
        DummyId = dummyId;
        Restart(Texts.DummyChangedRestart(BreakdownText.DummyName(_session.Data, dummyId)));
    }

    /// <summary>Choose the random mode and seed; restarts the fight.</summary>
    public void SetMode(SimMode mode, int seed)
    {
        Mode = mode;
        Seed = seed;
        Restart(Texts.ModeChangedRestart(BreakdownText.ModeName(mode)));
    }

    /// <summary>
    /// Real time passed since the previous call. Makes one engine step per <see cref="LiveFight.StepSec"/> of accumulated time,
    /// at most <see cref="MaxCatchUpSteps"/>; after a capped call the rest is dropped. Non-positive or non-finite time is ignored.
    /// </summary>
    public void Advance(double realDt)
    {
        if (_fight == null || !(realDt > 0) || double.IsInfinity(realDt)) return;
        _pendingDt += realDt;
        var due = Math.Floor(_pendingDt / LiveFight.StepSec + Eps);
        var steps = (int)Math.Min(MaxCatchUpSteps, due);
        _pendingDt = due > MaxCatchUpSteps ? 0 : Math.Max(0, _pendingDt - steps * LiveFight.StepSec);
        if (steps == 0) return;

        for (var i = 0; i < steps; i++) Step(_fight);
        Updated?.Invoke();
    }

    /// <summary>A key press on a slot (1-based): the reach rule first (refused <see cref="PressRefusal.OutOfReach"/>), then the
    /// fight's own checks. Every refusal is logged.</summary>
    public PressResult Press(int slot, double distanceTiles, bool targetOnScreen)
    {
        UpdateReach(distanceTiles, targetOnScreen);
        var r = Reach(slot, distanceTiles, targetOnScreen) ?? _fight!.Press(FightSlot(slot));
        if (!r.Accepted) LogRefusal(slot, r);
        return r;
    }

    /// <summary>
    /// Holds a slot's key (channel or repeat), after the reach rule. Returns the refusal (logged) when there is no fight, the dummy
    /// is out of reach, or the slot cannot be held (empty, triggered-only, wrong weapon: <see cref="LiveFight.HoldRefusal"/>);
    /// a refused hold leaves the previous one as it was. Cooldown and resource are not refusals: the held key waits for them.
    /// The hold stays under the reach rule: see <see cref="UpdateReach"/>.
    /// </summary>
    public PressResult StartChannel(int slot, double distanceTiles, bool targetOnScreen)
    {
        UpdateReach(distanceTiles, targetOnScreen);
        var refused = Reach(slot, distanceTiles, targetOnScreen)
            ?? (_fight!.HoldRefusal(FightSlot(slot)) is var why and not PressRefusal.None ? new PressResult(false, false, why, null) : null);
        if (refused != null)
        {
            LogRefusal(slot, refused);
            return refused;
        }
        _fight!.StartChannel(FightSlot(slot));
        _heldSlot = slot;
        return new PressResult(true, false, PressRefusal.None, null);
    }

    /// <summary>Releases the held key.</summary>
    public void StopChannel()
    {
        _heldSlot = null;
        _fight?.StopChannel();
    }

    /// <summary>
    /// Where the hero is now relative to the dummy (the game layer reports it every frame, or whenever it changes; presses report it
    /// too). A held key whose skill is no longer in reach (Whirlwind once the hero walks past <see cref="ReachRules.MeleeTiles"/>,
    /// Meteor once the dummy leaves the screen) is released, with one «вне досягаемости» refusal in the log.
    /// </summary>
    public void UpdateReach(double distanceTiles, bool targetOnScreen)
    {
        if (_heldSlot is not { } slot || Reach(slot, distanceTiles, targetOnScreen) is not { } refused) return;
        StopChannel();
        LogRefusal(slot, refused);
    }

    /// <summary>The meter's «Сбросить»: forgets every segment; the next damage starts a new one. The log stays.</summary>
    public void ResetMeter()
    {
        _segments.Clear();
        Updated?.Invoke();
    }

    private void OnSessionChanged()
    {
        if (_session.Revision == _revision) return;
        Restart(Texts.BuildChangedRestart);
    }

    // Null when the reach rule lets the press through to the fight.
    private PressResult? Reach(int slot, double distanceTiles, bool targetOnScreen)
    {
        if (_fight == null) return new PressResult(false, false, PressRefusal.FightOver, null);
        if (CompiledTags(slot) is not { } tags) return null; // empty slot (player slot, mapped): the fight refuses it
        var why = ReachRules.Check(tags, distanceTiles, targetOnScreen);
        return why == PressRefusal.None ? null : new PressResult(false, false, why, null);
    }

    private void Restart(string? line)
    {
        CloseCurrent();
        _revision = _session.Revision;
        _steps = 0;
        _pendingDt = 0;
        _heldSlot = null; // held and queued keys die with the old fight
        _build = _session.Build;
        MapSlots();
        _fight = LiveFight.Start(_session.Data, MechanicRegistry.Default, _build,
            new LiveFightConfig(DummyId, Mode, Seed), out var violations);
        _violations = _fight == null ? violations : Array.Empty<RuleViolation>();
        if (line != null) AddLog(line);
        if (_fight == null) AddLog(Texts.FightNotStarted(string.Join("; ", violations.Select(v => v.Message))));
        Updated?.Invoke();
    }

    private void Step(LiveFight fight)
    {
        fight.Step();
        _steps++;
        var now = _steps * LiveFight.StepSec;
        var events = fight.DrainEvents();
        var damage = fight.DrainDamage();

        foreach (var d in damage)
        {
            var seg = Current ?? Open(d.Time);
            seg.Add(d);
        }
        if (Current is { } current)
            foreach (var e in events) current.Add(e);
        Log(events, damage);

        if (Current is { } open && now - open.End >= SegmentIdleSec - Eps) open.Close();
    }

    private FightSegment Open(double start)
    {
        var seg = new FightSegment(_nextIndex++, start, _text);
        _segments.Add(seg);
        if (_segments.Count > MaxSegments) _segments.RemoveRange(0, _segments.Count - MaxSegments);
        return seg;
    }

    private void CloseCurrent() => Current?.Close();

    /// <summary>
    /// The step's log in order. A "hit" event is shown as its damage event (they come in the same order, one per hit), which has
    /// the damage types; "crit" is folded into the hit line; a hit without any damage part is not shown. DoT ticks are not shown (one per ailment and step: they would flood
    /// the window; the meter counts them). "no_resource" is not shown: in a live fight it only means a held key found its skill
    /// not usable yet (cooldown, resource), which a key press reports as a refusal. "proc_blocked" is engine bookkeeping.
    /// </summary>
    private void Log(IReadOnlyList<SimEvent> events, IReadOnlyList<DamageEvent> damage)
    {
        var hits = new Queue<DamageEvent>(damage.Where(d => !d.IsDot));
        foreach (var e in events)
        {
            switch (e.Kind)
            {
                case "hit":
                    // "fireball -> #0: 812". A hit with no damage part at all (Blink without damage nodes) has no damage event
                    // and is not shown: «попадание · 0» would be noise.
                    if (hits.Count > 0 && e.Text.StartsWith($"{hits.Peek().SkillId} -> #{hits.Peek().Target}:", StringComparison.Ordinal))
                        AddLog(_text.Line(hits.Dequeue()));
                    break;
                case "crit":
                case "no_resource":
                case "proc_blocked":
                    break;
                default:
                    AddLog(_text.Line(e));
                    break;
            }
        }
        while (hits.Count > 0) AddLog(_text.Line(hits.Dequeue()));
    }

    private void LogRefusal(int slot, PressResult r)
    {
        var skillId = _fight != null ? SkillIdAt(slot) : null;
        AddLog(_text.Refusal(_steps * LiveFight.StepSec, slot, skillId, r));
        Updated?.Invoke();
    }

    // The fight's build (the session may have moved on while detached); its slots are the fight's slots.
    private string? SkillIdAt(int slot) => FightSlot(slot) is var k and > 0 ? _build.Slots[k - 1].SkillId : null;

    /// <summary>The fight's slot of a player slot, 0 for an empty or out-of-range one (the fight refuses slot 0 as empty).</summary>
    private int FightSlot(int slot) => slot >= 1 && slot < _fightSlotOf.Length ? _fightSlotOf[slot] : 0;

    /// <summary>The session's filled slots in order are the build's slots (<see cref="SandboxSession.Build"/>): number them.</summary>
    private void MapSlots()
    {
        _fightSlotOf = new int[SandboxSession.SlotCount + 1];
        var playerSlotOf = new List<int> { 0 };
        for (var i = 0; i < SandboxSession.SlotCount; i++)
        {
            if (_session.GetSlot(i) == null) continue;
            playerSlotOf.Add(i + 1);
            _fightSlotOf[i + 1] = playerSlotOf.Count - 1;
        }
        _playerSlotOf = playerSlotOf.ToArray();
    }

    private void AddLog(string line)
    {
        _log.Add(line);
        if (_log.Count >= 2 * MaxLogLines) _log.RemoveRange(0, _log.Count - MaxLogLines); // in batches: amortized O(1)
    }

    /// <summary>The newest <see cref="MaxLogLines"/> lines of the log list (which holds up to twice as many), oldest first.</summary>
    private sealed class LogTail : IReadOnlyList<string>
    {
        private readonly List<string> _lines;

        public LogTail(List<string> lines) => _lines = lines;

        public int Count => Math.Min(_lines.Count, MaxLogLines);

        public string this[int index] =>
            (uint)index < (uint)Count ? _lines[_lines.Count - Count + index] : throw new ArgumentOutOfRangeException(nameof(index));

        public IEnumerator<string> GetEnumerator()
        {
            for (var i = _lines.Count - Count; i < _lines.Count; i++) yield return _lines[i];
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
