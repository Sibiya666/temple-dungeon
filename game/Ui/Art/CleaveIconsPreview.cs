using Godot;
using Dialoid.Game.Ui;

namespace Dialoid.Game.Ui.Art;

/// <summary>Opens the real skill editor with the generated Cleave icons; does not write a saved build.</summary>
public partial class CleaveIconsPreview : Node
{
    public override async void _Ready()
    {
        try
        {
            var main = GD.Load<PackedScene>("res://Main.tscn").Instantiate<Main>();
            AddChild(main);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var session = main.Session ?? throw new InvalidOperationException(main.LoadError);
            var reference = main.Builds!.References.First(r => r.Path.Contains("warrior_fire_cleave_crit", StringComparison.Ordinal));
            if (!main.Builds.Load(reference).Ok) throw new InvalidOperationException("Cannot load Cleave preview build");
            var textures = session.Data.Skills["cleave"].Tree.Select(node =>
                GameIcons.SkillNode("cleave", node.Id, node.Effects)
                ?? throw new InvalidOperationException("Missing icon: " + node.Id)).ToArray();
            if (textures.Length != 17 || textures.Select(t => t.ResourcePath).Distinct().Count() != 17)
                throw new InvalidOperationException("Cleave requires 17 distinct node icons");
            main.CloseAllWindows();
            main.OpenWindow(WindowIds.Skills);
            main.Skills!.CatalogButtons["cleave"].EmitSignal(Button.SignalName.Pressed);
            for (int i = 0; i < 8; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GD.Print("CLEAVE_NODE_ICONS_OK: 17 distinct textures; real SkillsPanel opened");
            var args = OS.GetCmdlineUserArgs();
            int at = Array.IndexOf(args, "--cleave-screenshot");
            if (at >= 0 && at + 1 < args.Length)
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                GetViewport().GetTexture().GetImage().SavePng(args[at + 1]);
            }
            if (args.Contains("--cleave-root"))
            {
                main.Skills.PadBack();
                for (int i = 0; i < 8; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (at >= 0 && at + 1 < args.Length)
                {
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    GetViewport().GetTexture().GetImage().SavePng(Path.ChangeExtension(args[at + 1], ".catalog.png"));
                }
            }
        }
        catch (Exception e) { GD.PushError("CLEAVE_NODE_ICONS_FAILED: " + e.Message); GetTree().Quit(1); }
    }
}
