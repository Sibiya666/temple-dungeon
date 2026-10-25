using Dialoid.Core.Stats;

namespace Dialoid.Core.Effects;

/// <summary>Where an effect applies: everywhere, to one skill, or to skills carrying all the given tags.</summary>
public abstract record Scope
{
    public sealed record Global : Scope;
    public sealed record Skill(string SkillId) : Scope;
    public sealed record Tags(TagSet Required) : Scope;
}
