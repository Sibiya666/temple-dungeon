using Godot;
using Dialoid.Game.Ui;

namespace Dialoid.Game.Ui.Art;

/// <summary>Opt-in art review scene. The game's main scene and existing HUD components stay untouched.</summary>
public partial class HudArtPreview : Node
{
    public override async void _Ready()
    {
        try
        {
            var room = GD.Load<PackedScene>("res://Main.tscn").Instantiate<Control>();
            AddChild(room);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            // Use an existing reference build, rather than painted-on sample numbers or empty slots.
            if (room is Main main && main.Builds is { } builds)
            {
                var reference = builds.References.FirstOrDefault(r => r.Path.Contains("warrior_berserker_whirlwind", StringComparison.Ordinal));
                if (reference != null) builds.Load(reference);
            }
            if (OS.GetCmdlineUserArgs().Contains("--art-windows"))
            {
                // Keep this adapter buildable before and after the room branch is integrated.
                var open = room.GetType().GetMethod("OpenWindow", new[] { typeof(string) });
                open?.Invoke(room, new object[] { "character" });
                open?.Invoke(room, new object[] { "skills" });
            }
            for (int frame = 0; frame < 4; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var geometry = Descendants(room).OfType<Control>()
                .Where(c => c.IsVisibleInTree() && (c is BaseButton || c.GetType().Name is "SkillSlotView" or "GameWindow"))
                .ToDictionary(c => c, c => (c.GetGlobalRect(), c.GetCombinedMinimumSize()));
            Install(room);
            await ToSignal(GetTree().CreateTimer(2), SceneTreeTimer.SignalName.Timeout);
            var changed = new List<string>();
            foreach (var (control, before) in geometry)
            {
                var rect = control.GetGlobalRect();
                if (!rect.Position.IsEqualApprox(before.Item1.Position) || !rect.Size.IsEqualApprox(before.Item1.Size)
                    || !control.GetCombinedMinimumSize().IsEqualApprox(before.Item2))
                    changed.Add($"{control.GetPath()} rect {before.Item1} -> {rect}; min {before.Item2} -> {control.GetCombinedMinimumSize()}");
            }
            if (changed.Count > 0) throw new InvalidOperationException("Art changed interactive geometry:\n" + string.Join("\n", changed));
            GD.Print($"HUD_ART_GEOMETRY: {geometry.Count} controls unchanged");
            var args = OS.GetCmdlineUserArgs();
            var index = Array.IndexOf(args, "--art-screenshot");
            if (index >= 0 && index + 1 < args.Length)
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                var error = GetViewport().GetTexture().GetImage().SavePng(args[index + 1]);
                GD.Print($"HUD_ART_SCREENSHOT: {error}");
                GetTree().Quit(error == Error.Ok ? 0 : 1);
            }
        }
        catch (Exception e)
        {
            GD.PushError($"HUD_ART_PREVIEW_FAILED: {e}");
            GetTree().Quit(1);
        }
    }

    /// <summary>Preview-only adapter; production integration is owned by the room's implementer.</summary>
    public static void Install(Control root)
    {
        var theme = RoomHudTheme.Create(root.Theme);
        root.Theme = theme;
        var controls = Descendants(root).OfType<Control>().ToArray();
        foreach (var control in controls)
        {
            // Apply to local theme roots too. Local style overrides deliberately keep their semantic state colours.
            if (control.Theme != null) control.Theme = RoomHudTheme.Create(control.Theme);
            if (control.GetType().Name == "SkillSlotView" && control.GetNodeOrNull<Control>("Face") is { } face)
            {
                // Behind the existing cooldown, state frame, text and key; semantic state remains unobstructed.
                var surface = new RoomHudDecoration { Solid = true };
                face.AddChild(surface);
                face.MoveChild(surface, 0);
            }
            if (control.Name == "Bottom" && control.GetParent().Name == "CombatHud")
            {
                // Place behind the VBox and all its descendants; the backdrop is clipped to the existing HUD bounds.
                var backing = new RoomHudDecoration { Solid = true, Follow = control };
                var owner = control.GetParent();
                owner.AddChild(backing);
                owner.MoveChild(backing, 0);
            }
            if (control is PanelContainer panel && control.GetType().Name is "GameWindow" or "TargetPlateView")
            {
                // Keep the window's live focus-style instance and padding. Texture corners affect drawing only.
                if (panel.GetThemeStylebox("panel") is StyleBoxFlat previous) previous.BgColor = RoomHudTheme.Stone;
                panel.GetParent().AddChild(new RoomHudDecoration { Follow = panel });
            }
        }
    }

    private static IEnumerable<Node> Descendants(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
