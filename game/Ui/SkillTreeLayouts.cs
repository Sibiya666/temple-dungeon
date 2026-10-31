using Dialoid.Core.Skills;
using Godot;

namespace Dialoid.Game.Ui;

/// <summary>UI coordinates only; requirements and costs remain authoritative in skill data.</summary>
public static partial class SkillTreeLayouts
{
    public static Dictionary<string, Vector2>? For(SkillDefinition skill)
    {
        var layout = Warrior(skill.Id) ?? Arcane(skill.Id);
        return layout != null && layout.Count == skill.Tree.Count && skill.Tree.All(n => layout.ContainsKey(n.Id))
            ? layout : null;
    }
}
