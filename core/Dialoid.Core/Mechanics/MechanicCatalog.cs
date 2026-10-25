using Dialoid.Core.Effects;

namespace Dialoid.Core.Mechanics;

/// <summary>Every mechanic implemented in code (spec 4.6). <see cref="MechanicRegistry.Default"/> registers them all.</summary>
public static class MechanicCatalog
{
    public static void RegisterAll(MechanicRegistry registry)
    {
        // Keystones (spec 7.4)
        registry.Register(new BloodPact());
        registry.Register(new TitanWill());
        registry.Register(new Inevitability());
        registry.Register(new AvatarOfFire());
        registry.Register(new BladeDance());
        registry.Register(new StoneSkin());

        // Berserker (spec 10.4)
        registry.Register(new BerserkFrenzy());
        registry.Register(new Insatiable());
        registry.Register(new Reckless());
        registry.Register(new WhirlRage());
        registry.Register(new FleshRend());
        registry.Register(new BattleTrance());
        registry.Register(new BloodFeast());

        // Thunder (spec 10.4)
        registry.Register(new StaticCharge());
        registry.Register(new Grounding());
        registry.Register(new GroundType());

        // Pyromancer (spec 10.4)
        registry.Register(new SpreadIgnite());
        registry.Register(new Ashes());
        registry.Register(new EternalFire());
        registry.Register(new SoulHeat());
        registry.Register(new FireArmor());

        // Cryomancer (spec 10.4)
        registry.Register(new Shatter());
        registry.Register(new IceArmor());
        registry.Register(new GrantSkillNode());
        registry.Register(new AilmentSwap());
        registry.Register(new Brittle());
        registry.Register(new ChillSlow());
    }
}
