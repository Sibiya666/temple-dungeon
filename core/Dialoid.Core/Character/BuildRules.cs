using Dialoid.Core.Data;
using Dialoid.Core.Passives;
using Dialoid.Core.Skills;

namespace Dialoid.Core.Character;

/// <summary>One broken build rule. Code is a stable machine id; Message is for the owner (Russian).</summary>
public sealed record RuleViolation(string Code, string Message);

/// <summary>The single authority on whether a <see cref="Build"/> is legal. Never throws for user-made illegality.</summary>
public static class BuildRules
{
    public const int MaxLevel = 60;
    public const int AscendancyUnlockLevel = 20;
    public const int NativeSkillCap = 20;
    public const int LootSkillBaseCap = 10;
    public const int LootSkillMaxCap = 14;

    private static readonly int[] SlotLevels = { 1, 4, 8, 14, 20 };

    public static int SlotsUnlocked(int level) => SlotLevels.Count(l => level >= l);

    /// <summary>Native class skill: 20. Loot skill (granted by an equipped item): 10 plus the item bonus, at most 14. Not available: 0.</summary>
    public static int SkillLevelCap(Build build, string skillId, GameData data)
    {
        if (data.Classes.TryGetValue(build.ClassId, out var cls) && cls.SkillIds.Contains(skillId)) return NativeSkillCap;
        var bonuses = build.ItemIds.Distinct()
            .Where(i => data.Items.TryGetValue(i, out var it) && it.GrantsSkill == skillId)
            .Select(i => data.Items[i].SkillLevelBonus).ToList();
        return bonuses.Count == 0 ? 0 : Math.Min(LootSkillMaxCap, LootSkillBaseCap + bonuses.Max());
    }

    public static IReadOnlyList<RuleViolation> Validate(Build build, GameData data)
    {
        var v = new List<RuleViolation>();
        void Add(string code, string message) => v.Add(new RuleViolation(code, message));

        if (build.Level is < 1 or > MaxLevel)
            Add("level_out_of_range", $"Уровень {build.Level} вне диапазона 1–{MaxLevel}");
        if (!data.Classes.TryGetValue(build.ClassId, out var cls))
        {
            Add("unknown_class", $"Неизвестный класс «{build.ClassId}»");
            return v;
        }

        CheckPassives(build, cls, data, Add);
        CheckAscendancy(build, cls, data, Add);

        if (build.WeaponId != null && !data.Weapons.ContainsKey(build.WeaponId))
            Add("unknown_weapon", $"Неизвестное оружие «{build.WeaponId}»");
        foreach (var itemId in build.ItemIds.Where(i => !data.Items.ContainsKey(i)))
            Add("unknown_item", $"Неизвестный предмет «{itemId}»");

        CheckSkills(build, cls, data, Add);
        return v;
    }

    private static void CheckPassives(Build build, ClassDefinition cls, GameData data, Action<string, string> add)
    {
        var tree = data.Passives;
        var known = new List<string>();
        foreach (var dup in build.PassiveNodes.GroupBy(x => x).Where(g => g.Count() > 1))
            add("duplicate_passive_node", $"Пассивный узел «{dup.Key}» взят {dup.Count()} раза");
        foreach (var id in build.PassiveNodes.Distinct())
        {
            if (!tree.Nodes.TryGetValue(id, out var node)) add("unknown_passive_node", $"Неизвестный пассивный узел «{id}»");
            else if (node.Kind == PassiveKind.Start) add("start_node_in_build", $"Стартовый узел «{id}» нельзя брать в билд: он бесплатный");
            else known.Add(id);
        }

        int allowed = PassiveTree.PointsForLevel(build.Level);
        if (known.Count > allowed)
            add("too_many_passive_points", $"Пассивных узлов {known.Count}, а очков на уровне {build.Level}: {allowed}");

        foreach (var id in known.Where(id => tree.Nodes[id].Kind == PassiveKind.Attribute && !build.AttributeChoices.ContainsKey(id)))
            add("attribute_choice_missing", $"Для узла атрибута «{id}» не выбран атрибут");

        if (!tree.IsConnectedToStart(known.Append(cls.PassiveStartNodeId), cls.PassiveStartNodeId))
            add("passive_disconnected", "Часть пассивных узлов не связана со стартовым узлом класса");
    }

    private static void CheckAscendancy(Build build, ClassDefinition cls, GameData data, Action<string, string> add)
    {
        if (build.AscendancyId == null)
        {
            if (build.AscendancyNodes.Count > 0) add("unknown_ascendancy_node", "Узлы асцендента выбраны, но сам асцендант не задан");
            return;
        }
        if (build.Level < AscendancyUnlockLevel)
        {
            add("ascendancy_locked", $"Асцендант открывается на уровне {AscendancyUnlockLevel}, а уровень билда {build.Level}");
            return;
        }
        if (!data.Ascendancies.TryGetValue(build.AscendancyId, out var asc) || asc.ClassId != cls.Id)
        {
            add("ascendancy_wrong_class", $"Асцендант «{build.AscendancyId}» не существует или не принадлежит классу «{cls.Id}»");
            return;
        }
        int spent = 0;
        foreach (var id in build.AscendancyNodes.Distinct())
        {
            var node = asc.Notables.FirstOrDefault(n => n.Id == id);
            if (node == null) add("unknown_ascendancy_node", $"Неизвестный узел асцендента «{id}»");
            else spent += node.Cost;
        }
        int allowed = Ascendancy.PointsForLevel(build.Level);
        if (spent > allowed)
            add("too_many_ascendancy_points", $"Потрачено очков асцендента: {spent}, доступно на уровне {build.Level}: {allowed}");
    }

    private static void CheckSkills(Build build, ClassDefinition cls, GameData data, Action<string, string> add)
    {
        int slotsOpen = SlotsUnlocked(build.Level);
        if (build.Slots.Count > slotsOpen)
            add("slot_locked", $"Занято слотов: {build.Slots.Count}, а на уровне {build.Level} открыто {slotsOpen}");

        WeaponStub? weapon = null;
        if (build.WeaponId != null) data.Weapons.TryGetValue(build.WeaponId, out weapon);
        bool weaponKnown = build.WeaponId == null || weapon != null;

        foreach (var dup in build.Slots.GroupBy(s => s.SkillId).Where(g => g.Count() > 1))
            add("duplicate_skill_in_slots", $"Скилл «{dup.Key}» стоит в нескольких слотах");

        foreach (var slot in build.Slots)
        {
            if (!data.Skills.TryGetValue(slot.SkillId, out var skill))
            {
                add("skill_not_available", $"Неизвестный скилл «{slot.SkillId}»");
                continue;
            }
            bool native = cls.SkillIds.Contains(skill.Id);
            int cap = SkillLevelCap(build, skill.Id, data);
            if (cap == 0)
            {
                add("skill_not_available", $"Скилл «{skill.Id}» недоступен: он не из набора класса и не выдан надетым предметом");
                continue;
            }
            if (native && build.Level < skill.UnlockLevel)
                add("skill_locked_by_level", $"Скилл «{skill.Id}» открывается на уровне {skill.UnlockLevel}, а уровень билда {build.Level}");
            if (slot.Level < 1 || slot.Level > cap || slot.Level > Math.Max(1, build.Level))
                add("skill_level_above_cap", $"Уровень скилла «{skill.Id}» {slot.Level} вне допустимого 1–{Math.Min(cap, Math.Max(1, build.Level))} (кап скилла {cap}, уровень персонажа {build.Level})");

            CheckSkillTree(slot, skill, add);

            if (skill.IsAttack && weaponKnown)
            {
                if (weapon == null) add("weapon_type_mismatch", $"Атаке «{skill.Id}» нужно оружие, а оно не выбрано");
                else if (skill.Base.WeaponTypes.Count > 0 && !skill.Base.WeaponTypes.Contains(weapon.Type))
                    add("weapon_type_mismatch", $"Атака «{skill.Id}» не работает с оружием типа «{weapon.Type}»");
            }
        }

        var slotted = build.Slots.Select(s => s.SkillId).ToHashSet();
        foreach (var id in build.Rotation.Distinct().Where(id => !slotted.Contains(id)))
            add("rotation_unknown_skill", $"В ротации скилл «{id}», которого нет в слотах");
    }

    private static void CheckSkillTree(SkillSlot slot, SkillDefinition skill, Action<string, string> add)
    {
        var points = slot.NodePoints;
        var nodes = skill.Tree.ToDictionary(n => n.Id);
        foreach (var (id, p) in points)
        {
            if (!nodes.TryGetValue(id, out var node))
                add("unknown_skill_node", $"В дереве скилла «{skill.Id}» нет узла «{id}»");
            else if (p < 0 || p > node.MaxPoints)
                add("skill_node_over_max", $"В узле «{id}» скилла «{skill.Id}» очков {p}, максимум {node.MaxPoints}");
        }

        int spent = SkillTree.PointsSpent(points);
        int allowed = SkillTree.PointsForLevel(slot.Level);
        if (spent > allowed)
            add("too_many_skill_points", $"В дереве скилла «{skill.Id}» потрачено {spent} очков, а уровень скилла {slot.Level}");

        foreach (var node in skill.Tree)
            if (points.TryGetValue(node.Id, out var p) && p > 0 && !SkillTree.RequirementsMet(node, points))
                add("skill_node_requirement_unmet", $"Узел «{node.Id}» скилла «{skill.Id}» взят без выполненных требований");

        foreach (var (group, members) in SkillTree.ViolatedGroups(skill, points))
            add("exclusive_group_violation", $"В скилле «{skill.Id}» взято несколько узлов из группы «{group}»: {string.Join(", ", members)}");
    }
}
