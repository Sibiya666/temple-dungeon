using Dialoid.Core.Character;
using Dialoid.Core.Combat;
using Dialoid.Core.Effects;
using Dialoid.Core.Stats;

namespace Dialoid.Core.Skills;

/// <summary>Mutable accumulator used while one skill is compiled.</summary>
internal sealed class SkillBuildState
{
    private static readonly string[] AreaBehaviorKeys = { "areaRadius", "area_radius" };

    public TagSet Tags;
    public DamageType? DamageType;
    public double Cost, Gain;
    public readonly Dictionary<string, double> Behavior;
    public readonly List<Conversion> Conversions = new();
    public readonly List<Modifier> Modifiers = new();
    public readonly List<GrantProcEffect> Procs = new();
    public readonly List<MechanicEffect> Mechanics = new();

    public SkillBuildState(SkillDefinition def)
    {
        Tags = def.Tags.With("skill:" + def.Id);
        Behavior = new Dictionary<string, double>(def.Base.Behavior);
        Cost = def.Base.ResourceCost;
        Gain = def.Base.ResourceGain;
    }

    /// <summary>Applies one effect. <paramref name="points"/> multiplies PerPoint effects; <paramref name="modTags"/> are added to modifier tags.</summary>
    public void Apply(Effect e, int points, SourceRef src, TagSet modTags, bool stampSource = false)
    {
        double k = e.PerPoint ? points : 1;
        switch (e)
        {
            case ModifierEffect me:
                Modifiers.Add(me.Modifier with
                {
                    Value = me.Modifier.Value * k,
                    RequiredTags = me.Modifier.RequiredTags.Union(modTags),
                    Source = stampSource ? src : me.Modifier.Source,
                });
                break;
            case AddTagEffect t: Tags = Tags.With(t.Tag); break;
            case RemoveTagEffect t:
                Tags = Tags.Without(t.Tag);
                if (t.Tag == "area")
                    foreach (var key in AreaBehaviorKeys) Behavior.Remove(key);
                break;
            case SetBehaviorEffect b: Behavior[b.Param] = b.Value; break;
            case AddBehaviorEffect b: Behavior[b.Param] = Behavior.GetValueOrDefault(b.Param) + b.Delta * k; break;
            case AddConversionEffect c: Conversions.Add(new Conversion(c.From, c.To, c.Percent * k, src)); break;
            case SetDamageTypeEffect d:
                DamageType = d.To;
                foreach (var t in DamageTypeTags.All) Tags = Tags.Without(t);
                Tags = Tags.With(DamageTypeTags.Of(d.To));
                break;
            case SetResourceEffect r:
                if (r.Field == ResourceField.Cost) Cost = r.Value; else Gain = r.Value;
                break;
            case GrantProcEffect p: Procs.Add(p); break;
            case MechanicEffect m when m.MechanicId != SkillCompiler.GrantSkillNodeId: Mechanics.Add(m); break;
        }
    }
}

internal static class DamageTypeTags
{
    public static readonly string[] All = Enum.GetValues<DamageType>().Select(Of).ToArray();

    public static string Of(DamageType t) => t.ToString().ToLowerInvariant();
}

/// <summary>Effective tree points after ascendancy-granted nodes (spec 8.4, conflicts).</summary>
internal static class SkillNodePoints
{
    /// <summary>Effective: points that apply effects. Paid: points that cost the player skill points (granted nodes are free).</summary>
    public static (Dictionary<string, int> Effective, Dictionary<string, int> Paid) Resolve(
        SkillSlot slot, SkillDefinition def, IReadOnlyList<(string SkillId, string NodeId)> granted)
    {
        var paid = slot.NodePoints.Where(kv => kv.Value > 0).ToDictionary(kv => kv.Key, kv => kv.Value);
        var effective = new Dictionary<string, int>(paid);
        foreach (var (skillId, nodeId) in granted)
        {
            if (skillId != def.Id) continue;
            var node = def.Tree.FirstOrDefault(n => n.Id == nodeId);
            if (node == null) continue;
            if (node.ExclusiveGroup != null)
                foreach (var other in def.Tree.Where(n => n.ExclusiveGroup == node.ExclusiveGroup && n.Id != nodeId))
                {
                    effective.Remove(other.Id);
                    paid.Remove(other.Id);
                }
            if (!effective.ContainsKey(nodeId)) effective[nodeId] = 1;
        }
        return (effective, paid);
    }
}
