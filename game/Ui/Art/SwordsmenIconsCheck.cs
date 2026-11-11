using Godot;
using Dialoid.Core.Effects;
using Dialoid.Game.Ui;

namespace Dialoid.Game.Ui.Art;

/// <summary>Frozen source catalog: 208 node PNGs and six archived AP skill PNGs.
/// --warrior-only limits verification to 104 Warrior nodes, without AP art dependencies.
/// Optional --swordsmen-shots ABSOLUTE_DIRECTORY saves real engine frames; never modifies content.</summary>
public partial class SwordsmenIconsCheck : Node
{
    private static readonly string[] Warrior = [ "cleave", "crush", "battle_cry", "whirlwind", "charge", "throwing_axe" ];
    private static readonly string[] Apostate = [ "dark_slash", "dark_wave", "space_rend", "shadow_step", "awakening", "crescent_flurry" ];
    private static readonly Dictionary<string, string[]> Expected = new(StringComparer.Ordinal)
    {
        ["cleave"] = [ "heavy_hand", "lacerate", "deep_cut", "bloodletting", "thirst", "frenzy", "wide_swing", "split", "precise_strike", "searing_blade", "burning_wounds", "thunder_strike", "echo", "rolling_echo", "steel_skin", "crushing_force", "edge" ],
        ["crush"] = [ "brute_force", "wide_impact", "shockwave", "earthquake", "deep_tremors", "fissures", "thunder_hammer", "static_discharge", "torn_earth", "bloody_harvest", "cold_calculation", "fury_return", "peak_fury", "stun", "concussion", "heavy_blow" ],
        ["battle_cry"] = [ "war_roar", "iron_cry", "intimidation", "battle_rhythm", "ancestral_fury", "long_roar", "unyielding", "steel_will", "hardened_body", "long_stand", "sharp_roar", "blood_cry", "endless_rage", "inspiration", "fury_surge", "tireless", "thunder_roar", "rolling_thunder", "deafening_roar", "ringing_ears" ],
        ["whirlwind"] = [ "steel_storm", "spin_up", "blade_hurricane", "fast_turns", "wide_circle", "thrifty_spin", "grinding_spin", "frenzied_spin", "ice_whirl", "frostbite", "ice_crumbs", "fire_whirl", "fanned_flames", "smoldering", "cutting_wind", "thousand_cuts", "steel_cocoon" ],
        ["charge"] = [ "momentum", "ram", "crushing_ram", "stunning_ram", "trample", "long_run", "unstoppable", "burning_trail", "embers", "furious_charge", "quick_feet", "bloody_path", "gore", "shoulder_bash", "armored_charge", "storm_charge", "true_lunge", "finishing_blow" ],
        ["throwing_axe"] = [ "whetstone", "returning", "axe_fan", "boomerang", "catch", "spare_axe", "piercing_throw", "ricochet", "poisoned_blades", "lethal_dose", "thrower_fury", "jagged_edge", "keen_throw", "headshot", "quick_throw", "storm_edge" ],
        ["dark_slash"] = [ "slash_edge", "slash_conversion", "slash_bleeding", "double_cut", "long_cut", "blade_thirst", "slash_arc", "third_wave", "blade_cadence", "slash_precision", "slash_cruelty", "slash_flow", "slash_rush", "blade_guard", "slash_wounds", "slash_step_echo" ],
        ["dark_wave"] = [ "sharp_edge", "pure_dark", "piercing_wave", "reflected_wave", "double_wave", "compressed_wave", "dark_clot", "heavy_wave", "wide_wave", "dark_trail", "greedy_dark", "dark_outflow", "crit_reflection", "keen_sight", "wave_rending", "bloody_wave", "ice_dark", "slash_echo" ],
        ["space_rend"] = [ "rend_force", "wide_rend", "narrow_rend", "rift_zone", "rift_duration", "rift_depth", "hungry_void", "total_devastation", "rend_reach", "rend_speed", "rend_precision", "rend_crit", "rend_penetration", "rend_step_echo", "rend_guard", "rend_awakening" ],
        ["shadow_step"] = [ "step_force", "arrival_cut", "path_cut", "through_dark", "step_afterimage", "step_recovery", "step_speed", "step_reach", "step_precision", "step_crit", "step_flow", "step_penetration", "step_armor", "step_dodge", "step_wave_echo", "step_awakening" ],
        ["awakening"] = [ "awakening_core", "lingering_awakening", "intense_awakening", "awakening_rage", "demonic_blood", "awakening_recovery", "awakening_cast", "awakening_armor", "awakening_dodge", "awakening_resists", "awakening_precision", "awakening_crit", "awakening_area", "awakening_pierce", "awakening_melee", "awakening_projectile", "awakening_penetration", "awakening_guard", "awakening_regen", "awakening_bleed", "awakening_chill", "awakening_step" ],
        ["crescent_flurry"] = [ "crescent_edge", "wide_fan", "narrowing", "returning_crescent", "cold_crescent", "cutting_wind", "crescent_reach", "crescent_speed", "crescent_precision", "crescent_crit", "crescent_dark", "crescent_chill", "crescent_echo", "crescent_guard", "crescent_awakening", "crescent_dot" ],
    };

    private static Texture2D Exact(Texture2D? texture, string suffix)
    {
        if (texture == null || !texture.ResourcePath.EndsWith(suffix, StringComparison.Ordinal))
            throw new InvalidOperationException($"Missing exact portrait {suffix}; resolved {texture?.ResourcePath ?? "null"}");
        if (texture.GetWidth() != 64 || texture.GetHeight() != 64)
            throw new InvalidOperationException($"Expected 64x64: {texture.ResourcePath} ({texture.GetWidth()}x{texture.GetHeight()})");
        return texture;
    }

    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            bool warriorOnly = args.Contains("--warrior-only", StringComparer.Ordinal);
            var selectedExpected = Expected.Where(pair => !warriorOnly || Warrior.Contains(pair.Key, StringComparer.Ordinal)).ToArray();
            int expectedNodeCount = warriorOnly ? 104 : 208;
            int expectedSkillCount = warriorOnly ? 0 : Apostate.Length;
            int at = Array.IndexOf(args, "--swordsmen-shots");
            string? directory = null;
            if (at >= 0)
            {
                if (at + 1 >= args.Length || !Path.IsPathFullyQualified(args[at + 1]))
                    throw new ArgumentException("--swordsmen-shots requires an absolute directory");
                if (DisplayServer.GetName() == "headless") throw new InvalidOperationException("Screenshots require graphical rendering");
                directory = args[at + 1];
                Directory.CreateDirectory(directory);
            }
            if (GameIcons.Skill("../dark_slash") != null)
                throw new InvalidOperationException("Unsafe skill ID resolved a portrait");
            foreach (var (skill, node) in new[] { ("crush", "heavy_hand"), ("cleave", "brute_force"), ("fireball", "heavy_hand"), ("crush", "missing"), ("../crush", "brute_force") })
                if (GameIcons.HasDedicatedSkillNodeArt(skill, node))
                    throw new InvalidOperationException($"Non-dedicated or wrong-skill pair reported dedicated art: {skill}/{node}");
            foreach (var (skill, node) in new[]
            {
                ("..", "slash_edge"), ("dark_slash", ".."), ("..", ".."),
                ("../dark_slash", "slash_edge"), ("dark_slash", "../slash_edge"),
            })
            {
                var unsafeTexture = GameIcons.SkillNode(skill, node, Array.Empty<Effect>());
                if (unsafeTexture?.ResourcePath.Contains("/skill_nodes/", StringComparison.Ordinal) == true)
                    throw new InvalidOperationException($"Unsafe node ID resolved node art: {skill}/{node}");
            }
            if (!warriorOnly)
            foreach (var (alias, parent) in new[]
            {
                ("dark_wave_return", "dark_wave"), ("dark_slash_wave", "dark_slash"), ("crescent_return", "crescent_flurry"),
            })
            {
                var parentTexture = Exact(GameIcons.Skill(parent), $"/skills/{parent}.png");
                var aliasTexture = Exact(GameIcons.Skill(alias), $"/skills/{parent}.png");
                if (aliasTexture != parentTexture)
                    throw new InvalidOperationException($"AP alias does not share its parent texture: {alias} -> {parent}");
            }
            var portraits = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (skill, nodes) in selectedExpected)
            {
                if (nodes.Distinct(StringComparer.Ordinal).Count() != nodes.Length) throw new InvalidOperationException("Duplicate ID: " + skill);
                foreach (var node in nodes)
                {
                    if (!GameIcons.HasDedicatedSkillNodeArt(skill, node))
                        throw new InvalidOperationException($"Dedicated pair not reported ready: {skill}/{node}");
                    var texture = Exact(GameIcons.SkillNode(skill, node, Array.Empty<Effect>()), $"/skill_nodes/{skill}/{node}.png");
                    if (!paths.Add(texture.ResourcePath)) throw new InvalidOperationException("Reused portrait: " + texture.ResourcePath);
                    portraits.Add(skill + "/" + node, texture);
                }
            }
            if (!warriorOnly)
                foreach (var skill in Apostate) portraits.Add(skill, Exact(GameIcons.Skill(skill), $"/skills/{skill}.png"));
            if (paths.Count != expectedNodeCount || portraits.Count != expectedNodeCount + expectedSkillCount)
                throw new InvalidOperationException($"Expected {expectedNodeCount} nodes + {expectedSkillCount} AP skills");
            var main = GD.Load<PackedScene>("res://Main.tscn").Instantiate<Main>();
            AddChild(main);
            await Frames();
            var session = main.Session ?? throw new InvalidOperationException(main.LoadError);
            var activeClassIds = session.Data.Classes.Keys.ToHashSet(StringComparer.Ordinal);
            var activeSkillIds = session.Data.Skills.Keys.ToHashSet(StringComparer.Ordinal);
            void CheckActiveCatalog()
            {
                if (!activeClassIds.SetEquals(new[] { "warrior", "sorcerer" })
                    || !activeClassIds.SetEquals(session.Data.Classes.Keys)
                    || !activeSkillIds.SetEquals(session.Data.Skills.Keys)
                    || session.Data.Skills.Values.Any(s => s.ClassId != null && s.ClassId != "warrior" && s.ClassId != "sorcerer")
                    || Apostate.Concat(new[] { "dark_wave_return", "dark_slash_wave", "crescent_return" }).Any(session.Data.Skills.ContainsKey))
                    throw new InvalidOperationException("Active catalog changed or archived AP content leaked into the game");
            }
            async Task CheckDebtDisclosure(string classId, string skillId, bool expected)
            {
                if (!main.Character!.SelectClass(classId).Ok) throw new InvalidOperationException("Cannot switch disclosure fixture class");
                main.CloseAllWindows();
                main.OpenWindow(WindowIds.Skills);
                await Frames();
                main.Skills!.CatalogButtons[skillId].EmitSignal(Button.SignalName.Pressed);
                await Frames();
                bool shown = main.Skills.FindChildren("*", "Label", true, false).OfType<Label>()
                    .Any(label => label.IsVisibleInTree() && label.Text.Contains("комплект смысловых иконок не завершён", StringComparison.Ordinal));
                if (shown != expected) throw new InvalidOperationException($"Icon debt disclosure incorrect for {skillId}: {shown}, expected {expected}");
            }
            await CheckDebtDisclosure("warrior", "crush", false);
            await CheckDebtDisclosure("sorcerer", "fireball", true);
            CheckActiveCatalog();
            foreach (var skill in Warrior)
            {
                var definition = session.Data.Skills[skill];
                if (!Expected[skill].ToHashSet(StringComparer.Ordinal).SetEquals(definition.Tree.Select(n => n.Id)))
                    throw new InvalidOperationException("Active catalog differs from frozen IDs: " + skill);
                foreach (var node in definition.Tree) Exact(GameIcons.SkillNode(skill, node.Id, node.Effects), $"/skill_nodes/{skill}/{node.Id}.png");
            }
            if (directory != null)
            {
                GetWindow().Size = new Vector2I(1600, 1000);
                GetWindow().ContentScaleSize = new Vector2I(1600, 1000);
                var builds = main.Builds ?? throw new InvalidOperationException("Build catalog unavailable");
                var reference = builds.References.First(r => r.Path.Contains("warrior_fire_cleave_crit", StringComparison.Ordinal));
                if (!builds.Load(reference).Ok) throw new InvalidOperationException("Cannot load Warrior reference");
                foreach (var skill in Warrior)
                {
                    main.CloseAllWindows();
                    main.OpenWindow(WindowIds.Skills);
                    await Frames();
                    var skills = main.Skills ?? throw new InvalidOperationException("Skills panel unavailable");
                    skills.CatalogButtons[skill].EmitSignal(Button.SignalName.Pressed);
                    await Capture(directory, "warrior-tree-" + skill + ".png");
                }
                main.CloseAllWindows();
                foreach (var (skill, nodes) in selectedExpected)
                    await Sheet(directory, "nodes-" + skill, skill + " / 64, 32, 24 px",
                        nodes.Select(n => (n, portraits[skill + "/" + n])).ToArray());
                if (!warriorOnly)
                    await Sheet(directory, "apostate-skills", "Archived Apostate: asset preview only / 64, 32, 24 px",
                    Apostate.Select(s => (s, portraits[s])).ToArray());
            }
            CheckActiveCatalog();
            GD.Print($"SWORDSMEN_ICONS_OK: mode={(warriorOnly ? "warrior-only" : "full")}; {paths.Count} exact 64x64 node PNGs, {expectedSkillCount} AP skill PNGs; unsafe IDs rejected; active Warrior IDs matched; active catalog unchanged");
            GetTree().Quit(0);
        }
        catch (Exception e) { GD.PushError("SWORDSMEN_ICONS_FAILED: " + e); GetTree().Quit(1); }
    }

    private async Task Frames()
    {
        for (int i = 0; i < 8; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Capture(string directory, string file)
    {
        await Frames();
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var error = GetViewport().GetTexture().GetImage().SavePng(Path.Combine(directory, file));
        if (error != Error.Ok) throw new IOException($"Screenshot {file}: {error}");
    }

    private async Task Sheet(string directory, string file, string title, (string Id, Texture2D Texture)[] items)
    {
        var layer = new CanvasLayer { Layer = 100 };
        AddChild(layer);
        var backdrop = new ColorRect { Color = new Color("0c1114"), Size = new Vector2(1600, 1000), MouseFilter = Control.MouseFilterEnum.Ignore };
        layer.AddChild(backdrop);
        backdrop.AddChild(new Label { Text = title, Position = new Vector2(24, 16) });
        for (int i = 0; i < items.Length; i++)
        {
            var origin = new Vector2(24 + (i % 4) * 394, 70 + (i / 4) * 148);
            backdrop.AddChild(new Label { Text = items[i].Id, Position = origin, Size = new Vector2(380, 30), ClipText = true });
            int x = 0;
            foreach (int size in new[] { 64, 32, 24 })
            {
                var icon = GameIcons.View(items[i].Texture, size);
                icon.Position = origin + new Vector2(x, 38);
                icon.Size = new Vector2(size, size);
                backdrop.AddChild(icon);
                x += size + 24;
            }
        }
        await Capture(directory, file + ".png");
        layer.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
