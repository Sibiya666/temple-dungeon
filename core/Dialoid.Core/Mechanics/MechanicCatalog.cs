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
    }
}
