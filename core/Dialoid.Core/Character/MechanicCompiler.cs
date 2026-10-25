using Dialoid.Core.Data;
using Dialoid.Core.Effects;
using Dialoid.Core.Skills;
using Dialoid.Core.Stats;

namespace Dialoid.Core.Character;

/// <summary>
/// Runs <see cref="IMechanic.OnCompile"/>. Ids missing from the registry are skipped: data is validated against the
/// registry it was loaded with, so a miss only happens when a caller compiles with a different registry.
/// </summary>
internal static class MechanicCompiler
{
    /// <summary>Global-scope mechanics in hook order: non-keystone sources first, keystones last (same order as skill effects).</summary>
    public static IReadOnlyList<SourcedEffect> Order(IEnumerable<SourcedEffect> mechanics)
    {
        var global = mechanics.Where(m => m.Effect.Scope is Scope.Global).ToList();
        return global.Where(m => m.Source.Kind != "keystone").Concat(global.Where(m => m.Source.Kind == "keystone")).ToList();
    }

    public static void CharacterPhase(MechanicRegistry registry, GameData data, Build build, ClassDefinition cls,
        IReadOnlyList<SourcedEffect> globals, List<Modifier> mods, Dictionary<string, double> rules)
    {
        foreach (var (e, src) in globals)
            if (e is MechanicEffect me && registry.TryGet(me.MechanicId, out var m))
                m.OnCompile(new MechanicCompileContext(data, build, cls, me, src, mods, rules, null));
    }

    /// <summary>Global mechanics, then the skill's own (scoped) mechanics, edit one compiled skill.</summary>
    public static CompiledSkill SkillPhase(MechanicRegistry registry, GameData data, Build build, ClassDefinition cls,
        IReadOnlyList<SourcedEffect> globals, StatEvaluator characterEvaluator, CompiledSkill skill)
    {
        var calls = new List<(IMechanic M, MechanicEffect E, SourceRef S)>();
        foreach (var (e, src) in globals)
            if (e is MechanicEffect me && registry.TryGet(me.MechanicId, out var m)) calls.Add((m, me, src));
        foreach (var me in skill.Mechanics)
            if (registry.TryGet(me.MechanicId, out var m)) calls.Add((m, me, new SourceRef("mechanic", me.MechanicId, me.MechanicId)));
        if (calls.Count == 0) return skill;

        var draft = new MechanicSkillDraft(skill, characterEvaluator, data);
        foreach (var (m, me, src) in calls)
            m.OnCompile(new MechanicCompileContext(data, build, cls, me, src, null, null, draft));
        return draft.ToCompiled();
    }

    public static CompiledSkill SkillPhase(MechanicRegistry registry, GameData data, CompiledCharacter ch, CompiledSkill skill) =>
        SkillPhase(registry, data, ch.Build, ch.Class, ch.GlobalMechanics, ch.Evaluator, skill);
}
