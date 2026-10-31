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
            var texture = cursor.Pointer ?? throw new InvalidOperationException("Cursor autoload did not load its sword");
            var hand = cursor.Hand ?? throw new InvalidOperationException("Cursor autoload did not load its hand");
            if (texture.GetSize() != new Vector2(40, 40) || hand.GetSize() != new Vector2(40, 40))
                throw new InvalidOperationException("Cursor dimensions changed");
            if (texture == hand || texture.ResourcePath == hand.ResourcePath)
                throw new InvalidOperationException("Sword and hand must use different textures");
            using var pixels = texture.GetImage();
            using var handPixels = hand.GetImage();
            if (pixels.GetPixel(3, 3).A <= 0.1f) throw new InvalidOperationException("Cursor hotspot is outside the opaque tip");
            if (handPixels.GetPixel(12, 3).A <= 0.1f) throw new InvalidOperationException("Hand hotspot is outside the opaque fingertip");
            if (pixels.GetData().SequenceEqual(handPixels.GetData())) throw new InvalidOperationException("Cursor shapes have identical pixels");
            if (DisplayServer.GetName() != "headless" && !cursor.NativeInstalled)
                throw new InvalidOperationException("Native cursor was not installed");

            var main = GD.Load<PackedScene>("res://Main.tscn").Instantiate<Main>();
            AddChild(main);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (main.Session == null) throw new InvalidOperationException(main.LoadError);

            var normalButton = new Button();
            var explicitButton = new Button { MouseDefaultCursorShape = Control.CursorShape.Ibeam };
            var plainControl = new Control();
            AddChild(normalButton); AddChild(explicitButton); AddChild(plainControl);
            for (var frame = 0; frame < 2; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (normalButton.MouseDefaultCursorShape != Control.CursorShape.PointingHand ||
                explicitButton.MouseDefaultCursorShape != Control.CursorShape.Ibeam ||
                plainControl.MouseDefaultCursorShape != Control.CursorShape.Arrow)
                throw new InvalidOperationException("Button cursor registration changed an explicit or noninteractive shape");
            normalButton.Disabled = true;
            normalButton.EmitSignal(Control.SignalName.MouseEntered);
            if (normalButton.MouseDefaultCursorShape != Control.CursorShape.Arrow)
                throw new InvalidOperationException("Disabled button retained the hand cursor");
            normalButton.Disabled = false;
            normalButton.EmitSignal(Control.SignalName.MouseEntered);
            if (normalButton.MouseDefaultCursorShape != Control.CursorShape.PointingHand)
                throw new InvalidOperationException("Re-enabled button did not regain the hand cursor");
            normalButton.MouseDefaultCursorShape = Control.CursorShape.Cross;
            normalButton.EmitSignal(Control.SignalName.MouseEntered);
            if (normalButton.MouseDefaultCursorShape != Control.CursorShape.Cross)
                throw new InvalidOperationException("An explicit cursor change was overwritten");
            normalButton.QueueFree(); explicitButton.QueueFree(); plainControl.QueueFree();
            GD.Print("CURSOR_BUTTONS_OK: ordinary button hand, disabled arrow, explicit Ibeam/Cross retained, plain Control arrow");

            var overlay = new CanvasLayer { Layer = 100 };
            AddChild(overlay);
            var panel = new PanelContainer { Position = new Vector2(20, 70), CustomMinimumSize = new Vector2(680, 285) };
            overlay.AddChild(panel);
            var box = new VBoxContainer();
            panel.AddChild(box);
            box.AddChild(new Label { Text = "Меч · hotspot 3,3 | Латная рука · hotspot 12,3 | 40 px и ×3" });
            var row = new HBoxContainer();
            box.AddChild(row);
            foreach (var sample in new[] { texture, hand })
            foreach (var color in new[] { new Color("10161b"), new Color("dedbd0") })
            {
                var swatch = new ColorRect { Color = color, CustomMinimumSize = new Vector2(165, 240), MouseFilter = Control.MouseFilterEnum.Ignore };
                row.AddChild(swatch);
                swatch.AddChild(new TextureRect
                {
                    Texture = sample, Position = new Vector2(52, 32), Size = new Vector2(40, 40),
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                });
                swatch.AddChild(new TextureRect
                {
                    Texture = sample, Position = new Vector2(22, 100), Size = new Vector2(120, 120),
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                });
            }
            GD.Print($"CURSOR_OK: distinct sword + hand, 40x40; hotspots 3,3 / 12,3 opaque; native={cursor.NativeInstalled}; Arrow + PointingHand only");
            var args = OS.GetCmdlineUserArgs();
            var at = Array.IndexOf(args, "--cursor-screenshot");
            if (at >= 0 && at + 1 < args.Length && DisplayServer.GetName() != "headless")
            {
                var directory = Path.GetDirectoryName(args[at + 1]) ?? throw new InvalidOperationException("Capture requires an absolute filename");
                Directory.CreateDirectory(directory);
                if (pixels.SavePng(Path.Combine(directory, "tower-sword.png")) != Error.Ok ||
                    handPixels.SavePng(Path.Combine(directory, "tower-hand.png")) != Error.Ok)
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
