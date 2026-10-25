using Dialoid.Core.Combat;
using Dialoid.Core.Effects;
using Dialoid.Core.Simulation;
using Dialoid.Core.Skills;
using Dialoid.Core.Stats;

namespace Dialoid.Core.Mechanics;

// Cryomancer (Криомант) ascendancy (spec 10.4). The numbers are params with the spec values as defaults. Вечная мерзлота is
// chill_slow plus a conditional cold damage modifier; Северное сияние (freeze_chance +10 for spells, freeze_duration +50%)
// and Ледник (ground_duration 3 / ground_fraction 0.15 on the ice spear) are plain effects.

/// <summary>
/// Раскол. A hit on a frozen target deals more (+100)% More damage (damage More, tags hit, condition target_frozen: the hit
/// target) and removes the freeze after the hit, which starts the usual freeze immunity (spec 6.7), so that hit cannot
/// refreeze the target.
/// </summary>
public sealed class Shatter : IMechanic
{
    public string Id => "shatter";

    public IReadOnlyList<MechanicParam> ParamSchema { get; } = P.Of(P.Num("more"));

    /// <summary>Character-phase (or every-skill) mechanic: a scoped copy would do nothing.</summary>
    public MechanicScopes SupportedScopes => MechanicScopes.Global;

    public void OnCompile(MechanicCompileContext ctx)
    {
        if (ctx.Skill != null) return;
        ctx.AddCharacterModifier(ctx.Mod("damage", ModKind.More, ctx.Number("more", 100), tags: TagSet.Of(HitCalculator.HitTag),
            conditions: new Condition("target_frozen")));
    }

    public void OnHit(MechanicSimContext ctx)
    {
        if (!ctx.HasAilment(ctx.TargetIndex, "freeze")) return;
        ctx.RemoveAilment(ctx.TargetIndex, "freeze");
        ctx.Log($"freeze on #{ctx.TargetIndex} shattered");
    }
}

/// <summary>Ледяная броня. When a freeze from the character lands, the character gets armor (+200) armor (Flat) for duration (4) s
/// (a buff: a new freeze refreshes it, never stacks).</summary>
public sealed class IceArmor : IMechanic
{
    public string Id => "ice_armor";

    public IReadOnlyList<MechanicParam> ParamSchema { get; } = P.Of(P.Ailment(), P.Num("duration"), P.Num("armor"));

    public void OnAilmentApplied(MechanicSimContext ctx, AilmentInstance ailment)
    {
        if (ailment.AilmentId != ctx.Text("ailment", "freeze")) return;
        ctx.ApplyBuff("mechanic:" + Id, ctx.Number("duration", 4), new[] { ctx.Mod("armor", ModKind.Flat, ctx.Number("armor", 200)) });
    }
}

/// <summary>
/// Морозный шар, part 1 (spec 8.4): the ascendancy grants node <c>node</c> of skill <c>skill</c> for free and takes its
/// exclusive group. The rule lives in the skill compiler (<see cref="SkillCompiler.GrantSkillNodeId"/>), which reads the
/// params and never lands this mechanic on a skill; registering it lets data validate. It has no hooks of its own, so the
/// node is applied exactly once.
/// </summary>
public sealed class GrantSkillNode : IMechanic
{
    public string Id => SkillCompiler.GrantSkillNodeId;

    public IReadOnlyList<MechanicParam> ParamSchema { get; } =
        P.Of(P.Skill(required: true), new MechanicParam("node", MechanicParamKind.Text, Required: true));

    /// <summary>The declared params, then: the node must be a node of that skill's tree.</summary>
    public IEnumerable<string> Validate(MechanicEffect effect, Data.GameData data)
    {
        var errors = MechanicParamSchema.Validate(Id, ParamSchema, effect, data).ToList();
        if (errors.Count == 0 && data.Skills.TryGetValue(effect.Params["skill"].GetString()!, out var skill))
        {
            var node = effect.Params["node"].GetString()!;
            if (skill.Tree.All(n => n.Id != node))
                errors.Add($"механика «{Id}»: в дереве скилла «{skill.Id}» нет узла «{node}»");
        }
        return errors;
    }
}

/// <summary>
/// Морозный шар, part 2. For skill <c>skill</c> (fireball): its own modifiers on the chance stat of ailment <c>from</c>
/// (ignite) move to the chance stat of <c>to</c> (freeze), and its damage modifiers for <c>from</c> (stat damage, tag
/// ailment_ignite) become Increased/More of <c>duration_stat</c> (freeze_duration) at ratio (0.5): +1% ignite damage is
/// +0.5% freeze duration. Only the skill's own modifiers move (tree, scoped passive/ascendancy/keystone effects), not the
/// character pool. Freeze still obeys diminishing returns (spec 6.7).
/// </summary>
public sealed class AilmentSwap : IMechanic
{
    public string Id => "ailment_swap";

    public IReadOnlyList<MechanicParam> ParamSchema { get; } = P.Of(P.Skill(), P.Ailment("from"), P.Ailment("to"), P.Stat("duration_stat"), P.Num("ratio"));

    public void OnCompile(MechanicCompileContext ctx)
    {
        if (ctx.Skill is not { } skill) return;
        var scopeSkill = ctx.Effect.Scope is Scope.Skill s ? s.SkillId : "fireball";
        if (skill.SkillId != ctx.Text("skill", scopeSkill)) return;
        var from = Ailment(ctx, ctx.Text("from", "ignite"));
        var to = Ailment(ctx, ctx.Text("to", "freeze"));
        var durationStat = ctx.Text("duration_stat", "freeze_duration");
        var ratio = ctx.Number("ratio", 0.5);

        foreach (var m in skill.RemoveModifiers(m => m.Stat == from.ChanceStat))
            skill.AddModifier(m with { Stat = to.ChanceStat });
        foreach (var m in skill.RemoveModifiers(m => m.Stat == "damage" && m.RequiredTags.Contains(from.Tag)))
            if (m.Kind is ModKind.Increased or ModKind.More)
                skill.AddModifier(m with { Stat = durationStat, Value = m.Value * ratio, RequiredTags = TagSet.Of("skill:" + skill.SkillId) });
    }

    private Data.AilmentDefinition Ailment(MechanicCompileContext ctx, string id) =>
        ctx.Data.Ailments.TryGetValue(id, out var def) ? def : throw new ArgumentException($"Mechanic '{Id}': unknown ailment '{id}'.");
}

/// <summary>
/// Хрупкость. Crits on chilled targets deal more (+15)% More damage: damage More with tags hit and crit (crit-only, see
/// <see cref="HitCalculator.CritTag"/>) on condition target_chilled (the hit target).
/// </summary>
public sealed class Brittle : IMechanic
{
    public string Id => "brittle";

    public IReadOnlyList<MechanicParam> ParamSchema { get; } = P.Of(P.Num("more"));

    /// <summary>Character-phase (or every-skill) mechanic: a scoped copy would do nothing.</summary>
    public MechanicScopes SupportedScopes => MechanicScopes.Global;

    public void OnCompile(MechanicCompileContext ctx)
    {
        if (ctx.Skill != null) return;
        ctx.AddCharacterModifier(ctx.Mod("damage", ModKind.More, ctx.Number("more", 15),
            tags: TagSet.Of(HitCalculator.HitTag, HitCalculator.CritTag), conditions: new Condition("target_chilled")));
    }
}

/// <summary>Вечная мерзлота, slow part: chills applied by the character slow by slow (25)% instead of the default 15%
/// (the chill magnitude is a constant, so no plain effect can change it).</summary>
public sealed class ChillSlow : IMechanic
{
    public string Id => "chill_slow";

    public IReadOnlyList<MechanicParam> ParamSchema { get; } = P.Of(P.Num("slow"));

    public AilmentInstance OnAilmentApplying(MechanicSimContext ctx, AilmentInstance ailment) =>
        ailment.AilmentId == "chill" ? ailment with { Magnitude = ctx.Number("slow", 25) } : ailment;
}
