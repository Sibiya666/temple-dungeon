using System.Text.Json;
using Dialoid.Core.Combat;
using Dialoid.Core.Stats;

namespace Dialoid.Core.Effects;

public enum ResourceField { Cost, Gain }

public abstract record Trigger
{
    public sealed record OnUse : Trigger;
    public sealed record OnHit : Trigger;
    public sealed record OnCrit : Trigger;
    public sealed record OnKill : Trigger;
    public sealed record EveryNUses(int N) : Trigger;
    public sealed record EveryNHits(int N) : Trigger;
}

public abstract record ProcAction
{
    public sealed record ApplyBuff(string BuffId, double DurationSec, IReadOnlyList<Modifier> Mods) : ProcAction;
    public sealed record ApplyAilment(string AilmentId) : ProcAction;
    public sealed record CastSkill(string SkillId, double DamagePercent) : ProcAction;
}

/// <summary>One data-driven change made by a node. PerPoint: the effect is multiplied by invested points.</summary>
public abstract record Effect(Scope Scope, bool PerPoint);

public sealed record ModifierEffect(Modifier Modifier, Scope Scope, bool PerPoint = true) : Effect(Scope, PerPoint);
public sealed record AddTagEffect(string Tag, Scope Scope) : Effect(Scope, false);
public sealed record RemoveTagEffect(string Tag, Scope Scope) : Effect(Scope, false);
public sealed record SetBehaviorEffect(string Param, double Value, Scope Scope) : Effect(Scope, false);
public sealed record AddBehaviorEffect(string Param, double Delta, Scope Scope, bool PerPoint = true) : Effect(Scope, PerPoint);
public sealed record AddConversionEffect(DamageType From, DamageType To, double Percent, Scope Scope, bool PerPoint = true) : Effect(Scope, PerPoint);
public sealed record SetDamageTypeEffect(DamageType To, Scope Scope) : Effect(Scope, false);
public sealed record SetResourceEffect(ResourceField Field, double Value, Scope Scope) : Effect(Scope, false);
public sealed record GrantProcEffect(Trigger Trigger, ProcAction Action, Scope Scope) : Effect(Scope, false);
public sealed record MechanicEffect(string MechanicId, IReadOnlyDictionary<string, JsonElement> Params, Scope Scope) : Effect(Scope, false);
