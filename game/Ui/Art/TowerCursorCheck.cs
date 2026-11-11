using Godot;

namespace Dialoid.Game.Ui.Art;

/// <summary>Checks cursor preferences without changing the real profile; native art is shown explicitly in captures.</summary>
public partial class TowerCursorCheck : Node
{
    private static void Require(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
    }

    public override async void _Ready()
    {
        var cursor = GetNode<TowerCursor>("/root/TowerCursor");
        var originalId = cursor.SelectedId;
        var originalSettingsPath = cursor.SettingsPath;
        var testPath = Path.Combine(Path.GetTempPath(), "dialoid-cursor-" + Guid.NewGuid().ToString("N") + ".cfg");
        try
        {
            CheckPreferences(cursor, testPath);
            CheckKeyGuards();
            var imageData = new List<byte[]>();
            foreach (var id in TowerCursor.Designs)
            {
                var texture = cursor.TextureFor(id) ?? throw new InvalidOperationException("Missing cursor " + id);
                Require(texture.GetSize() == new Vector2(48, 48), "Cursor dimensions changed: " + id);
                using var pixels = texture.GetImage();
                var hotspot = TowerCursor.HotspotFor(id);
                Require(pixels.GetPixel((int)hotspot.X, (int)hotspot.Y).A > 0.1f, "Transparent hotspot: " + id);
                var data = pixels.GetData();
                Require(imageData.All(other => !other.SequenceEqual(data)), "Cursor images are identical");
                imageData.Add(data);
                cursor.Select(id, persist: false, showToast: false);
                Require(cursor.Pointer == texture && cursor.Hand == texture, "Arrow/PointingHand diverged");
                Require(DisplayServer.GetName() == "headless" || cursor.NativeInstalled, "Native cursor not installed");
            }

            var main = GD.Load<PackedScene>("res://Main.tscn").Instantiate<Main>();
            AddChild(main);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(main.Session != null, main.LoadError ?? "Main did not create a session");
            await CheckButtons();
            var field = new LineEdit { Position = new Vector2(20, 400), Size = new Vector2(150, 30) };
            AddChild(field);
            field.GrabFocus();
            Require(!cursor.CanCycle(new InputEventKey { Keycode = Godot.Key.F8, Pressed = true }), "F8 intercepted text input");
            field.ReleaseFocus();
            field.QueueFree();
            cursor.Select(TowerCursor.GlassId, persist: false, showToast: false);
            GetViewport().PushInput(new InputEventKey { Keycode = Godot.Key.F8, Pressed = true });
            GetViewport().PushInput(new InputEventKey { Keycode = Godot.Key.F8, Pressed = false });
            Require(cursor.SelectedId == TowerCursor.SpearId && TowerCursor.LoadSelection(testPath) == TowerCursor.SpearId,
                "F8 did not reach the actual unhandled input/save route");
            GetViewport().PushInput(new InputEventKey { Keycode = Godot.Key.F8, Pressed = true, Echo = true });
            Require(cursor.SelectedId == TowerCursor.SpearId, "F8 echo cycled via input route");
            var toast = cursor.FindChild("CursorToast", true, false) as Label ?? throw new InvalidOperationException("Cursor toast missing");
            Require(toast is { Visible: true, MouseFilter: Control.MouseFilterEnum.Ignore, FocusMode: Control.FocusModeEnum.None },
                "Cursor toast was absent or interactive");
            await ToSignal(GetTree().CreateTimer(2.1), SceneTreeTimer.SignalName.Timeout);
            Require(!toast.Visible, "Cursor toast did not expire");
            GD.Print("CURSOR_F8_OK: Viewport.PushInput press/release cycles once and saves isolated preference; echo ignored");

            var overlay = new CanvasLayer { Layer = 100 };
            AddChild(overlay);
            var panel = new PanelContainer { Position = new Vector2(20, 70), CustomMinimumSize = new Vector2(360, 310) };
            overlay.AddChild(panel);
            var box = new VBoxContainer();
            panel.AddChild(box);
            var label = new Label();
            box.AddChild(label);
            var row = new HBoxContainer();
            box.AddChild(row);
            var samples = new List<TextureRect>();
            foreach (var color in new[] { new Color("10161b"), new Color("dedbd0") })
            {
                var swatch = new ColorRect { Color = color, CustomMinimumSize = new Vector2(165, 265), MouseFilter = Control.MouseFilterEnum.Ignore };
                row.AddChild(swatch);
                foreach (var scale in new[] { 1, 3 })
                {
                    var sample = new TextureRect
                    {
                        Position = scale == 1 ? new Vector2(48, 20) : new Vector2(10, 100),
                        Size = new Vector2(48, 48) * scale,
                        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                        TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
                        MouseFilter = Control.MouseFilterEnum.Ignore,
                    };
                    swatch.AddChild(sample); samples.Add(sample);
                }
            }
            var args = OS.GetCmdlineUserArgs();
            var at = Array.IndexOf(args, "--cursor-screenshot");
            foreach (var id in TowerCursor.Designs)
            {
                cursor.Select(id, persist: false, showToast: false);
                label.Text = $"{id} · 48 px и ×3 · hotspot {cursor.Hotspot}";
                foreach (var sample in samples) sample.Texture = cursor.Pointer;
                if (at >= 0 && at + 1 < args.Length && DisplayServer.GetName() != "headless")
                {
                    var directory = Path.GetDirectoryName(args[at + 1]) ?? throw new InvalidOperationException("Absolute capture filename required");
                    Directory.CreateDirectory(directory);
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    using var screenshot = GetViewport().GetTexture().GetImage();
                    Require(screenshot.SavePng(Path.Combine(directory, "in-game-" + id + ".png")) == Error.Ok, "Capture failed: " + id);
                }
            }
            GD.Print($"CURSOR_OK: 3 distinct 48px PNGs, opaque hotspots, shared Arrow/PointingHand; native={cursor.NativeInstalled}");
            GD.Print("CURSOR_SELECTION_OK: default, cycle, save/load, corrupt/unknown config, F8 guards, text focus; profile unchanged");
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            GD.PushError("CURSOR_FAILED: " + error);
            GetTree().Quit(1);
        }
        finally
        {
            cursor.SettingsPath = originalSettingsPath;
            cursor.Select(originalId, persist: false, showToast: false);
            if (File.Exists(testPath)) File.Delete(testPath);
        }
    }

    private static void CheckPreferences(TowerCursor cursor, string path)
    {
        Require(TowerCursor.LoadSelection(path) == TowerCursor.GlassId, "Missing preference did not default to glass");
        cursor.SettingsPath = path;
        cursor.Select(TowerCursor.DefaultId, persist: false, showToast: false);
        var expected = new[] { TowerCursor.SpearId, TowerCursor.KeyId, TowerCursor.GlassId };
        foreach (var id in expected)
        {
            cursor.Cycle(persist: true, showToast: false);
            Require(cursor.SelectedId == id && TowerCursor.LoadSelection(path) == id, "Cursor cycle/save mismatch");
        }
        File.WriteAllText(path, "[cursor]\ndesign=\"unknown\"\n");
        Require(TowerCursor.LoadSelection(path) == TowerCursor.DefaultId, "Unknown preference accepted");
        File.WriteAllText(path, "[cursor]\ndesign=123\n");
        Require(TowerCursor.LoadSelection(path) == TowerCursor.DefaultId, "Nonstring preference accepted");
        File.WriteAllText(path, "[broken");
        Require(TowerCursor.LoadSelection(path) == TowerCursor.DefaultId, "Corrupt preference accepted");
        Require(!cursor.Select("unknown", persist: false, showToast: false) && cursor.SelectedId == TowerCursor.DefaultId,
            "Unknown API ID did not fall back safely");
    }

    private static void CheckKeyGuards()
    {
        var press = new InputEventKey { Keycode = Godot.Key.F8, Pressed = true };
        Require(TowerCursor.IsCycleKey(press, false, false), "F8 press rejected");
        Require(!TowerCursor.IsCycleKey(press, true, false) && !TowerCursor.IsCycleKey(press, false, true), "F8 focus/popup guard missing");
        foreach (var input in new InputEvent[]
        {
            new InputEventKey { Keycode = Godot.Key.F8, Pressed = false },
            new InputEventKey { Keycode = Godot.Key.F8, Pressed = true, Echo = true },
            new InputEventKey { Keycode = Godot.Key.F8, Pressed = true, CtrlPressed = true },
            new InputEventKey { Keycode = Godot.Key.F8, Pressed = true, AltPressed = true },
            new InputEventKey { Keycode = Godot.Key.F8, Pressed = true, ShiftPressed = true },
            new InputEventKey { Keycode = Godot.Key.F8, Pressed = true, MetaPressed = true },
            new InputEventKey { Keycode = Godot.Key.F7, Pressed = true },
            new InputEventJoypadButton { ButtonIndex = JoyButton.A, Pressed = true },
        }) Require(!TowerCursor.IsCycleKey(input, false, false), "Nonqualifying input cycled cursor");
    }

    private async Task CheckButtons()
    {
        var normal = new Button();
        var explicitButton = new Button { MouseDefaultCursorShape = Control.CursorShape.Ibeam };
        var plain = new Control();
        AddChild(normal); AddChild(explicitButton); AddChild(plain);
        for (var frame = 0; frame < 2; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Require(normal.MouseDefaultCursorShape == Control.CursorShape.PointingHand && explicitButton.MouseDefaultCursorShape == Control.CursorShape.Ibeam &&
            plain.MouseDefaultCursorShape == Control.CursorShape.Arrow, "Cursor registration overwrote a shape");
        normal.Disabled = true; normal.EmitSignal(Control.SignalName.MouseEntered);
        Require(normal.MouseDefaultCursorShape == Control.CursorShape.Arrow, "Disabled button retained hand");
        normal.Disabled = false; normal.EmitSignal(Control.SignalName.MouseEntered);
        Require(normal.MouseDefaultCursorShape == Control.CursorShape.PointingHand, "Re-enabled button lost hand");
        normal.MouseDefaultCursorShape = Control.CursorShape.Cross; normal.EmitSignal(Control.SignalName.MouseEntered);
        Require(normal.MouseDefaultCursorShape == Control.CursorShape.Cross, "Explicit cursor overwritten");
        normal.QueueFree(); explicitButton.QueueFree(); plain.QueueFree();
        GD.Print("CURSOR_BUTTONS_OK: ordinary button hand, disabled arrow, explicit Ibeam/Cross retained, plain Control arrow");
    }
}
