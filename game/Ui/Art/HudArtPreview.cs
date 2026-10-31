using Godot;
using Dialoid.Game.Ui;

namespace Dialoid.Game.Ui.Art;

/// <summary>Geometry review scene: measures the ordinary UI before applying the shared skin; rejects --hud-art.</summary>
public partial class HudArtPreview : Node
{
    public override async void _Ready()
    {
        try
        {
            if (OS.GetCmdlineUserArgs().Contains(RoomHudSkin.EnableArg))
                throw new InvalidOperationException("Run the geometry preview without --hud-art; it must measure the unstyled UI first.");
            var room = GD.Load<PackedScene>("res://Main.tscn").Instantiate<Main>();
            room.SkipHudForGeometryReview = true;
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
            RoomHudSkin.Apply(room);
            var installedCount = Descendants(room).Count();
            if (RoomHudSkin.Apply(room) || Descendants(room).Count() != installedCount)
                throw new InvalidOperationException("HUD skin must be idempotent");
            GD.Print("HUD_ART_IDEMPOTENT: OK");
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

    private static IEnumerable<Node> Descendants(Node node) => RoomHudSkin.Descendants(node);
}
