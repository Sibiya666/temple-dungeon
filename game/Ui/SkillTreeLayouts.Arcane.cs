using Godot;

namespace Dialoid.Game.Ui;

public static partial class SkillTreeLayouts
{
    public static Dictionary<string, Vector2>? Arcane(string skillId) => skillId switch
    {
        "spark" => new Dictionary<string, Vector2>
        {
            // Left: current shapes; upper right: shock; lower right: critical flash.
            ["crackle"] = new(-100, 0),
            ["spark_fan"] = new(-190, -80),
            ["storm_fan"] = new(-280, -115),
            ["focused_arc"] = new(-210, 10),
            ["mana_siphon"] = new(-175, 100),
            ["overload"] = new(-95, 190),
            ["static"] = new(80, -70),
            ["breakdown"] = new(110, -165),
            ["ionize"] = new(205, -90),
            ["ember_spark"] = new(-75, -110),
            ["smolder"] = new(-105, -205),
            ["mana_current"] = new(0, 100),
            ["quick_spark"] = new(15, -175),
            ["bright_flash"] = new(100, 70),
            ["dazzle"] = new(200, 140),
            ["spark_ward"] = new(30, 200),
        },
        "fireball" => new Dictionary<string, Vector2>
        {
            // Heat fans left into blast, shapes and ignition; ignition rejoins at soul_flame.
            ["heat"] = new(-90, 0),
            ["wide_blast"] = new(-190, -155),
            ["ring_of_fire"] = new(-280, -210),
            ["scatter"] = new(-185, -65),
            ["compress"] = new(-185, 20),
            ["frost_orb"] = new(-90, -110),
            ["storm_orb"] = new(5, -155),
            ["molten_core"] = new(-180, 100),
            ["eternal_flame"] = new(-280, 70),
            ["lingering_heat"] = new(-300, 150),
            ["flame_mark"] = new(-170, 190),
            ["soul_flame"] = new(-260, 220),
            ["overheat"] = new(-60, 120),
            ["blast_wave"] = new(20, 200),
            ["mana_focus"] = new(100, 0),
            ["backlash"] = new(200, -60),
            ["swift_orb"] = new(160, 90),
        },
        "ice_lance" => new Dictionary<string, Vector2>
        {
            // Weapon forms spread left; freezing and critical control spread right.
            ["frost_edge"] = new(-100, 0),
            ["glacial_block"] = new(-185, -85),
            ["ice_wedge"] = new(-280, -125),
            ["piercing_cold"] = new(-220, 15),
            ["binding_lance"] = new(-180, 115),
            ["bitter_cold"] = new(80, -80),
            ["deep_freeze"] = new(110, -180),
            ["ice_shards"] = new(210, -100),
            ["point"] = new(100, 65),
            ["ice_crown"] = new(220, 80),
            ["cold_precision"] = new(140, 180),
            ["cold_poise"] = new(-70, -125),
            ["swift_ice"] = new(10, -190),
            ["ice_sting"] = new(-75, 180),
            ["ice_carapace"] = new(15, 120),
        },
        "ball_lightning" => new Dictionary<string, Vector2>
        {
            // Core shapes branch left; coverage, shock and critical discharge occupy the right.
            ["storm_core"] = new(-100, 0),
            ["static_orb"] = new(-200, -100),
            ["dense_orb"] = new(-220, 0),
            ["sun_orb"] = new(-185, 100),
            ["solar_corona"] = new(-260, 190),
            ["twin_orb"] = new(-80, 150),
            ["long_flight"] = new(-65, -130),
            ["big_orb"] = new(25, -110),
            ["arc_discharge"] = new(40, -210),
            ["static_field"] = new(110, -65),
            ["spark_cascade"] = new(220, -110),
            ["capacitor"] = new(10, 100),
            ["quick_charge"] = new(10, 200),
            ["bright_discharge"] = new(110, 60),
            ["flash"] = new(220, 100),
            ["ion_shield"] = new(110, 180),
        },
        "meteor" => new Dictionary<string, Vector2>
        {
            // Impact forms left; field and ignition above; frost and critical fragments below.
            ["sky_fire"] = new(-110, 0),
            ["comet_core"] = new(-230, -50),
            ["starfall"] = new(-220, 70),
            ["fire_field"] = new(-70, -110),
            ["scorched_earth"] = new(-110, -210),
            ["kindling"] = new(35, -110),
            ["core_heat"] = new(45, -210),
            ["ice_meteor"] = new(-70, 110),
            ["frost_shatter"] = new(-115, 210),
            ["falling_star"] = new(40, 110),
            ["comet_fragment"] = new(70, 210),
            ["crater"] = new(110, -40),
            ["instant_fall"] = new(210, -95),
            ["star_thrift"] = new(200, 15),
            ["ash_veil"] = new(140, 110),
        },
        "blink" => new Dictionary<string, Vector2>
        {
            // Adjacent elemental spokes above, cast follow-ups right, protection below, mobility left.
            ["arrival_flare"] = new(-115, -100),
            ["double_flare"] = new(-185, -180),
            ["frost_wake"] = new(-15, -135),
            ["deep_cold"] = new(-20, -220),
            ["storm_step"] = new(85, -110),
            ["high_voltage"] = new(140, -195),
            ["spark_trail"] = new(170, -35),
            ["twin_spark"] = new(270, -55),
            ["fire_exit"] = new(105, 35),
            ["blink_focus"] = new(170, 110),
            ["lingering_focus"] = new(265, 165),
            ["impulse"] = new(70, 145),
            ["elusive"] = new(-10, 175),
            ["afterimage"] = new(-85, 145),
            ["phase_barrier"] = new(-150, 95),
            ["ether_mend"] = new(-210, 30),
            ["short_step"] = new(-105, 15),
            ["light_step"] = new(-210, -65),
        },
        "poison_cloud" => new Dictionary<string, Vector2>
        {
            // Cloud coverage above left, poison above right, fire below left, cadence below right.
            ["thick_mist"] = new(-110, -35),
            ["expansion"] = new(-220, -70),
            ["long_haze"] = new(-55, -110),
            ["lingering"] = new(-95, -210),
            ["acrid_haze"] = new(40, -155),
            ["venom_dose"] = new(110, -70),
            ["rot"] = new(220, -120),
            ["erosion"] = new(155, 10),
            ["plague_mist"] = new(235, 70),
            ["caustic_burst"] = new(10, 80),
            ["fire_mist"] = new(-110, 65),
            ["ember_fume"] = new(-220, 70),
            ["ash_smoke"] = new(-175, 165),
            ["mist_cloak"] = new(-65, 175),
            ["quick_brew"] = new(95, 115),
            ["toxic_echo"] = new(170, 200),
        },
        _ => null,
    };
}
