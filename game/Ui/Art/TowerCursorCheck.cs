using Godot;

namespace Dialoid.Game.Ui.Art;

/// <summary>Real-room preview plus explicit cursor swatches: native cursors are absent from viewport captures.</summary>
public partial class TowerCursorCheck : Node
{
    public override async void _Ready()
    {
        try
        {
            var cursor = GetNode<TowerCursor>("/root/TowerCursor");
            var texture = cursor.Pointer ?? throw new InvalidOperationException("Cursor autoload did not load its texture");
            if (texture.GetSize() != new Vector2(40, 40)) throw new InvalidOperationException("Cursor dimensions changed");
            using var pixels = texture.GetImage();
            if (pixels.GetPixel(3, 3).A <= 0.1f) throw new InvalidOperationException("Cursor hotspot is outside the opaque tip");
            if (DisplayServer.GetName() != "headless" && !cursor.NativeInstalled)
                throw new InvalidOperationException("Native cursor was not installed");

            var main = GD.Load<PackedScene>("res://Main.tscn").Instantiate<Main>();
            AddChild(main);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (main.Session == null) throw new InvalidOperationException(main.LoadError);

            var overlay = new CanvasLayer { Layer = 100 };
            AddChild(overlay);
            var panel = new PanelContainer { Position = new Vector2(20, 70), CustomMinimumSize = new Vector2(360, 285) };
            overlay.AddChild(panel);
            var box = new VBoxContainer();
            panel.AddChild(box);
            box.AddChild(new Label { Text = "Курсор: образцы 40 px · точка клика 3,3" });
            var row = new HBoxContainer();
            box.AddChild(row);
            foreach (var color in new[] { new Color("10161b"), new Color("dedbd0") })
            {
                var swatch = new ColorRect { Color = color, CustomMinimumSize = new Vector2(165, 240), MouseFilter = Control.MouseFilterEnum.Ignore };
                row.AddChild(swatch);
                swatch.AddChild(new TextureRect
                {
                    Texture = texture, Position = new Vector2(52, 32), Size = new Vector2(40, 40),
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                });
                swatch.AddChild(new TextureRect
                {
                    Texture = texture, Position = new Vector2(22, 100), Size = new Vector2(120, 120),
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                });
            }
            GD.Print($"CURSOR_OK: 40x40, hotspot 3,3 opaque; native={cursor.NativeInstalled}; Arrow + PointingHand only");
            var args = OS.GetCmdlineUserArgs();
            var at = Array.IndexOf(args, "--cursor-screenshot");
            if (at >= 0 && at + 1 < args.Length && DisplayServer.GetName() != "headless")
            {
                var directory = Path.GetDirectoryName(args[at + 1]) ?? throw new InvalidOperationException("Capture requires an absolute filename");
                Directory.CreateDirectory(directory);
                if (pixels.SavePng(Path.Combine(directory, "tower-pointer.png")) != Error.Ok)
                    throw new InvalidOperationException("Cursor PNG export failed");
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                var error = GetViewport().GetTexture().GetImage().SavePng(args[at + 1]);
                if (error != Error.Ok) throw new InvalidOperationException("Cursor capture failed: " + error);
            }
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            GD.PushError("CURSOR_FAILED: " + error);
            GetTree().Quit(1);
        }
    }
}
