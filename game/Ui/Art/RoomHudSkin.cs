using Dialoid.Game.Ui.Hud;
using Godot;

namespace Dialoid.Game.Ui.Art;

/// <summary>Optional visual skin. Layout, combat state, input and the room's number safe-area stay with their owners.</summary>
public static class RoomHudSkin
{
    public const string EnableArg = "--hud-art";
    private const string Installed = "dialoid_room_hud_skin";

    /// <summary>Installs once after Main has built its UI. Returns false when already installed.</summary>
    public static bool Apply(Control root)
    {
        if (root.HasMeta(Installed)) return false;
        var controls = Descendants(root).OfType<Control>().ToArray();
        root.Theme = RoomHudTheme.Create(root.Theme);
        foreach (var control in controls)
        {
            if (control.Theme != null) control.Theme = RoomHudTheme.Create(control.Theme);
            if (control is SkillSlotView && control.GetNodeOrNull<Control>("Face") is { } face)
            {
                // The semantic frame, cooldown, key and text draw over the decorative surface.
                var surface = new RoomHudDecoration { Solid = true };
                face.AddChild(surface);
                face.MoveChild(surface, 0);
            }
            if (control.Name == "Bottom" && control.GetParent() is CombatHud)
            {
                var backing = new RoomHudDecoration { Solid = true, Follow = control };
                var owner = control.GetParent();
                owner.AddChild(backing);
                owner.MoveChild(backing, 0);
            }
            if (control is GameWindow or TargetPlateView)
            {
                var panel = (PanelContainer)control;
                // Do not replace GameWindow's live style instance: it owns the active-window colour.
                if (panel.GetThemeStylebox("panel") is StyleBoxFlat previous) previous.BgColor = RoomHudTheme.Stone;
                // A Label does not lay out its children. This keeps the border in its own window's draw order,
                // unlike a sibling under WindowHost, where a background window's frame could cover a foreground one.
                var host = control is GameWindow window ? window.TitleLabel : Descendants(panel).OfType<Label>().First();
                host.AddChild(new RoomHudDecoration { Follow = panel });
            }
        }
        root.SetMeta(Installed, true);
        return true;
    }

    internal static IEnumerable<Node> Descendants(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
