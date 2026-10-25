using Dialoid.Core.Character;
using Dialoid.Core.Combat;
using Dialoid.Core.Data;
using Dialoid.Core.Effects;
using Dialoid.Core.Stats;

namespace Dialoid.Core.Skills;

/// <summary>Sourced effects that reach slotted skills from outside their own tree. ClassId decides IsLoot (null: a skill without a class is loot).</summary>
public sealed record CompilationContext(GameData Data, IReadOnlyList<SourcedEffect> AscendancyEffects, IReadOnlyList<SourcedEffect> KeystoneEffects,
    IReadOnlyList<SourcedEffect> PassiveScopedEffects, IReadOnlyList<(string SkillId, string NodeId)> GrantedNodes, string? ClassId = null);

/// <summary>
/// Pure skill compilation (spec 4.6, 8.4). Effects are applied in a fixed order: skill tree nodes, passives,
/// ascendancy, keystones; for single-valued effects (damage type, behaviour, resource) the last one wins.
/// </summary>
public static class SkillCompiler
{
    public const string GrantSkillNodeId = "grant_skill_node";

    public static CompiledSkill Compile(SkillSlot slot, SkillDefinition def, CompilationContext ctx)
    {
        var (points, paid) = SkillNodePoints.Resolve(slot, def, ctx.GrantedNodes);
        var state = new SkillBuildState(def);

        foreach (var node in def.Tree)
        {
            if (!points.TryGetValue(node.Id, out var p) || p <= 0) continue;
            var src = new SourceRef("skill_node", node.Id, node.Name);
            foreach (var e in node.Effects)
                if (TreeEffectApplies(e, state.Tags))
                    state.Apply(e, p, src, e.Scope is Scope.Tags t ? t.Required.With("skill:" + def.Id) : TagSet.Of("skill:" + def.Id), stampSource: true);
        }

        // Tag scopes of outside effects are matched against the tags as the tree left them.
        var treeTags = state.Tags;
        ApplyOutside(state, def, treeTags, ctx.PassiveScopedEffects);
        ApplyOutside(state, def, treeTags, ctx.AscendancyEffects);
        ApplyOutside(state, def, treeTags, ctx.KeystoneEffects);

        bool native = ctx.ClassId != null && ctx.Data.Classes.TryGetValue(ctx.ClassId, out var cls)
            ? cls.SkillIds.Contains(def.Id)
            : def.ClassId != null;

        int spent = paid.Values.Where(v => v > 0).Sum();
        return new CompiledSkill(def.Id, slot.Level, !native, state.Tags, state.DamageType, def.Base, state.Behavior,
            state.Conversions, state.Cost, state.Gain, state.Modifiers, state.Procs, state.Mechanics, points)
        { FreePoints = slot.Level - spent, MechanicSources = state.MechanicSources.ToList() };
    }

    private static bool TreeEffectApplies(Effect e, TagSet tags) => e.Scope switch
    {
        Scope.Tags t => tags.ContainsAll(t.Required),
        _ => true,
    };

    private static void ApplyOutside(SkillBuildState state, SkillDefinition def, TagSet treeTags, IReadOnlyList<SourcedEffect> effects)
    {
        foreach (var (e, src) in effects)
        {
            switch (e.Scope)
            {
                case Scope.Skill s when s.SkillId == def.Id:
                    state.Apply(e, 1, src, TagSet.Of("skill:" + def.Id));
                    break;
                case Scope.Tags t when treeTags.ContainsAll(t.Required):
                    state.Apply(e, 1, src, t.Required);
                    break;
                case Scope.Global:
                    state.Apply(e, 1, src, TagSet.Empty);
                    break;
            }
        }
    }
}
