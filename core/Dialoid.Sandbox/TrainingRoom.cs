using System.Collections.Immutable;
using Dialoid.Core.Character;
using Dialoid.Core.Effects;
using Dialoid.Core.Simulation;
using Dialoid.Core.Stats;

namespace Dialoid.Sandbox;

/// <summary>
/// The training room without the 3D scene (spec 6–8): a <see cref="LiveFight"/> of the session's build against a dummy preset,
/// the reach rule (<see cref="ReachRules"/>), fight segments for the meter, the Russian combat log (<see cref="Log"/>) and the
/// scene's event feed (<see cref="Feed"/>). Single-threaded: call everything on the session's thread. The game layer calls
/// <see cref="Advance"/> every frame and sends key presses with the hero's distance to the dummy (meters, from the edge of the
/// nearest figure) and whether the dummy is on the screen.
/// <para>A build edit (<see cref="SandboxSession.Changed"/>, after <see cref="Attach"/>) restarts the fight with the new build
/// (spec 6.2b): the current segment closes, held and queued keys are gone, and the log says so. Changing the dummy or the mode
/// restarts it too (and closes the segment the same way). An illegal build or an unknown dummy starts no fight
/// (<see cref="Running"/> is false, <see cref="Violations"/> say why) until the next edit.</para>
/// <para>Slots are the player's (1-based, keys 1–5, as the session numbers them, gaps included): key N presses the skill in
/// session slot N even when an earlier slot is empty. The fight's build lists only the filled slots, so the room translates both
/// ways: everything it exposes (<see cref="Slots"/>, <see cref="State"/>, <see cref="Feed"/>) is in player slots.</para>
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

    /// <summary>Log line kinds the log window hides until the player turns them on (spec 7: DoT ticks).</summary>
    public static IReadOnlySet<LogKind> DefaultHiddenKinds { get; } = ImmutableHashSet.Create(LogKind.DotTick);

    private const double Eps = 1e-9;

    private readonly SandboxSession _session;
    private readonly CombatLogText _text;
    private readonly CombatLog _log;
    private readonly List<FightSegment> _segments = new();
    private LiveFight? _fight;
    private Build _build = new();
    // Player slot (index, 1-based) → the fight's slot (1-based among the filled ones), 0 for an empty slot; and back.
    // Taken together with the fight's build at the restart.
    private int[] _fightSlotOf = Array.Empty<int>();
    private int[] _playerSlotOf = Array.Empty<int>();
    private Dictionary<string, int> _slotOfSkill = new(StringComparer.Ordinal);
    private IReadOnlyList<SlotInfo?> _slots = Array.Empty<SlotInfo?>();
    private IReadOnlyList<RuleViolation> _violations = Array.Empty<RuleViolation>();
    private LiveState? _stateSource, _state; // the fight's state and its player-slot copy
    private string? _resourceId;
    private int _revision;
    private long _steps;
    private double _pendingDt;
    private int _nextIndex = 1;
    private bool _attached;
    private bool _afterBuildChange; // the next segment is the first after a build edit

    /// <summary>Starts a fight with the session's current build against the training dummy in roll mode, seed 1 (spec 6.2: real
    /// crits and ailment rolls; <see cref="SetMode"/> switches to average). Does not follow build edits until <see cref="Attach"/>.</summary>
    public TrainingRoom(SandboxSession session)
    {
        _session = session;
        _text = new CombatLogText(session.Data);
        _log = new CombatLog(_text, MaxLogLines);
        Restart(null);
    }

    public string DummyId { get; private set; } = DefaultDummyId;
    public SimMode Mode { get; private set; } = SimMode.Roll;
    public int Seed { get; private set; } = 1;

    /// <summary>A fight is going on (false for an illegal build or an unknown dummy).</summary>
    public bool Running => _fight != null;

    /// <summary>Why no fight is going on; empty while <see cref="Running"/>.</summary>
    public IReadOnlyList<RuleViolation> Violations => _violations;

    /// <summary>The scene's events (uses, hits, ailments, refusals), drained every frame.</summary>
    public RoomFeed Feed { get; } = new();

    /// <summary>The skill bar: <see cref="SandboxSession.SlotCount"/> entries for player slots 1–5 (index 0 is slot 1), null for an
    /// empty slot; <see cref="SlotInfo.Slot"/> is the player slot. All null without a fight.</summary>
    public IReadOnlyList<SlotInfo?> Slots => _slots;

    /// <summary>The class resource's Russian name for the bar («Мана», «Ярость»); empty without a fight.</summary>
    public string ResourceName { get; private set; } = "";

    /// <summary>What the HUD shows (cooldowns, queued and held keys by player slot, costs and resource-short slots by player slot, resource, buffs); null without a fight. The same
    /// object until the next step or command, like <see cref="LiveFight.State"/>.</summary>
    public LiveState? State
    {
        get
        {
            if (_fight?.State is not { } s) return null;
            if (ReferenceEquals(s, _stateSource)) return _state;
            var cooldowns = new SortedDictionary<int, double>();
            foreach (var (fightSlot, left) in s.CooldownLeftBySlot)
                if (PlayerSlot(fightSlot) is { } slot) cooldowns[slot] = left;
            var costs = new SortedDictionary<int, double>();
            foreach (var (fightSlot, cost) in s.CostBySlot)
                if (PlayerSlot(fightSlot) is { } slot) costs[slot] = cost;
            var shortSlots = s.ResourceShortSlots.Select(f => PlayerSlot(f)).OfType<int>().Order().ToList();
            _stateSource = s;
            return _state = s with
            {
                CooldownLeftBySlot = cooldowns, QueuedSlot = PlayerSlot(s.QueuedSlot), HeldSlot = PlayerSlot(s.HeldSlot),
                CostBySlot = costs, ResourceShortSlots = shortSlots,
            };
        }
    }

    /// <summary>Kept segments, oldest first, at most <see cref="MaxSegments"/>; the last one may be <see cref="Current"/>.</summary>
    public IReadOnlyList<FightSegment> Segments => _segments;

    /// <summary>The segment collecting damage now, or null between fights.</summary>
    public FightSegment? Current => _segments.Count > 0 && _segments[^1].IsOpen ? _segments[^1] : null;

    /// <summary>The latest combat log lines, oldest first, at most <see cref="MaxLogLines"/> (see <see cref="CombatLog"/> for what is
    /// shown and how lines are joined, collapsed and aggregated).</summary>
    public IReadOnlyList<CombatLogEntry> Log => _log.Entries;

    /// <summary>The texts of <see cref="Log"/>.</summary>
    public IReadOnlyList<string> LogLines => _log.Lines;

    /// <summary>Raised after anything shown changed: steps made, a restart, a logged refusal, a meter reset.</summary>
    public event Action? Updated;

    /// <summary>The lines of <see cref="Log"/> of the given kinds (the shown ones; by default every kind but
    /// <see cref="DefaultHiddenKinds"/>) and, when <paramref name="skillId"/> is set, of that skill only.</summary>
    public IReadOnlyList<CombatLogEntry> Filter(ISet<LogKind> kinds, string? skillId) => _log.Filter(kinds, skillId);

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

    /// <summary>Choose a dummy preset; restarts the fight and closes the segment (spec 6.4).</summary>
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

    /// <summary>A key press on a slot (1-based): the reach rule first (<see cref="PressRefusal.OutOfReach"/>,
    /// <see cref="PressRefusal.TargetNotVisible"/>), then the fight's own checks. Every refusal is logged and sent to the feed.</summary>
    public PressResult Press(int slot, double distanceMeters, bool targetOnScreen)
    {
        UpdateReach(distanceMeters, targetOnScreen);
        var r = Reach(slot, distanceMeters, targetOnScreen) ?? _fight!.Press(FightSlot(slot));
        if (!r.Accepted) Refused(slot, r);
        return r;
    }

    /// <summary>
    /// Holds a slot's key (channel or repeat), after the reach rule. Returns the refusal (logged; the same refusal again, as when
    /// the game layer retries every frame, adds to the previous line's count) when there is no fight, the dummy is out of reach
    /// or not visible, or the slot cannot be held (empty, triggered-only, wrong weapon: <see cref="LiveFight.HoldRefusal"/>);
    /// a refused hold leaves the previous one as it was. Cooldown and resource are not refusals: the held key waits for them.
    /// The hold stays under the reach rule: see <see cref="UpdateReach"/>.
    /// </summary>
    public PressResult StartChannel(int slot, double distanceMeters, bool targetOnScreen)
    {
        UpdateReach(distanceMeters, targetOnScreen);
        var refused = Reach(slot, distanceMeters, targetOnScreen)
            ?? (_fight!.HoldRefusal(FightSlot(slot)) is var why and not PressRefusal.None ? new PressResult(false, false, why, null) : null);
        if (refused != null)
        {
            Refused(slot, refused);
            return refused;
        }
        _fight!.StartChannel(FightSlot(slot));
        return new PressResult(true, false, PressRefusal.None, null);
    }

    /// <summary>Releases the held key.</summary>
    public void StopChannel() => _fight?.StopChannel();

    /// <summary>
    /// Where the hero is now relative to the dummy (the game layer reports it every frame, or whenever it changes; presses report it
    /// too). A held key whose skill is no longer usable from there (Whirlwind once the hero walks past
    /// <see cref="ReachRules.MeleeReachMeters"/>, Meteor once the dummy leaves the screen) is released, with one refusal in the log.
    /// The held key is the fight's own (<see cref="LiveState.HeldSlot"/>), so a hold the fight dropped is never released twice.
    /// </summary>
    public void UpdateReach(double distanceMeters, bool targetOnScreen)
    {
        if (State?.HeldSlot is not { } slot || Reach(slot, distanceMeters, targetOnScreen) is not { } refused) return;
        StopChannel();
        Refused(slot, refused);
    }

    /// <summary>The meter's «Сбросить»: closes the current segment and forgets every segment; the next damage starts a new one.
    /// The log stays.</summary>
    public void ResetMeter()
    {
        CloseCurrent();
        _segments.Clear();
        Updated?.Invoke();
    }

    private void OnSessionChanged()
    {
        if (_session.Revision == _revision) return;
        _afterBuildChange = true;
        Restart(Texts.BuildChangedRestart);
    }

    // Null when the reach rule lets the press through to the fight.
    private PressResult? Reach(int slot, double distanceMeters, bool targetOnScreen)
    {
        if (_fight == null) return new PressResult(false, false, PressRefusal.FightOver, null);
        if (CompiledTags(slot) is not { } tags) return null; // empty slot (player slot, mapped): the fight refuses it
        var why = ReachRules.Check(tags, distanceMeters, targetOnScreen);
        return why == PressRefusal.None ? null : new PressResult(false, false, why, null);
    }

    private void Restart(string? line)
    {
        CloseCurrent();
        _revision = _session.Revision;
        _steps = 0;
        _pendingDt = 0;
        _build = _session.Build; // held and queued keys die with the old fight
        _fight = LiveFight.Start(_session.Data, MechanicRegistry.Default, _build,
            new LiveFightConfig(DummyId, Mode, Seed), out var violations);
        _violations = _fight == null ? violations : Array.Empty<RuleViolation>();
        MapSlots();
        var cls = _session.Data.Classes.GetValueOrDefault(_build.ClassId);
        _resourceId = cls?.Resource.Id;
        ResourceName = _fight != null && cls != null ? cls.Resource.Name : "";
        _log.Restart(showTargets: _fight?.State.TargetLife.Count > 1);
        Feed.Clear();
        if (line != null) _log.System(0, line);
        if (_fight == null) _log.System(0, Texts.FightNotStarted(string.Join("; ", violations.Select(v => v.Message))));
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
        var current = Current;
        if (current != null)
            foreach (var e in events) current.Add(e);
        _log.Step(current, now, events, damage);
        Feed.Step(events, damage, SlotOfSkill);

        if (current != null && now - current.End >= SegmentIdleSec - Eps) Close(current);
    }

    private FightSegment Open(double start)
    {
        var previous = _segments.Count > 0 ? _segments[^1].Dps : (double?)null;
        var seg = new FightSegment(_nextIndex++, start, _text, DummyId, _afterBuildChange, previous);
        _afterBuildChange = false;
        _segments.Add(seg);
        if (_segments.Count > MaxSegments) _segments.RemoveRange(0, _segments.Count - MaxSegments);
        return seg;
    }

    private void CloseCurrent()
    {
        if (Current is { } open) Close(open);
    }

    private void Close(FightSegment segment)
    {
        segment.Close();
        _log.SegmentClosed(segment);
    }

    private void Refused(int slot, PressResult r)
    {
        var skillId = _fight != null ? SkillIdAt(slot) : null;
        var time = _steps * LiveFight.StepSec;
        _log.Refusal(Current, time, slot, skillId, r);
        Feed.Push(new VisualEvent(VisualKind.Refused, time, slot, skillId, null)
            { Refusal = r.Refusal, RefusalText = CombatLogText.RefusalPopup(r, _resourceId) });
        Updated?.Invoke();
    }

    // The fight's build (the session may have moved on while detached); its slots are the fight's slots.
    private string? SkillIdAt(int slot) => FightSlot(slot) is var k and > 0 ? _build.Slots[k - 1].SkillId : null;

    private int? SlotOfSkill(string skillId) => _slotOfSkill.TryGetValue(skillId, out var slot) ? slot : null;

    /// <summary>The fight's slot of a player slot, 0 for an empty or out-of-range one (the fight refuses slot 0 as empty).</summary>
    private int FightSlot(int slot) => slot >= 1 && slot < _fightSlotOf.Length ? _fightSlotOf[slot] : 0;

    /// <summary>The player slot of a fight slot (1-based), null for none.</summary>
    private int? PlayerSlot(int? fightSlot) =>
        fightSlot is { } k && k >= 1 && k < _playerSlotOf.Length ? _playerSlotOf[k] : null;

    /// <summary>The session's filled slots in order are the build's slots (<see cref="SandboxSession.Build"/>): number them, and
    /// lay the fight's slot infos out by player slot.</summary>
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

        var slots = new SlotInfo?[SandboxSession.SlotCount];
        _slotOfSkill = new Dictionary<string, int>(StringComparer.Ordinal);
        if (_fight != null)
            foreach (var info in _fight.Slots)
                if (PlayerSlot(info.Slot) is { } player)
                {
                    slots[player - 1] = info with { Slot = player };
                    _slotOfSkill.TryAdd(info.SkillId, player);
                }
        _slots = slots;
    }
}
