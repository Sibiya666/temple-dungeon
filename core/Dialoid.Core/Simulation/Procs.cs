using Dialoid.Core.Character;
using Dialoid.Core.Combat;
using Dialoid.Core.Data;
using Dialoid.Core.Effects;
using Dialoid.Core.Skills;
using Dialoid.Core.Stats;

namespace Dialoid.Core.Simulation;

/// <summary>
/// Runs the procs granted by skill trees, passives and the ascendancy (spec 8.4). Counters keep one value per
/// (skill, proc index). Casts of triggered copies are queued and run in <see cref="Flush"/>, never inside a hit loop.
/// A triggered copy never fires, and never counts for, any proc (runtime loop guard).
/// </summary>
internal sealed class ProcEngine
{
    private readonly GameData _data;
    private readonly CompiledCharacter _character;
    private readonly Dictionary<string, SkillRuntime> _skills; // slotted skills; also registers copies so ground zones can find them
    private readonly HashSet<string> _slotted;
    private readonly SimState _s;
    private readonly UseResolver _resolver;
    private readonly SimLog _log;
    private readonly WeaponStub? _weapon;

    private readonly Dictionary<(string Skill, int Index), int> _counters = new();
    private readonly SortedDictionary<string, IReadOnlyList<Modifier>> _buffMods = new(StringComparer.Ordinal);
    private readonly Queue<(SkillRuntime Source, ProcAction.CastSkill Cast)> _casts = new();
    private readonly List<(SkillRuntime Source, string AilmentId)> _useAilments = new();
    private readonly Dictionary<string, IReadOnlyDictionary<DamageType, double>> _lastBase = new(StringComparer.Ordinal);
    private readonly (string Skill, bool Triggered)?[] _lastSource;
    private readonly Dictionary<(string Skill, int Level, double Percent), SkillRuntime> _copies = new();
    private readonly CompilationContext _compileContext;
    private bool _castingTriggered;

    public ProcEngine(GameData data, CompiledCharacter character, Dictionary<string, SkillRuntime> skills, SimState s,
        UseResolver resolver, SimLog log, WeaponStub? weapon)
    {
        _weapon = weapon;
        _data = data;
        _character = character;
        _skills = skills;
        _slotted = skills.Keys.ToHashSet(StringComparer.Ordinal);
        _s = s;
        _resolver = resolver;
        _log = log;
        _lastSource = new (string, bool)?[s.Targets.Count];

        IReadOnlyList<SourcedEffect> Of(string kind) => character.DeferredEffects.Where(d => d.Source.Kind == kind).ToList();
        _compileContext = new CompilationContext(data, Of("ascendancy"), Of("keystone"), Of("passive"),
            Array.Empty<(string, string)>(), character.Class.Id);
    }

    /// <summary>The character evaluator with the active buffs, for resource regeneration and the like.</summary>
    public StatEvaluator Evaluator => _buffMods.Count == 0 ? _character.Evaluator : _character.Evaluator.WithExtra(ActiveBuffMods());

    // ---- Hooks ----

    /// <summary>One use of a rotation skill (a channel tick counts as one): on_use and every_n_uses.</summary>
    public void OnUse(SkillRuntime skill)
    {
        if (skill.IsTriggeredCopy) return;
        var procs = skill.Skill.Procs;
        for (var i = 0; i < procs.Count; i++)
        {
            switch (procs[i].Trigger)
            {
                case Trigger.OnUse:
                    Fire(skill, procs[i], null, -1);
                    break;
                case Trigger.EveryNUses n when Tick(skill, i) % n.N == 0:
                    Fire(skill, procs[i], null, -1);
                    break;
            }
        }
    }

    /// <summary>One hit on one target: every_n_hits counts it; on_hit fires for the first hit on the primary target of a use.</summary>
    public void OnHit(SkillRuntime skill, int targetIndex, HitResult hit, bool firstOnPrimary)
    {
        _lastSource[targetIndex] = (skill.Id, skill.IsTriggeredCopy);
        if (skill.IsTriggeredCopy) return;
        if (firstOnPrimary) _lastBase[skill.Id] = hit.AilmentBase;

        var procs = skill.Skill.Procs;
        for (var i = 0; i < procs.Count; i++)
        {
            switch (procs[i].Trigger)
            {
                case Trigger.OnHit when firstOnPrimary:
                    Fire(skill, procs[i], hit.AilmentBase, targetIndex);
                    break;
                case Trigger.EveryNHits n when Tick(skill, i) % n.N == 0:
                    Fire(skill, procs[i], hit.AilmentBase, targetIndex);
                    break;
            }
        }

        if (firstOnPrimary && _useAilments.Count > 0)
        {
            foreach (var (source, ailmentId) in _useAilments) ApplyAilment(source, ailmentId, hit.AilmentBase, -1);
            _useAilments.Clear();
        }
    }

    /// <summary>Crit event of a use on the primary target (Average mode: the chance accumulator already decided).</summary>
    public void OnCrit(SkillRuntime skill)
    {
        if (skill.IsTriggeredCopy) return;
        var procs = skill.Skill.Procs;
        for (var i = 0; i < procs.Count; i++)
            if (procs[i].Trigger is Trigger.OnCrit) Fire(skill, procs[i], _lastBase.GetValueOrDefault(skill.Id), _s.PrimaryIndex);
    }

    /// <summary>A target died; the kill belongs to the last damage source. Triggered copies fire nothing.</summary>
    public void OnKill(int targetIndex)
    {
        var src = _lastSource[targetIndex];
        _lastSource[targetIndex] = null;
        if (src is not { Triggered: false } s || !_slotted.Contains(s.Skill)) return;
        var skill = _skills[s.Skill];
        var procs = skill.Skill.Procs;
        for (var i = 0; i < procs.Count; i++)
            if (procs[i].Trigger is Trigger.OnKill) Fire(skill, procs[i], _lastBase.GetValueOrDefault(skill.Id), targetIndex);
    }

    /// <summary>Damage over time or a zone tick hurt a target: remember the source for kill attribution.</summary>
    public void NoteDamage(int targetIndex, string skillId) => _lastSource[targetIndex] = (skillId, false);

    /// <summary>Runs the queued triggered casts. Hooks stay silent while a copy is being cast.</summary>
    public void Flush()
    {
        while (_casts.Count > 0)
        {
            var (source, cast) = _casts.Dequeue();
            if (_s.PrimaryIndex < 0) { _casts.Clear(); return; }
            var copy = CopyOf(source, cast);
            if (copy == null || !copy.WeaponFits(_weapon)) continue;
            if (copy.Skill.Procs.Count > 0)
                _log.Add(_s.Time, "proc_blocked", $"{copy.Id}: triggered copy fires no procs");
            _castingTriggered = true;
            try { _resolver.Apply(copy); }
            finally { _castingTriggered = false; }
        }
    }

    // ---- Buffs ----

    /// <summary>Called after buffs expired or were applied: pushes the active buff modifiers into every skill evaluator.</summary>
    public void RefreshBuffs()
    {
        foreach (var id in _buffMods.Keys.ToList())
            if (!_s.Buffs.TryGetValue(id, out var left) || left <= 0) _buffMods.Remove(id);
        var mods = ActiveBuffMods();
        foreach (var skill in _skills.Values) skill.SetBuffs(mods);
        foreach (var copy in _copies.Values) copy.SetBuffs(mods);
    }

    private IReadOnlyList<Modifier> ActiveBuffMods() => _buffMods.Values.SelectMany(m => m).ToList();

    // ---- Firing ----

    private int Tick(SkillRuntime skill, int index)
    {
        var key = (skill.Id, index);
        return _counters[key] = _counters.GetValueOrDefault(key) + 1;
    }

    private void Fire(SkillRuntime source, GrantProcEffect proc, IReadOnlyDictionary<DamageType, double>? hitBase, int hitTarget)
    {
        if (_castingTriggered) return; // runtime guard: never reached through the hooks, kept as a hard stop
        Log(source, proc);
        switch (proc.Action)
        {
            case ProcAction.ApplyBuff b:
                _s.Buffs[b.BuffId] = b.DurationSec; // re-applying refreshes, never stacks
                _buffMods[b.BuffId] = b.Mods;
                RefreshBuffs();
                break;
            case ProcAction.ApplyAilment a:
                if (proc.Trigger is Trigger.OnUse or Trigger.EveryNUses) _useAilments.Add((source, a.AilmentId)); // waits for this use's hit
                else ApplyAilment(source, a.AilmentId, hitBase, proc.Trigger is Trigger.OnKill ? hitTarget : -1);
                break;
            case ProcAction.CastSkill c:
                _casts.Enqueue((source, c));
                break;
        }
    }

    /// <summary>Applies the ailment to the primary target; for a kill the dying target (<paramref name="exclude"/>) is passed over.</summary>
    private void ApplyAilment(SkillRuntime source, string ailmentId, IReadOnlyDictionary<DamageType, double>? hitBase, int exclude)
    {
        if (!_data.Ailments.TryGetValue(ailmentId, out var def)) return;
        var ti = -1;
        for (var i = 0; i < _s.Targets.Count; i++)
            if (i != exclude && _s.IsAlive(i)) { ti = i; break; }
        if (ti < 0) return;
        _resolver.ForceAilment(source, ti, def, hitBase ?? _lastBase.GetValueOrDefault(source.Id));
    }

    private void Log(SkillRuntime source, GrantProcEffect proc)
    {
        var trigger = proc.Trigger switch
        {
            Trigger.OnUse => "on_use",
            Trigger.OnHit => "on_hit",
            Trigger.OnCrit => "on_crit",
            Trigger.OnKill => "on_kill",
            Trigger.EveryNUses n => $"every_n_uses({n.N})",
            Trigger.EveryNHits n => $"every_n_hits({n.N})",
            _ => "?",
        };
        var action = proc.Action switch
        {
            ProcAction.ApplyBuff b => $"apply_buff {b.BuffId}",
            ProcAction.ApplyAilment a => $"apply_ailment {a.AilmentId}",
            ProcAction.CastSkill c => $"cast_skill {c.SkillId}",
            _ => "?",
        };
        _log.Add(_s.Time, "proc", $"{source.Id}: {trigger} -> {action}");
    }

    // ---- Triggered copies ----

    private SkillRuntime? CopyOf(SkillRuntime source, ProcAction.CastSkill cast)
    {
        var slotted = _slotted.Contains(cast.SkillId);
        var key = (cast.SkillId, slotted ? -1 : source.Skill.Level, cast.DamagePercent);
        if (_copies.TryGetValue(key, out var cached)) return cached;

        CompiledSkill baseSkill;
        if (slotted) baseSkill = _skills[cast.SkillId].Skill;
        else
        {
            if (!_data.Skills.TryGetValue(cast.SkillId, out var def)) return null;
            baseSkill = SkillCompiler.Compile(new SkillSlot { SkillId = cast.SkillId, Level = source.Skill.Level }, def, _compileContext);
        }

        var mods = baseSkill.Modifiers.ToList();
        if (Math.Abs(cast.DamagePercent - 100) > 1e-9)
            mods.Add(new Modifier("damage", ModKind.More, Math.Max(0, cast.DamagePercent) - 100,
                TagSet.Of("skill:" + cast.SkillId, "triggered"), Array.Empty<Condition>(), null,
                new SourceRef("mechanic", "triggered_copy", $"Копия по проку: {cast.DamagePercent}% урона")));
        var compiled = baseSkill with { Tags = baseSkill.Tags.With("triggered"), Modifiers = mods };
        var copy = new SkillRuntime(_data, _character, compiled, isTriggeredCopy: true);
        copy.SetBuffs(ActiveBuffMods());
        _copies[key] = copy;
        if (!slotted) _skills.TryAdd(cast.SkillId, copy); // lets ground zones of the copy find their runtime
        return copy;
    }
}
