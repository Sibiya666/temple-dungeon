using Dialoid.Core.Character;
using Dialoid.Core.Combat;
using Dialoid.Core.Data;
using Dialoid.Core.Skills;
using Dialoid.Core.Stats;

namespace Dialoid.Core.Simulation;

/// <summary>
/// A slotted skill as the simulator sees it: the character evaluator extended with the skill's own modifiers, plus the
/// skill's timing, resource and coverage numbers. Values that may depend on conditions are evaluated per use.
/// </summary>
internal sealed class SkillRuntime
{
    private const double MinMultiplier = 1e-6;

    private readonly GameData _data;

    public CompiledSkill Skill { get; }
    /// <summary>The compiled character whose evaluator also holds this skill's tree/scoped modifiers.</summary>
    public CompiledCharacter Character { get; }
    public string Id => Skill.SkillId;
    public TagSet Tags => Skill.Tags;
    public bool IsAttack => Skill.Base.WeaponEffectiveness != null;
    public bool IsChannel => Tags.Contains("channel");
    public bool IsGenerator => Tags.Contains("generator");
    public bool IsTriggeredOnly { get; }

    public SkillRuntime(GameData data, CompiledCharacter character, CompiledSkill skill)
    {
        _data = data;
        Skill = skill;
        Character = character with { Evaluator = character.Evaluator.WithExtra(skill.Modifiers) };
        IsTriggeredOnly = Tags.Contains("triggered")
                          || (data.Skills.TryGetValue(skill.SkillId, out var def) && def.IsTriggeredOnly);
    }

    /// <summary>Evaluates a stat for this skill; a stat missing from the data yields <paramref name="baseValue"/> (or 0).</summary>
    public double Stat(string stat, IConditionContext? c, double? baseValue = null) =>
        _data.Stats.ContainsKey(stat) ? Character.Evaluator.Evaluate(stat, Tags, c, baseValue).Value : baseValue ?? 0;

    public bool WeaponFits(WeaponStub? weapon) =>
        !IsAttack || (weapon != null && (Skill.Base.WeaponTypes.Count == 0 || Skill.Base.WeaponTypes.Contains(weapon.Type)));

    public double ChannelInterval =>
        Skill.Behavior.TryGetValue("channel_interval", out var i) && i > 0 ? i : Skill.Base.CastTime;

    /// <summary>Spell: CastTime / cast speed; attack: AttackTimeMultiplier / (weapon APS × attack speed); channel: one interval. At least one step.</summary>
    public double UseTime(IConditionContext c, WeaponStub? weapon, double step)
    {
        double t;
        if (IsChannel) t = ChannelInterval;
        else if (IsAttack && weapon != null)
            t = Skill.Base.AttackTimeMultiplier / (weapon.AttacksPerSecond * Math.Max(MinMultiplier, Stat("attack_speed", c, 1)));
        else t = Skill.Base.CastTime / Math.Max(MinMultiplier, Stat("cast_speed", c, 1));
        return Math.Max(step, t);
    }

    public double Cooldown(IConditionContext c) =>
        Skill.Base.Cooldown is > 0 and var cd ? cd / Math.Max(MinMultiplier, Stat("cooldown_recovery", c, 1)) : 0;

    /// <summary>Resource paid per use. Channel skills state their cost per second, so one tick pays cost × interval.</summary>
    public double Cost(IConditionContext c)
    {
        var cost = Math.Max(0, Stat("resource_cost", c, Skill.ResourceCost));
        return IsChannel ? cost * ChannelInterval : cost;
    }

    /// <summary>Resource gained per use (spec 8.4: once per use if at least one target was hit).</summary>
    public double Gain(IConditionContext c) => Math.Max(0, Stat("resource_gen", c, Skill.ResourceGain));

    public double CritChance(IConditionContext c, WeaponStub? weapon)
    {
        var critBase = IsAttack && weapon != null ? weapon.CritChance : Skill.Base.CritChance;
        return Math.Clamp(Character.Evaluator.Evaluate("crit_chance", Tags, c, critBase).Value, 0, 100);
    }

    /// <summary>Expected distinct targets per use (spec 11.2), at least 1 and at most the pack size.</summary>
    public double Coverage(int pack, double packRadius, IConditionContext c)
    {
        var b = Skill.Behavior;
        var radiusScale = Math.Sqrt(Math.Max(0, Stat("area", c, 1)));
        int Count(string key, double def) => (int)Math.Floor(Math.Max(0, Stat(key, c, b.GetValueOrDefault(key, def))));
        double? Radius(string key) => b.TryGetValue(key, out var r) ? r * radiusScale : null;

        var chain = Count("chain_count", 0);
        bool area = Tags.Contains("area"), projectile = Tags.Contains("projectile");
        if (area && !projectile) return Combat.Coverage.AreaTargets(pack, Radius("area_radius") ?? 0, packRadius);
        if (projectile)
            return Combat.Coverage.DistinctTargets(pack, Count("projectile_count", 1), Count("pierce_count", 0), chain,
                Radius("explosion_radius") ?? (area ? Radius("area_radius") : null), packRadius);
        return Math.Max(1, Math.Min(pack, 1 + chain));
    }
}
