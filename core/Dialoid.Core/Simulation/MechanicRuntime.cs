using Dialoid.Core.Character;
using Dialoid.Core.Combat;
using Dialoid.Core.Data;
using Dialoid.Core.Effects;
using Dialoid.Core.Stats;

namespace Dialoid.Core.Simulation;

/// <summary>
/// Fires the simulator hooks of <see cref="IMechanic"/>: Global-scope mechanics from the character (every event), scoped
/// ones from the event skill's <c>CompiledSkill.Mechanics</c> (only that skill's events). Ids missing from the registry are skipped.
/// </summary>
internal sealed class MechanicRuntime
{
    private readonly MechanicRegistry _registry;
    private readonly IReadOnlyList<(IMechanic M, MechanicEffect E, SourceRef S)> _global;
    private readonly IReadOnlyList<SkillRuntime> _slotted;
    private readonly SimState _s;
    private readonly ProcEngine _procs;
    private readonly SimConditionContext _conditions;
    private readonly SimLog _log;
    private readonly GameData _data;

    public MechanicRuntime(MechanicRegistry registry, CompiledCharacter character, IReadOnlyList<SkillRuntime> slotted, SimState s,
        ProcEngine procs, SimConditionContext conditions, SimLog log, GameData data)
    {
        _registry = registry;
        _slotted = slotted;
        _s = s;
        _procs = procs;
        _conditions = conditions;
        _log = log;
        _data = data;
        var global = new List<(IMechanic, MechanicEffect, SourceRef)>();
        foreach (var (e, src) in character.GlobalMechanics)
            if (e is MechanicEffect me && registry.TryGet(me.MechanicId, out var m)) global.Add((m, me, src));
        _global = global;
    }

    public void Use(SkillRuntime skill, double resourcePaid)
    {
        foreach (var (m, e, src) in For(skill))
            m.OnUse(Ctx(e, src, skill, skill.IsTriggeredCopy, resourcePaid: resourcePaid));
    }

    public void Hit(SkillRuntime skill, int target, HitResult hit, bool firstOnPrimary)
    {
        foreach (var (m, e, src) in For(skill))
            m.OnHit(Ctx(e, src, skill, skill.IsTriggeredCopy, target, hit, firstOnPrimary));
    }

    public void Kill(int target, SkillRuntime? source, bool triggered)
    {
        foreach (var (m, e, src) in For(source))
            m.OnKill(Ctx(e, src, source, triggered, target));
    }

    /// <summary>Global mechanics tick once; a scoped mechanic ticks once per slotted skill carrying it.</summary>
    public void Tick(double dt)
    {
        foreach (var (m, e, src) in _global) m.OnTick(Ctx(e, src, null, false), dt);
        foreach (var skill in _slotted)
            foreach (var (m, e, src) in Scoped(skill))
                m.OnTick(Ctx(e, src, skill, false), dt);
    }

    private IEnumerable<(IMechanic M, MechanicEffect E, SourceRef S)> For(SkillRuntime? skill) =>
        skill == null ? _global : _global.Concat(Scoped(skill));

    private IEnumerable<(IMechanic M, MechanicEffect E, SourceRef S)> Scoped(SkillRuntime skill)
    {
        foreach (var me in skill.Skill.Mechanics)
            if (_registry.TryGet(me.MechanicId, out var m))
                yield return (m, me, new SourceRef("mechanic", me.MechanicId, me.MechanicId));
    }

    private MechanicSimContext Ctx(MechanicEffect e, SourceRef src, SkillRuntime? skill, bool triggered, int target = -1,
        HitResult? hit = null, bool firstOnPrimary = false, double resourcePaid = 0) =>
        new(_s, _procs, _conditions, _log, _data, e, src, skill, triggered, target, hit, firstOnPrimary, resourcePaid);
}
