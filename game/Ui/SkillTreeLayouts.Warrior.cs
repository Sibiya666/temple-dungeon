using Godot;

namespace Dialoid.Game.Ui;

public static partial class SkillTreeLayouts
{
    public static Dictionary<string, Vector2>? Warrior(string skillId) => skillId switch
    {
        "cleave" => new Dictionary<string, Vector2>
        {
            // Upper fan: impact, area/precision and elemental blades.
            ["heavy_hand"] = new(70, -55),
            ["wide_swing"] = new(50, -140),
            ["split"] = new(-25, -205),
            ["precise_strike"] = new(125, -205),
            ["edge"] = new(210, -220),
            ["searing_blade"] = new(160, -100),
            ["burning_wounds"] = new(250, -140),
            ["thunder_strike"] = new(240, -30),
            ["crushing_force"] = new(150, 15),
            // Blood runs southeast; rage and its echoes run southwest.
            ["lacerate"] = new(60, 85),
            ["deep_cut"] = new(135, 135),
            ["bloodletting"] = new(215, 190),
            ["thirst"] = new(-85, 45),
            ["frenzy"] = new(-175, 0),
            ["echo"] = new(-150, 125),
            ["rolling_echo"] = new(-230, 185),
            ["steel_skin"] = new(-60, -75)
        },
        "crush" => new Dictionary<string, Vector2>
        {
            // The impact fan separates wave, aftershock and lightning paths.
            ["brute_force"] = new(40, -90),
            ["wide_impact"] = new(-45, -155),
            ["shockwave"] = new(35, -220),
            ["earthquake"] = new(125, -140),
            ["deep_tremors"] = new(125, -220),
            ["fissures"] = new(210, -210),
            ["thunder_hammer"] = new(140, -55),
            ["static_discharge"] = new(235, -70),
            // Blood and economy below; stun and critical impact to the west.
            ["torn_earth"] = new(100, 60),
            ["bloody_harvest"] = new(200, 115),
            ["cold_calculation"] = new(-10, 105),
            ["fury_return"] = new(0, 205),
            ["peak_fury"] = new(-100, 75),
            ["stun"] = new(-110, -15),
            ["concussion"] = new(-215, -15),
            ["heavy_blow"] = new(-120, -105)
        },
        "battle_cry" => new Dictionary<string, Vector2>
        {
            // Offensive and defensive crowns, each with its own duration tip.
            ["war_roar"] = new(-95, -65),
            ["intimidation"] = new(-200, -65),
            ["battle_rhythm"] = new(-140, -150),
            ["ancestral_fury"] = new(-225, -205),
            ["long_roar"] = new(-50, -215),
            ["iron_cry"] = new(95, -65),
            ["unyielding"] = new(200, -20),
            ["steel_will"] = new(225, -105),
            ["hardened_body"] = new(140, -150),
            ["long_stand"] = new(50, -215),
            // Independent cadence roots flank the central resource branch.
            ["sharp_roar"] = new(-25, -120),
            ["tireless"] = new(-100, 25),
            ["fury_surge"] = new(100, 25),
            ["blood_cry"] = new(0, 100),
            ["endless_rage"] = new(-50, 200),
            ["inspiration"] = new(50, 200),
            // Control spokes remain independent of either cry specialization.
            ["thunder_roar"] = new(-135, 115),
            ["rolling_thunder"] = new(-235, 190),
            ["deafening_roar"] = new(135, 115),
            ["ringing_ears"] = new(235, 190)
        },
        "whirlwind" => new Dictionary<string, Vector2>
        {
            // Cold and fire fans occupy opposite sides of the upper crown.
            ["ice_whirl"] = new(-105, -100),
            ["frostbite"] = new(-210, -95),
            ["ice_crumbs"] = new(-160, -195),
            ["fire_whirl"] = new(105, -100),
            ["fanned_flames"] = new(160, -195),
            ["smoldering"] = new(210, -95),
            ["steel_storm"] = new(0, -150),
            // Adjacent roots describe reach, speed and sustained channel damage.
            ["wide_circle"] = new(-155, 0),
            ["fast_turns"] = new(155, 0),
            ["spin_up"] = new(140, 95),
            ["blade_hurricane"] = new(230, 165),
            ["cutting_wind"] = new(-140, 95),
            ["thousand_cuts"] = new(-230, 165),
            // The two resource roles bracket efficiency; armor stays central below.
            ["grinding_spin"] = new(-80, 190),
            ["thrifty_spin"] = new(0, 220),
            ["frenzied_spin"] = new(80, 190),
            ["steel_cocoon"] = new(0, 100)
        },
        "charge" => new Dictionary<string, Vector2>
        {
            // Momentum forks into a focused ram and the broader trample.
            ["momentum"] = new(0, -120),
            ["ram"] = new(-105, -165),
            ["crushing_ram"] = new(-205, -220),
            ["stunning_ram"] = new(-215, -130),
            ["trample"] = new(105, -165),
            ["long_run"] = new(210, -210),
            // Elemental trail and blood spokes lead to their own outer tips.
            ["storm_charge"] = new(100, -45),
            ["burning_trail"] = new(155, 40),
            ["embers"] = new(260, 65),
            ["bloody_path"] = new(100, 125),
            ["gore"] = new(185, 200),
            // Rage and repeated charges share the lower arc, without false links.
            ["furious_charge"] = new(0, 150),
            ["quick_feet"] = new(-80, 130),
            ["unstoppable"] = new(-155, 80),
            ["shoulder_bash"] = new(-150, -10),
            ["armored_charge"] = new(75, 40),
            ["true_lunge"] = new(-80, -65),
            ["finishing_blow"] = new(-240, -50)
        },
        "throwing_axe" => new Dictionary<string, Vector2>
        {
            // Flight alternatives run across the north; return has its own catch.
            ["returning"] = new(-125, -100),
            ["catch"] = new(-230, -160),
            ["axe_fan"] = new(-40, -155),
            ["boomerang"] = new(45, -155),
            ["whetstone"] = new(130, -100),
            ["spare_axe"] = new(235, -160),
            // Reach and ailments occupy distinct lateral branches.
            ["piercing_throw"] = new(-155, 0),
            ["ricochet"] = new(-270, 0),
            ["poisoned_blades"] = new(155, 0),
            ["lethal_dose"] = new(270, 0),
            ["jagged_edge"] = new(130, 100),
            ["storm_edge"] = new(45, 155),
            // Critical accuracy leads outward; speed and rage stay neighboring roots.
            ["keen_throw"] = new(-130, 100),
            ["headshot"] = new(-230, 170),
            ["quick_throw"] = new(-45, 155),
            ["thrower_fury"] = new(0, 80)
        },
        _ => null
    };
}
