using Dialoid.Core.Character;
using Dialoid.Core.Combat;
using Dialoid.Core.Effects;

namespace Dialoid.Core.Tests.Content;

/// <summary>Spec 8.5 and 10.5 over the real data: the classless loot skill «Ядовитый туман», the two item stubs and the loot level cap.</summary>
public class LootContentTests
{
    private static Build SorcererWithAxe(int slotLevel, params string[] items)
    {
        var b = new Build
        {
            ClassId = "sorcerer", Level = 20, WeaponId = "sword",
            Slots = { new SkillSlot { SkillId = "throwing_axe", Level = slotLevel } },
        };
        b.ItemIds.AddRange(items);
        return b;
    }

    private static Build WithCloud(string classId, int slotLevel, params string[] items)
    {
        var b = new Build
        {
            ClassId = classId, Level = 20, WeaponId = classId == "warrior" ? "greataxe" : "staff",
            Slots = { new SkillSlot { SkillId = "poison_cloud", Level = slotLevel } },
        };
        b.ItemIds.AddRange(items);
        return b;
    }

    [Fact]
    void Poison_cloud_is_a_classless_loot_skill()
    {
        var cloud = ContentHarness.Data.Skills["poison_cloud"];
        Assert.Null(cloud.ClassId);
        Assert.False(cloud.IsTriggeredOnly);
        Assert.False(cloud.IsAttack);
        foreach (var t in new[] { "spell", "area", "poison", "dot" }) Assert.True(cloud.Tags.Contains(t), t);
        Assert.Equal(3, cloud.Base.Behavior["ground_duration"]);
        Assert.True(cloud.Base.Behavior["ground_fraction"] > 0);
        Assert.Equal(100, cloud.Base.AilmentChances["poison"]);
        Assert.True(cloud.Base.Cooldown is > 0);
        Assert.Equal(0, cloud.Base.ResourceCost);   // usable by both classes: no mana, no Fury
        Assert.Equal(0, cloud.Base.ResourceGain);   // never a mana source
        Assert.Contains(cloud.Tree, n => n.Effects.Any(e => e is SetDamageTypeEffect { To: DamageType.Fire }));
        Assert.Contains(cloud.Tree, n => n.Effects.OfType<ModifierEffect>().Any(e => e.Modifier.Stat == "area"));
        Assert.Contains(cloud.Tree, n => n.Effects.OfType<ModifierEffect>().Any(e => e.Modifier.Stat == "cooldown_recovery"));
        Assert.Contains(cloud.Tree, n => n.Effects.OfType<AddBehaviorEffect>().Any(e => e.Param == "ground_duration"));

        var ring = ContentHarness.Data.Items["ring_of_mist"];
        Assert.Equal("Кольцо тумана", ring.Name);
        Assert.Equal("poison_cloud", ring.GrantsSkill);
        Assert.Equal(0, ring.SkillLevelBonus);
        var axe = ContentHarness.Data.Items["veteran_axe"];
        Assert.Equal("Топор ветерана", axe.Name);
        Assert.Equal("throwing_axe", axe.GrantsSkill);
        Assert.Equal(2, axe.SkillLevelBonus);
    }

    [Fact]
    void Sorcerer_can_slot_throwing_axe_with_veteran_axe_and_sword()
    {
        var ok = SorcererWithAxe(12, "veteran_axe");
        Assert.Empty(BuildRules.Validate(ok, ContentHarness.Data));
        Assert.Empty(BuildRules.Validate(SorcererWithAxe(1, "veteran_axe"), ContentHarness.Data));
        Assert.NotEmpty(BuildRules.Validate(SorcererWithAxe(12), ContentHarness.Data)); // no item, no skill
    }

    [Fact]
    void Loot_skill_cap_is_ten_plus_item_bonus()
    {
        var data = ContentHarness.Data;
        Assert.Empty(BuildRules.Validate(WithCloud("sorcerer", 10, "ring_of_mist"), data));
        Assert.Empty(BuildRules.Validate(WithCloud("warrior", 10, "ring_of_mist"), data));
        Assert.NotEmpty(BuildRules.Validate(WithCloud("sorcerer", 11, "ring_of_mist"), data));
        Assert.NotEmpty(BuildRules.Validate(WithCloud("sorcerer", 1), data));
        Assert.Empty(BuildRules.Validate(SorcererWithAxe(12, "veteran_axe"), data));
        Assert.NotEmpty(BuildRules.Validate(SorcererWithAxe(13, "veteran_axe"), data));
        // A native skill keeps its cap of 20 regardless of items.
        var native = new Build { ClassId = "sorcerer", Level = 20, Slots = { new SkillSlot { SkillId = "spark", Level = 20 } } };
        Assert.Empty(BuildRules.Validate(native, data));
    }

    [Fact]
    void Sorcerer_with_throwing_axe_deals_damage()
    {
        var r = ContentHarness.Run(SorcererWithAxe(12, "veteran_axe"), "training");
        Assert.Empty(r.Violations);
        Assert.True(r.DpsSingleTarget > 0, $"DPS {r.DpsSingleTarget}");
        Assert.True(r.DamageBySkill.GetValueOrDefault("throwing_axe") > 0);
    }

    // ---- Poison cloud tree: every fork option wins a scenario at the loot cap (skill level 10, ten points, full compatible layout) ----

    private static Build Cloud(string classId, params (string Node, int Points)[] nodes)
    {
        var b = WithCloud(classId, 10, "ring_of_mist");
        foreach (var (n, p) in nodes) b.Slots[0].NodePoints[n] = p;
        Assert.Equal(10, nodes.Sum(x => x.Points));
        Assert.Empty(BuildRules.Validate(b, ContentHarness.Data));
        return b;
    }

    private static readonly (string, int)[] FireLayout =
        { ("fire_mist", 1), ("ember_fume", 2), ("ash_smoke", 2), ("quick_brew", 3), ("long_haze", 2) };
    private static readonly (string, int)[] PlagueLayout =
        { ("plague_mist", 1), ("venom_dose", 3), ("rot", 2), ("quick_brew", 3), ("long_haze", 1) };
    private static readonly (string, int)[] NoForkLayout =
        { ("venom_dose", 3), ("rot", 2), ("quick_brew", 3), ("long_haze", 2) };

    [Fact]
    void Each_cloud_element_wins_its_own_scenario()
    {
        var fire = Cloud("sorcerer", FireLayout);
        var plague = Cloud("sorcerer", PlagueLayout);
        var none = Cloud("sorcerer", NoForkLayout);
        // One target: the burning cloud beats poison and the plain cloud.
        var fireOne = ContentHarness.TrainingDps(fire);
        Assert.True(fireOne > ContentHarness.TrainingDps(plague) * 1.1, "fire wins one target");
        Assert.True(fireOne > ContentHarness.TrainingDps(none), "fire beats no fork on one target");
        // A pack of five: the plague cloud (wider, second dose, copy on every kill) beats fire and the plain cloud.
        var plagueSwarm = ContentHarness.Run(plague, "swarm").DpsPack;
        Assert.True(plagueSwarm > ContentHarness.Run(fire, "swarm").DpsPack * 1.1, "plague wins the pack");
        Assert.True(plagueSwarm > ContentHarness.Run(none, "swarm").DpsPack * 1.1, "plague beats no fork on the pack");
    }

    [Fact]
    void Fire_cloud_burns_and_disables_poison()
    {
        var r = ContentHarness.Run(Cloud("sorcerer", FireLayout), "training");
        Assert.True(r.DamageByType.GetValueOrDefault(DamageType.Fire) > 0);
        Assert.Equal(0, r.DamageByType.GetValueOrDefault(DamageType.Poison));
        var plain = ContentHarness.Run(Cloud("sorcerer", NoForkLayout), "training");
        Assert.Equal(0, plain.DamageByType.GetValueOrDefault(DamageType.Fire));
        Assert.True(plain.DamageByType.GetValueOrDefault(DamageType.Poison) > 0);
    }

    [Fact]
    void Cloud_directions_each_pay_off()
    {
        // Each direction against the same ten-point filler (venom_dose 3, rot 2, heavy_vapor 2 plus the tested nodes).
        double Train(params (string, int)[] n) => ContentHarness.TrainingDps(Cloud("sorcerer", n));
        double Pack(params (string, int)[] n) => ContentHarness.Run(Cloud("sorcerer", n), "swarm").DpsPack;
        (string, int)[] Filler(params (string, int)[] tested)
        {
            var left = 10 - tested.Sum(x => x.Item2);
            var filler = new[] { ("venom_dose", 3), ("rot", 2), ("heavy_vapor", 2), ("caustic_burst", 2), ("erosion", 3) };
            var rest = new List<(string, int)>();
            foreach (var f in filler)
            {
                var take = Math.Min(f.Item2, left);
                if (take > 0) rest.Add((f.Item1, take));
                left -= take;
            }
            return tested.Concat(rest).ToArray();
        }
        var baseline = Train(Filler());
        // Stacking: cooldown recovery lets clouds overlap.
        Assert.True(Train(Filler(("quick_brew", 3))) > baseline * 1.3);
        // Duration: a longer cloud with a stronger zone.
        Assert.True(Train(Filler(("long_haze", 3), ("lingering", 3))) > baseline * 1.15);
        // Size: a bigger cloud covers more of the pack.
        Assert.True(Pack(Filler(("thick_mist", 3), ("expansion", 2))) > Pack(Filler()) * 1.1);
        // Toxic echo: an extra half-strength cloud every second use.
        Assert.True(Train(Filler(("quick_brew", 2), ("toxic_echo", 1))) > Train(Filler(("quick_brew", 2))));
    }

    [Fact]
    void Poison_cloud_works_for_both_classes_and_gives_no_resource()
    {
        foreach (var cls in new[] { "sorcerer", "warrior" })
        {
            var r = ContentHarness.Run(Cloud(cls, NoForkLayout), "training");
            Assert.Empty(r.Violations);
            Assert.True(r.DpsSingleTarget > 0, cls);
        }
    }
}
