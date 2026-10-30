using Dialoid.Core.Combat;
using Dialoid.Core.Effects;
using Dialoid.Core.Simulation;
using Dialoid.Core.Stats;

namespace Dialoid.Core.Mechanics;

// Thunder (Громовержец) ascendancy (spec 10.4). The numbers are params with the spec values as defaults. Грозовое оружие,
// Цепной удар, Буря в жилах and Громовой топор are plain effects; Разряд is plain effects plus ground_type.

/// <summary>
/// Статический заряд. Each use whose hits dealt lightning damage gives one charge, at most max_charges (10). Triggered
/// copies give none (spec 8.4). With max_charges charges, the next spender use (tag spender, or any use that paid resource)
/// deals more (+100)% More damage (a buff on that skill's tag, removed at the end of the step) and resets the charges;
/// that use itself gives no charge.
/// </summary>
public sealed class StaticCharge : IMechanic
{
    public string Id => "static_charge";

    public IReadOnlyList<MechanicParam> ParamSchema { get; } = P.Of(P.Num("max_charges"), P.Num("more"));

    /// <summary>Character-phase (or every-skill) mechanic: a scoped copy would do nothing.</summary>
    public MechanicScopes SupportedScopes => MechanicScopes.Global;

    private string BuffId => "mechanic:" + Id;

    public void OnUse(MechanicSimContext ctx)
    {
        ctx.SetCounter("counted", 0);
        if (ctx.Counter("charges") < ctx.Number("max_charges", 10) - 1e-9) return;
        if (!ctx.SkillTags.Contains("spender") && ctx.ResourcePaid <= 0) return;
        var spent = ctx.Counter("charges");
        ctx.SetCounter("charges", 0);
        ctx.SetCounter("counted", 1);
        ctx.SetCounter("armed", 1);
        ctx.ApplyBuff(BuffId, double.PositiveInfinity,
            new[] { ctx.Mod("damage", ModKind.More, ctx.Number("more", 100), TagSet.Of("skill:" + ctx.SkillId)) });
        ctx.Log("charges spent", spent);
    }

    public void OnHit(MechanicSimContext ctx)
    {
        if (ctx.IsTriggeredCopy || ctx.Counter("counted") != 0 || ctx.Hit?.Final.GetValueOrDefault(DamageType.Lightning) is not > 0) return;
        ctx.SetCounter("counted", 1);
        ctx.SetCounter("charges", Math.Min(ctx.Number("max_charges", 10), ctx.Counter("charges") + 1));
    }

    public void OnTick(MechanicSimContext ctx, double dt)
    {
        if (ctx.Counter("armed") == 0) return;
        ctx.SetCounter("armed", 0);
        ctx.RemoveBuff(BuffId);
    }
}

/// <summary>Заземление. Every shock the character applies has at least min_effect (20)% effect and lasts duration (5) s.</summary>
public sealed class Grounding : IMechanic
{
    public string Id => "grounding";

    public IReadOnlyList<MechanicParam> ParamSchema { get; } = P.Of(P.Ailment(), P.Num("min_effect"), P.Num("duration"));

    public AilmentInstance OnAilmentApplying(MechanicSimContext ctx, AilmentInstance ailment) =>
        ailment.AilmentId != ctx.Text("ailment", "shock")
            ? ailment
            : ailment with { Magnitude = Math.Max(ailment.Magnitude, ctx.Number("min_effect", 20)), Remaining = ctx.Number("duration", 5) };
}

/// <summary>
/// The damage type <c>type</c> (physical, lightning, cold, fire, poison, dark) of the ground zone of the skills it reaches
/// (Разряд: "грозовая земля" is lightning even on a physical skill). Without it a zone takes the hit's largest share.
/// </summary>
public sealed class GroundType : IMechanic
{
    public string Id => "ground_type";

    public IReadOnlyList<MechanicParam> ParamSchema { get; } = P.Of(P.DamageType("type"));

    public void OnCompile(MechanicCompileContext ctx)
    {
        if (ctx.Skill is not { } skill) return;
        var name = ctx.Text("type", "lightning");
        skill.GroundDamageType = Enum.TryParse<DamageType>(name, ignoreCase: true, out var t) && Enum.IsDefined(t) && !int.TryParse(name, out _)
            ? t
            : throw new ArgumentException($"Mechanic '{Id}': unknown damage type '{name}'.");
    }
}
