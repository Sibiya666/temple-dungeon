using Dialoid.Core.Effects;

namespace Dialoid.Core.Mechanics;

/// <summary>Short builders for the declared params of the catalog mechanics.</summary>
internal static class P
{
    public static IReadOnlyList<MechanicParam> Of(params MechanicParam[] ps) => ps;
    public static MechanicParam Num(string name) => new(name, MechanicParamKind.Number);
    public static MechanicParam Text(string name) => new(name, MechanicParamKind.Text);
    public static MechanicParam Stat(string name) => new(name, MechanicParamKind.Stat);
    public static MechanicParam Ailment(string name = "ailment") => new(name, MechanicParamKind.Ailment);
    public static MechanicParam Skill(string name = "skill", bool required = false) => new(name, MechanicParamKind.Skill, required);
    public static MechanicParam Skills(string name) => new(name, MechanicParamKind.SkillList);
    public static MechanicParam DamageType(string name) => new(name, MechanicParamKind.DamageType);
    public static MechanicParam Attribute(string name) => new(name, MechanicParamKind.Attribute);
    public static MechanicParam Condition(string name) => new(name, MechanicParamKind.Condition);
}
