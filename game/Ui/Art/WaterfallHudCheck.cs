using Godot;

namespace Dialoid.Game.Ui.Art;

/// <summary>Checks actual HUD state binding and the water gauge's scale in the engine.</summary>
public partial class WaterfallHudCheck : Node
{
    private int _checks;
    private void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        _checks++;
    }

    public override async void _Ready()
    {
        try
        {
            if (OS.GetCmdlineUserArgs().Contains(TowerWaterfallHud.EnableArg))
                throw new InvalidOperationException("Run WaterfallHudCheck without --tower-hud to measure the original layout first.");
            foreach (var part in new[] { 0d, .25, .5, 1 })
            {
                var reading = new WaterfallReading("Здоровье", part * 1000, 1000, Colors.DarkRed);
                Check(Math.Abs(reading.Fraction - part) < 1e-6, "Incorrect water gauge fraction");
            }
            Check(new WaterfallReading("Мана", 80, 100, Colors.Blue).Fraction == .8f, "Resource fraction");
            Check(new WaterfallReading("Мана", 200, 100, Colors.Blue).Fraction == 1, "Over-cap gauge");
            Check(!new WaterfallReading("Мана", 1, 0, Colors.Blue).Known, "Zero maximum must be unavailable");
            Check(!new WaterfallReading("Мана", double.NaN, 100, Colors.Blue).Known, "NaN must be unavailable");
            Check(!new WaterfallReading("Мана", -1, 100, Colors.Blue).Known, "Negative must be unavailable");
            Check(!new WaterfallReading("Мана", double.PositiveInfinity, 100, Colors.Blue).Known, "Infinity must be unavailable");
            Check(new WaterfallReading("Мана", 53, 100, Colors.Blue).PercentText == "53%", "Float percentage rounding");
            Check(new WaterfallReading("Мана", 29, 100, Colors.Blue).PercentText == "29%", "Double percentage rounding");
            var main = GD.Load<PackedScene>("res://Main.tscn").Instantiate<Main>();
            main.SkipHudForGeometryReview = true;
            AddChild(main);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var reference = main.Builds!.References.First(r => r.Path.Contains("sorcerer_pyromancer_fireball", StringComparison.Ordinal));
            Check(main.Builds.Load(reference).Ok, "Reference build must load");
            for (int i = 0; i < 4; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var slots = main.Hud!.Slots.Select(s => s.GetGlobalRect()).ToArray();
            var hud = TowerWaterfallHud.Install(main);
            Check(ReferenceEquals(hud, TowerWaterfallHud.Install(main)), "Waterfalls must install once");
            for (int i = 0; i < 4; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            hud.Refresh();
            var state = main.Training!.State!;
            Check(hud.Health.Reading?.Value == state.Life && hud.Health.Reading.Max == state.LifeMax, "Live health snapshot mismatch");
            Check(hud.Resource.Reading?.Value == state.Resource && hud.Resource.Reading.Max == state.ResourceMax, "Live resource snapshot mismatch");
            Check(hud.Resource.Reading!.Name == main.Training.ResourceName, "Resource name mismatch");
            Check(slots.SequenceEqual(main.Hud.Slots.Select(s => s.GetGlobalRect())), "Waterfalls moved skill slots");
            foreach (var c in RoomHudSkin.Descendants(hud).OfType<Control>().Prepend(hud))
                Check(c.MouseFilter == Control.MouseFilterEnum.Ignore && c.FocusMode == Control.FocusModeEnum.None, "Waterfall captures input");
            hud.Health.Show(new WaterfallReading("Здоровье", 500, 1000, Colors.DarkRed));
            hud.Resource.Show(new WaterfallReading("Мана", 25, 100, Colors.Blue));
            Check(hud.Health.ShaderFraction == .5f && hud.Resource.ShaderFraction == .25f, "Waterfall materials share a value");
            hud.Refresh();
            hud.Health.Show(new WaterfallReading("Здоровье", 20, 100, Colors.DarkRed, Alert: true));
            Check(hud.Health.WarningShown && hud.Health.ShaderFraction == .2f, "Critical state must not change the true value");
            hud.Refresh();
            var warrior = main.Builds.References.First(r => r.Path.Contains("warrior_berserker_whirlwind", StringComparison.Ordinal));
            Check(main.Builds.Load(warrior).Ok, "Warrior reference load");
            for (int i = 0; i < 4; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            hud.Refresh();
            Check(hud.Resource.Reading!.Name == "Ярость" && hud.Resource.Reading.Value == main.Training.State!.Resource,
                "Class switch kept the old resource");
            Check(main.Builds.Load(reference).Ok, "Restore spellcaster");
            for (int i = 0; i < 4; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            hud.Refresh();
            // Read actual GPU pixels: empty water and the unfilled part must stay empty regardless of animation phase.
            if (DisplayServer.GetName() != "headless")
            {
                var probe = new WaterfallView { Position = new Vector2(20, 80) };
                main.AddChild(probe);
                var rows = new[] { .1f, .4f, .8f };
                var empty = new Color[rows.Length];
                foreach (var part in new[] { 0d, .25, .5, 1 })
                {
                    probe.Show(new WaterfallReading("Проверка", part * 1000, 1000, Colors.Red));
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    var pixels = GetViewport().GetTexture().GetImage();
                    for (int i = 0; i < rows.Length; i++)
                    {
                        var p = probe.GlobalPosition + WaterfallView.Channel.Position + new Vector2(WaterfallView.Channel.Size.X / 2, WaterfallView.Channel.Size.Y * rows[i]);
                        var sample = pixels.GetPixel((int)p.X, (int)p.Y);
                        if (part == 0) empty[i] = sample;
                        else Check((sample.R - empty[i].R > .02f) == (rows[i] < part), $"Shader boundary incorrect at {part}, row {rows[i]}");
                    }
                }
                probe.QueueFree();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            var args = OS.GetCmdlineUserArgs();
            if (DisplayServer.GetName() != "headless")
            {
                var output = Array.IndexOf(args, "--waterfall-screenshot") is var imageAt && imageAt >= 0 && imageAt + 1 < args.Length
                    ? args[imageAt + 1] : null;
                foreach (var resolution in new[] { new Vector2I(1366, 768), new Vector2I(1600, 1000), new Vector2I(2560, 1440) })
                {
                    main.CloseAllWindows();
                    DisplayServer.WindowSetSize(resolution);
                    for (int i = 0; i < 6; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    Check(main.Bar!.DummyOptions.GetGlobalRect().End.X <= resolution.X + .5f, $"{resolution}: preset selector offscreen");
                    foreach (var id in new[] { "character", "skills", "log", "meter" }) main.OpenWindow(id);
                    for (int i = 0; i < 6; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    foreach (var id in new[] { "character", "skills", "log", "meter" })
                    {
                        var box = main.Windows.Window(id).GetGlobalRect();
                        Check(!box.Intersects(hud.Health.GetGlobalRect()) && !box.Intersects(hud.Resource.GetGlobalRect()),
                            $"{resolution}: {id} covers waterfall");
                    }
                    Check(hud.Health.GetGlobalRect().Position.X >= 0 && hud.Resource.GetGlobalRect().End.X <= main.Size.X,
                        $"{resolution}: waterfall offscreen");
                    if (output != null)
                    {
                        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                        var shot = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(output)!, $"windows-{resolution.X}x{resolution.Y}.png");
                        Check(GetViewport().GetTexture().GetImage().SavePng(shot) == Error.Ok, "Resolution screenshot");
                    }
                }
                main.CloseAllWindows();
                DisplayServer.WindowSetSize(new Vector2I(1600, 1000));
                for (int i = 0; i < 6; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                var motionAt = Array.IndexOf(args, "--waterfall-motion");
                if (motionAt >= 0 && motionAt + 1 < args.Length)
                {
                    var directory = args[motionAt + 1];
                    DirAccess.MakeDirRecursiveAbsolute(directory);
                    var gallery = new Control { Size = new Vector2(400, 240), Position = (main.Size - new Vector2(400, 240)) / 2,
                        MouseFilter = Control.MouseFilterEnum.Ignore };
                    main.AddChild(gallery);
                    gallery.AddChild(new ColorRect { Color = new Color("0c1217"), Size = gallery.Size, MouseFilter = Control.MouseFilterEnum.Ignore });
                    var half = new WaterfallView { Position = new Vector2(70, 40) };
                    var full = new WaterfallView { Position = new Vector2(230, 40) };
                    gallery.AddChild(half);
                    gallery.AddChild(full);
                    half.Show(new WaterfallReading("Тест 50%", 500, 1000, new Color("a14e56")));
                    full.Show(new WaterfallReading("Тест 100%", 100, 100, new Color("527cb2")));
                    var fps = Engine.MaxFps;
                    try
                    {
                        Engine.MaxFps = 24;
                        for (int frame = 0; frame < 48; frame++)
                        {
                            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                            var pixels = GetViewport().GetTexture().GetImage().GetRegion(new Rect2I((Vector2I)gallery.GlobalPosition, (Vector2I)gallery.Size));
                            Check(pixels.SavePng(System.IO.Path.Combine(directory, $"{frame:000}.png")) == Error.Ok, "Motion frame");
                        }
                    }
                    finally { Engine.MaxFps = fps; }
                    gallery.QueueFree();
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    GD.Print("WATERFALL_MOTION: 48 actual frames; test levels 50% and 100%");
                }
            }
            var at = Array.IndexOf(args, "--waterfall-screenshot");
            if (at >= 0 && at + 1 < args.Length)
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                Check(GetViewport().GetTexture().GetImage().SavePng(args[at + 1]) == Error.Ok, "Screenshot failed");
            }
            GD.Print($"WATERFALL_CHECK: {_checks} passed");
            GetTree().Quit();
        }
        catch (Exception e)
        {
            GD.PushError($"WATERFALL_CHECK_FAILED: {e}");
            GetTree().Quit(1);
        }
    }
}
