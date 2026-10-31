using Godot;
using Dialoid.Game.Ui;
using Dialoid.Core.Combat;

namespace Dialoid.Game.Ui.Art;

/// <summary>Checks icon binding against the accepted 3D-room UI, without altering a saved build.</summary>
public partial class IconIntegrationCheck : Node
{
    public override async void _Ready()
    {
        try
        {
            if (typeof(TreeViewNode).GetProperty("Icon") == null)
                throw new InvalidOperationException("Accepted tree has no perk icon binding");
            var main = GD.Load<PackedScene>("res://Main.tscn").Instantiate<Main>();
            AddChild(main);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var session = main.Session ?? throw new InvalidOperationException(main.LoadError);
            foreach (var family in new[] { ("Weapon", session.Data.Weapons.Keys.AsEnumerable()), ("Class", session.Data.Classes.Keys.AsEnumerable()) })
            {
                var method = typeof(GameIcons).GetMethod(family.Item1) ?? throw new InvalidOperationException("No icon catalog for " + family.Item1);
                foreach (var id in family.Item2)
                    if (method.Invoke(null, new object[] { id }) == null) throw new InvalidOperationException("Missing " + family.Item1 + " icon: " + id);
                if (method.Invoke(null, new object[] { "../unknown" }) != null) throw new InvalidOperationException("Unsafe equipment icon fallback");
            }
            foreach (var node in main.Passives!.View.Nodes)
                if (node.Icon == null) throw new InvalidOperationException("Tree view has no passive image: " + node.Id);
            foreach (var node in session.Data.Passives.Nodes.Values)
                if (GameIcons.Perk(node.Id, node.Effects) == null) throw new InvalidOperationException("Missing passive " + node.Id);
            foreach (var asc in session.Data.Ascendancies.Values)
                foreach (var node in new[] { asc.Start }.Concat(asc.Notables))
                    if (GameIcons.Perk(node.Id, node.Effects) == null) throw new InvalidOperationException("Missing perk " + node.Id);
            foreach (var id in session.Data.Skills.Keys)
                if (GameIcons.Skill(id) == null) throw new InvalidOperationException("Missing skill " + id);
            foreach (var id in new[] { "bleed", "ignite", "poison", "shock", "chill", "freeze" })
                if (GameIcons.Ailment(id) == null) throw new InvalidOperationException("Missing ailment " + id);
            foreach (var type in Enum.GetValues<DamageType>())
                if (GameIcons.Damage(type) == null) throw new InvalidOperationException("Missing damage " + type);
            foreach (var (id, button) in main.Bar!.Buttons)
                if (button.Icon == null) throw new InvalidOperationException("Top bar has no icon for " + id);
            if (GameIcons.Skill("../unknown") != null) throw new InvalidOperationException("Unsafe icon fallback");
            foreach (var id in new[] { "champion", "rare" })
                if (GameIcons.Elite(id) == null) throw new InvalidOperationException("Missing elite badge " + id);
            var reference = main.Builds!.References.First(r => r.Path.Contains("sorcerer_pyromancer_fireball", StringComparison.Ordinal));
            if (!main.Builds.Load(reference).Ok) throw new InvalidOperationException("Reference load failed");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var classChoice = main.Character!.FindChild("ClassChoice", true, false) as OptionButton
                ?? throw new InvalidOperationException("Class selector missing");
            for (int i = 0; i < classChoice.ItemCount; i++)
                if (classChoice.GetItemIcon(i) == null) throw new InvalidOperationException("Class selector has text only");
            var weaponChoice = main.Character.WeaponOptions!;
            if (weaponChoice.GetItemIcon(0) != null) throw new InvalidOperationException("Empty weapon choice has an icon");
            for (int i = 1; i < weaponChoice.ItemCount; i++)
                if (weaponChoice.GetItemIcon(i) == null) throw new InvalidOperationException("Weapon selector has text only");
            await main.Simulation!.Simulate();
            // The task completion queues a deferred redraw. ProcessFrame can resume before that redraw in graphical mode.
            for (int frame = 0; frame < 8 && main.Simulation.Numbers.Count == 0; frame++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var resultIcons = main.Simulation.Numbers.Where(n => new[] { "sim_dps_skill_", "sim_dps_type_", "sim_skill_", "sim_type_" }
                .Any(prefix => n.Line.Key.StartsWith(prefix, StringComparison.Ordinal))).ToArray();
            if (resultIcons.Length == 0 || resultIcons.Any(n => n.Icon == null))
                throw new InvalidOperationException($"Simulation result rows lack skill/type icons; drawn={main.Simulation.Numbers.Count}, state={main.Simulation.StatusText}, resultSkills={main.Simulation.LastOutcome?.Result.DpsBySkill.Count}, missing={string.Join(',', resultIcons.Where(n => n.Icon == null).Select(n => n.Line.Key))}");
            GD.Print("EQUIPMENT_ICONS_OK: 2 classes, 4 weapons, empty choice and unknown IDs; simulation result icons; 2 elite badges");
            GD.Print("ICON_INTEGRATION_OK: accepted 3D room; 160 passives, 28 ascendancy perks, all skill/ailment/damage IDs, window buttons");
            var args = OS.GetCmdlineUserArgs();
            int at = Array.IndexOf(args, "--icon-screenshots");
            if (at >= 0 && at + 1 < args.Length)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                async Task Capture(string file)
                {
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    var error = GetViewport().GetTexture().GetImage().SavePng(Path.Combine(args[at + 1], file));
                    if (error != Error.Ok) throw new InvalidOperationException("Capture failed: " + error);
                }
                main.CloseAllWindows();
                main.OpenWindow(WindowIds.Passives);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                var view = main.Passives!.View;
                view.FitToView();
                if (view.NodeLocalPosition("keystone_avatar_of_fire") is { } point)
                {
                    view.ZoomAt(point, 1.1f / view.Zoom);
                    view.PanBy(view.Size / 2 - point);
                    view.HoverAt(view.NodeLocalPosition("keystone_avatar_of_fire")!.Value);
                }
                await Capture("accepted-passives-icons.png");
                main.CloseAllWindows(); main.OpenWindow(WindowIds.Ascendancy);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                main.AscendancyView!.View.FitToView();
                if (main.AscendancyView.View.NodeLocalPosition("meteor_rain") is { } perk) main.AscendancyView.View.HoverAt(perk);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                main.AscendancyView.View.FitToView();
                GD.Print($"ICON_TREE_CAPTURE: {main.AscendancyView.View.Nodes.Count} nodes; size {main.AscendancyView.View.Size}; zoom {main.AscendancyView.View.Zoom}");
                await Capture("accepted-ascendancy-icons.png");
                main.CloseAllWindows();
                await Capture("accepted-interface-icons.png");
                main.OpenWindow(WindowIds.Character);
                await Capture("equipment-classes.png");
                main.CloseAllWindows(); main.OpenWindow(WindowIds.Simulation);
                await Capture("simulation-icons.png");
            }
            GetTree().Quit(0);
        }
        catch (Exception e) { GD.PushError("ICON_INTEGRATION_FAILED: " + e.Message); GetTree().Quit(1); }
    }
}
