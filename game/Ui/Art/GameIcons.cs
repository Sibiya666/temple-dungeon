using Dialoid.Core.Combat;
using Dialoid.Core.Effects;
using Dialoid.Core.Simulation;
using Godot;

namespace Dialoid.Game.Ui.Art;

/// <summary>Bundled UI art. IDs are whitelisted; missing art never hides the caller's text.</summary>
public static partial class GameIcons
{
    private const string Root = "res://Ui/Art/Icons/";
    private static readonly HashSet<string> Skills = new(StringComparer.Ordinal)
    {
        "cleave", "crush", "battle_cry", "whirlwind", "charge", "throwing_axe",
        "spark", "fireball", "ice_lance", "ball_lightning", "meteor", "blink", "poison_cloud",
        "dark_slash", "dark_wave", "space_rend", "shadow_step", "awakening", "crescent_flurry",
    };
    private static readonly HashSet<string> Ailments = new(StringComparer.Ordinal)
        { "bleed", "ignite", "poison", "shock", "chill", "freeze" };
    private static readonly HashSet<string> PaintedBuffs = new(StringComparer.Ordinal)
    {
        "battle_cry", "cleave_frenzy", "inspiration", "steel_skin", "blink_focus", "phase_barrier", "berserk_frenzy", "static_charge",
    };
    private static readonly Dictionary<string, Texture2D> Cache = new(StringComparer.Ordinal);
    private static readonly HashSet<string> Symbols = new(StringComparer.Ordinal)
    {
        "str", "dex", "will", "int", "health", "armor", "speed", "crit", "resource",
        "modifier", "mechanic", "tree", "character", "inventory", "simulation", "breakdown", "builds", "ring",
        "close", "back", "confirm", "expand", "collapse", "shuffle", "upgrade", "remove", "tower", "map", "lock", "crown",
        "offense", "attribute", "penetration", "projectiles", "chain",
    };

    public static Texture2D? Skill(string? id)
    {
        id = id switch
        {
            "returning_axe" => "throwing_axe", "fireball_blast" => "fireball", "blink_flare" => "blink",
            "dark_wave_return" => "dark_wave", "dark_slash_wave" => "dark_slash", "crescent_return" => "crescent_flurry",
            _ => id,
        };
        return id != null && Skills.Contains(id) ? Load("skills/" + id + ".png") : null;
    }

    public static Texture2D? Ailment(string? id) => id != null && Ailments.Contains(id) ? Load("ailments/" + id + ".png") : null;

    /// <summary>True only for a whitelisted pair whose dedicated texture actually loads, never a fallback symbol.</summary>
    public static bool HasDedicatedSkillNodeArt(string skillId, string nodeId) =>
        DedicatedNodeArt.TryGetValue(skillId, out var nodes) && nodes.Contains(nodeId)
        && Load("skill_nodes/" + skillId + "/" + nodeId + ".png") != null;

    /// <summary>Dedicated paintings by skill/node pair; unfinished families retain their existing effect symbols.</summary>
    public static Texture2D? SkillNode(string skillId, string nodeId, IReadOnlyList<Effect> effects) =>
        DedicatedNodeArt.TryGetValue(skillId, out var nodes) && nodes.Contains(nodeId)
            ? Load("skill_nodes/" + skillId + "/" + nodeId + ".png")
            : Effects(effects);

    public static Texture2D? Damage(DamageType type) => Enum.IsDefined(type)
        ? Load("damage/" + type.ToString().ToLowerInvariant() + ".svg") : null;

    public static Texture2D? HealthPotion() => Load("items/potion_health.png");

    public static Texture2D? Weapon(string? id) => id is "sword" or "greataxe" or "staff" or "wand"
        ? Load("equipment/" + id + ".png") : null;

    public static Texture2D? Class(string? id) => id is "warrior" or "sorcerer"
        ? Load("classes/" + id + ".png") : null;

    public static Texture2D? Elite(string? id) => id is "champion" or "rare" ? Load("elites/" + id + ".svg") : null;

    public static Texture2D? Interface(string id) => id switch
    {
        "skills" => Damage(DamageType.Physical),
        "passives" => Symbol("tree"),
        "ascendancy" => Symbol("crown"),
        "meter" => Symbol("simulation"),
        "effects" or "run_mods" => Symbol("mechanic"),
        "log" => Symbol("builds"),
        "charts" => Symbol("simulation"),
        "laboratory" => Symbol("builds"),
        _ => Symbol(id),
    };

    /// <summary>Named perk art first; numerical minor nodes retain reusable category symbols.</summary>
    public static Texture2D? Perk(string id, IReadOnlyList<Effect> effects)
    {
        if (id is "keystone_avatar_of_fire" or "keystone_blade_dance" or "keystone_inevitability" or
            "keystone_stone_skin" or "keystone_blood_pact" or "keystone_titan_will")
            return Load("perks/" + id + ".png");
        return id switch
        {
            "int_bitter_frost" or "permafrost" => Ailment("chill"),
            "int_frozen_heart" or "aurora" or "shatter" => Ailment("freeze"),
            "int_archmage" => Symbol("int"),
            "int_incineration" or "conflagration" or "spread_ignite" or "eternal_fire" or "int_inferno" or "ashes" => Ailment("ignite"),
            "int_arcane_precision" or "int_piercing_thought" or "dex_assassin" or "str_ruthless_strike" or "brittle" => Symbol("crit"),
            "glacier" => Skill("ice_lance"),
            "meteor_rain" => Skill("meteor"),
            "int_elemental_bastion" or "str_iron_hide" or "fire_armor" or "ice_armor" => Symbol("armor"),
            "dex_venomous_blades" or "dex_crippling_venom" => Ailment("poison"),
            "dex_phantom_step" or "dex_elusive" => Symbol("dex"),
            "dex_flurry" => Symbol("speed"),
            "whirl_rage" => Skill("whirlwind"),
            "dex_flawless_technique" or "str_butcher" or "berserk_frenzy" => Symbol("offense"),
            "dex_volley" => Symbol("projectiles"),
            "thunder_axe" => Skill("throwing_axe"),
            "str_open_wounds" or "str_bloodbath" or "blood_feast" or "flesh_rend" => Ailment("bleed"),
            "str_unbreakable" or "str_bull_vigor" or "will_desperate_resolve" or "reckless" or "insatiable" => Symbol("health"),
            "will_war_herald" or "battle_trance" => Skill("battle_cry"),
            "will_unyielding_rhythm" => Symbol("speed"),
            "will_wellspring" or "will_iron_will" or "soul_heat" or "str_relentless_fury" => Symbol("resource"),
            "will_storm_front" or "storm_weapon" or "discharge" => Skill("spark"),
            "will_chain_reaction" or "chain_strike" => Symbol("chain"),
            "static_charge" => Skill("ball_lightning"),
            "grounding" => Ailment("shock"),
            "storm_veins" => Symbol("penetration"),
            "will_slow_death" => Symbol("simulation"),
            "frost_orb_asc" => Ailment("freeze"),
            _ => Effects(effects),
        };
    }

    public static Texture2D? Buff(string id) => PaintedBuffs.Contains(id) ? Load("buffs/" + id + ".png") : Symbol(id switch
    {
        "steel_cocoon" or "armored_charge" or "ice_carapace" or "steel_will" or "spark_ward" or
        "ash_veil" or "afterimage" or "mist_cloak" or "frost_wake" or "binding_lance" => "armor",
        "intimidation" => "offense",
        "ancestral_fury" => "crit",
        "battle_rhythm" or "spark_rush" or "blink_impulse" or "shoulder_bash" or "unstoppable" => "speed",
        "ether_mend" or "hardened_body" or "unyielding" => "health",
        "blood_cry" or "endless_rage" => "resource",
        "elusive" or "ion_shield" => "dex",
        _ => "modifier",
    });

    /// <summary>Use the core kind and ID; mechanic buff prefixes and counter keys are not asset filenames.</summary>
    public static Texture2D? Timed(TimedEffect effect) => effect.Kind switch
    {
        TimedEffectKind.Ailment => Ailment(effect.Id),
        TimedEffectKind.MechanicStack => effect.Id == "static_charge:charges" ? Buff("static_charge") : Symbol("mechanic"),
        TimedEffectKind.Buff => Buff(effect.Id.StartsWith("mechanic:", StringComparison.Ordinal)
            ? effect.Id["mechanic:".Length..] : effect.Id),
        TimedEffectKind.Debuff => Perk(effect.Id, Array.Empty<Effect>()),
        _ => null,
    };

    public static Texture2D? Symbol(string id) => Symbols.Contains(id) ? Load("symbols/" + id + ".svg") : null;

    public static void Decorate(Button button, string id, int size = 16)
    {
        button.Icon = Interface(id);
        button.ExpandIcon = false;
        button.AddThemeConstantOverride("icon_max_width", size);
    }

    /// <summary>Shared category motifs, not unique portraits or a complete description of a multi-effect node.</summary>
    public static Texture2D? Effects(IReadOnlyList<Effect> effects)
    {
        if (effects.Any(e => e is MechanicEffect)) return Symbol("mechanic");
        if (effects.OfType<SetDamageTypeEffect>().FirstOrDefault() is { } set) return Damage(set.To);
        if (effects.OfType<AddConversionEffect>().FirstOrDefault() is { } conversion) return Damage(conversion.To);
        var mod = effects.OfType<ModifierEffect>().FirstOrDefault()?.Modifier;
        if (mod == null) return Symbol("modifier");
        foreach (var type in Enum.GetValues<DamageType>())
            if (mod.RequiredTags.Contains(type.ToString().ToLowerInvariant())) return Damage(type);
        return Stat(mod.Stat);
    }

    public static Texture2D? Stat(string stat)
    {
        if (Symbols.Contains(stat)) return Symbol(stat);
        if (stat == "damage") return Symbol("offense");
        if (stat.Contains("penetration", StringComparison.Ordinal)) return Symbol("penetration");
        if (stat.Contains("life", StringComparison.Ordinal)) return Symbol("health");
        if (stat.Contains("armor", StringComparison.Ordinal) || stat.Contains("resist", StringComparison.Ordinal)) return Symbol("armor");
        if (stat.Contains("crit", StringComparison.Ordinal)) return Symbol("crit");
        if (stat.Contains("speed", StringComparison.Ordinal) || stat.Contains("dodge", StringComparison.Ordinal)) return Symbol("speed");
        if (stat.Contains("resource", StringComparison.Ordinal) || stat.Contains("mana", StringComparison.Ordinal)) return Symbol("resource");
        return Symbol("modifier");
    }

    /// <summary>A persistent 48 px portrait keeps a selected perk readable even when the graph is zoomed out.</summary>
    public static HBoxContainer Description(Label label, out TextureRect icon)
    {
        var row = new HBoxContainer();
        icon = new TextureRect
        {
            Texture = Symbol("tree"), CustomMinimumSize = new Vector2(48, 48),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(icon); row.AddChild(label);
        return row;
    }

    /// <summary>Decorative only: labels retain accessible names, keyboard focus and mouse input.</summary>
    public static TextureRect View(Texture2D texture, int size = 24) => new()
    {
        Texture = texture,
        CustomMinimumSize = new Vector2(size, size),
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        MouseFilter = Control.MouseFilterEnum.Ignore,
        FocusMode = Control.FocusModeEnum.None,
    };

    private static Texture2D? Load(string relative)
    {
        if (Cache.TryGetValue(relative, out var texture)) return texture;
        var path = Root + relative;
        if (!ResourceLoader.Exists(path)) return null;
        texture = GD.Load<Texture2D>(path);
        if (texture != null) Cache.Add(relative, texture);
        return texture;
    }
}
