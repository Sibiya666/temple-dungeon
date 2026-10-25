using Dialoid.Core.Character;

namespace Dialoid.Core.Tests.Character;

internal static class TestBuilds
{
    public static Build Warrior(int level = 20, string? weapon = null, string? items = null, SkillSlot? slot = null, SkillSlot[]? slots = null)
        => Warrior(level, weapon, items == null ? Array.Empty<string>() : new[] { items }, slot, slots);

    public static Build Warrior(int level, string? weapon, string[] items, SkillSlot? slot = null, SkillSlot[]? slots = null)
    {
        var b = new Build { ClassId = "test_warrior", Level = level, WeaponId = weapon };
        b.ItemIds.AddRange(items);
        if (slot != null) b.Slots.Add(slot);
        if (slots != null) b.Slots.AddRange(slots);
        return b;
    }
}
