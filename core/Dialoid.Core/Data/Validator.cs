using Dialoid.Core.Character;
using Dialoid.Core.Effects;
using Dialoid.Core.Passives;
using Dialoid.Core.Skills;
using Dialoid.Core.Stats;

namespace Dialoid.Core.Data;

/// <summary>
/// Whole-data checks that need several catalogs at once. Runs inside <see cref="DataLoader.Load"/> after every
/// catalog loaded cleanly. One private method per check; later tasks add their checks here.
/// </summary>
public static class Validator
{
    public static IReadOnlyList<DataError> Validate(GameData data, MechanicRegistry mechanics)
    {
        var errors = new List<DataError>();
        CheckClassReferences(data, errors);
        CheckAscendancyReferences(data, errors);
        CheckStartNodes(data, errors);
        CheckPassiveEdges(data, errors);
        CheckTreeConnectivity(data, errors);
        CheckSkillReferences(data, errors);
        CheckSkillTrees(data, errors);
        CheckItemAndWeaponReferences(data, errors);
        CheckEffectReferences(data, mechanics, errors);
        CheckStatCycles(data, errors);
        return errors;
    }

    private static void CheckClassReferences(GameData data, List<DataError> errors)
    {
        const string F = "classes.json";
        foreach (var c in data.Classes.Values)
        {
            if (!data.Passives.Nodes.TryGetValue(c.PassiveStartNodeId, out var start))
                errors.Add(new DataError(F, c.Id, $"Неизвестный стартовый узел пассивного дерева «{c.PassiveStartNodeId}»"));
            else if (start.StartForClassId != c.Id)
                errors.Add(new DataError(F, c.Id, $"Узел «{start.Id}» не является стартовым узлом этого класса (в passives.json его «start_for_class»: «{start.StartForClassId}»)"));

            foreach (var ascId in c.AscendancyIds)
            {
                if (!data.Ascendancies.TryGetValue(ascId, out var asc))
                    errors.Add(new DataError(F, c.Id, $"Неизвестный асцендант «{ascId}»"));
                else if (asc.ClassId != c.Id)
                    errors.Add(new DataError(F, c.Id, $"Асцендант «{ascId}» принадлежит другому классу («{asc.ClassId}»)"));
            }
        }
    }

    private static void CheckAscendancyReferences(GameData data, List<DataError> errors)
    {
        const string F = "ascendancies.json";
        foreach (var a in data.Ascendancies.Values)
        {
            if (!data.Classes.TryGetValue(a.ClassId, out var cls))
                errors.Add(new DataError(F, a.Id, $"Неизвестный класс «{a.ClassId}»"));
            else if (!cls.AscendancyIds.Contains(a.Id))
                errors.Add(new DataError(F, a.Id, $"Класс «{a.ClassId}» не перечисляет этот асцендант в «ascendancies»"));
        }
    }

    private static void CheckStartNodes(GameData data, List<DataError> errors)
    {
        const string F = "passives.json";
        var starts = data.Passives.Nodes.Values.Where(n => n.StartForClassId != null).ToList();
        foreach (var n in starts.Where(n => !data.Classes.ContainsKey(n.StartForClassId!)))
            errors.Add(new DataError(F, n.Id, $"Стартовый узел относится к неизвестному классу «{n.StartForClassId}»"));
        foreach (var c in data.Classes.Values)
        {
            var count = starts.Count(n => n.StartForClassId == c.Id);
            if (count != 1)
                errors.Add(new DataError(F, c.Id, $"У класса «{c.Id}» стартовых узлов: {count}, а должен быть ровно один"));
        }
    }

    private static void CheckPassiveEdges(GameData data, List<DataError> errors)
    {
        foreach (var (id, targets) in data.Passives.Edges)
            foreach (var to in targets.Where(t => !data.Passives.Nodes.ContainsKey(t)))
                errors.Add(new DataError("passives.json", id, $"Связь ведёт к неизвестному узлу «{to}»"));
    }

    private static void CheckTreeConnectivity(GameData data, List<DataError> errors)
    {
        var tree = data.Passives;
        var seen = new HashSet<string>();
        var queue = new Queue<string>();
        foreach (var n in tree.Nodes.Values.Where(n => n.Kind == PassiveKind.Start))
            if (seen.Add(n.Id)) queue.Enqueue(n.Id);
        while (queue.Count > 0)
            foreach (var next in tree.Edges[queue.Dequeue()])
                if (tree.Nodes.ContainsKey(next) && seen.Add(next)) queue.Enqueue(next);
        foreach (var n in tree.Nodes.Values.Where(n => !seen.Contains(n.Id)))
            errors.Add(new DataError("passives.json", n.Id, "Узел недостижим ни от одного стартового узла"));
    }

    private static void CheckSkillReferences(GameData data, List<DataError> errors)
    {
        const string F = "skills.json";
        foreach (var sk in data.Skills.Values)
        {
            if (sk.ClassId != null && !data.Classes.ContainsKey(sk.ClassId))
                errors.Add(new DataError(F, sk.Id, $"Неизвестный класс «{sk.ClassId}»"));
            foreach (var wt in sk.Base.WeaponTypes.Where(t => !WeaponTypes.All.Contains(t)))
                errors.Add(new DataError(F, sk.Id, $"Неизвестный тип оружия «{wt}» в «weapon_types» (ожидается one_hand, two_hand, staff или wand)"));
            foreach (var key in sk.Base.AilmentChances.Keys.Where(k => !data.Ailments.ContainsKey(k)))
                errors.Add(new DataError(F, sk.Id, $"Неизвестный недуг «{key}» в «ailment_chances»"));
        }
        foreach (var c in data.Classes.Values)
            foreach (var skillId in c.SkillIds)
            {
                if (!data.Skills.TryGetValue(skillId, out var sk))
                    errors.Add(new DataError("classes.json", c.Id, $"Неизвестный скилл «{skillId}»"));
                else if (sk.ClassId != c.Id)
                    errors.Add(new DataError("classes.json", c.Id, $"Скилл «{skillId}» принадлежит другому классу («{sk.ClassId ?? "null"}»)"));
            }
    }

    private static void CheckSkillTrees(GameData data, List<DataError> errors)
    {
        const string F = "skills.json";
        foreach (var sk in data.Skills.Values)
        {
            var nodes = sk.Tree.ToDictionary(n => n.Id);
            foreach (var n in sk.Tree)
                foreach (var (req, points) in n.Requires)
                {
                    if (!nodes.TryGetValue(req, out var target))
                        errors.Add(new DataError(F, sk.Id, $"Узел «{n.Id}» требует неизвестный узел «{req}»"));
                    else if (points > target.MaxPoints)
                        errors.Add(new DataError(F, sk.Id, $"Узел «{n.Id}» требует {points} очков в «{req}», а там максимум {target.MaxPoints}"));
                }
            if (SkillTree.FindRequirementCycle(sk.Tree) is { } cycle)
                errors.Add(new DataError(F, sk.Id, $"Цикл требований в дереве скилла: {string.Join(" → ", cycle)}"));
        }
    }

    private static void CheckItemAndWeaponReferences(GameData data, List<DataError> errors)
    {
        foreach (var it in data.Items.Values)
            if (!data.Skills.ContainsKey(it.GrantsSkill))
                errors.Add(new DataError("items.json", it.Id, $"Предмет выдает неизвестный скилл «{it.GrantsSkill}»"));
        foreach (var w in data.Weapons.Values)
        {
            if (!WeaponTypes.All.Contains(w.Type))
                errors.Add(new DataError("weapons.json", w.Id, $"Неизвестный тип оружия «{w.Type}» (ожидается one_hand, two_hand, staff или wand)"));
            foreach (var m in w.Implicits)
            {
                if (!data.Stats.ContainsKey(m.Stat))
                    errors.Add(new DataError("weapons.json", w.Id, $"Имплицит: неизвестный стат «{m.Stat}»"));
                if (m.Per is { } per && !data.Stats.ContainsKey(per.Stat))
                    errors.Add(new DataError("weapons.json", w.Id, $"Имплицит: неизвестный стат «{per.Stat}» в «per»"));
            }
        }
    }

    private static void CheckEffectReferences(GameData data, MechanicRegistry mechanics, List<DataError> errors)
    {
        foreach (var n in data.Passives.Nodes.Values)
            CheckEffects("passives.json", n.Id, n.Effects, null);
        foreach (var a in data.Ascendancies.Values)
            foreach (var node in a.Notables.Prepend(a.Start))
                CheckEffects("ascendancies.json", a.Id, node.Effects, node.Id);
        foreach (var sk in data.Skills.Values)
            foreach (var node in sk.Tree)
                CheckEffects("skills.json", sk.Id, node.Effects, node.Id);

        void CheckEffects(string file, string ownerId, IReadOnlyList<Effect> effects, string? nodeId)
        {
            var where = nodeId == null ? "" : $"Узел «{nodeId}»: ";
            foreach (var e in effects)
                switch (e)
                {
                    case ModifierEffect me:
                        if (!data.Stats.ContainsKey(me.Modifier.Stat))
                            errors.Add(new DataError(file, ownerId, $"{where}неизвестный стат «{me.Modifier.Stat}»"));
                        if (me.Modifier.Per is { } per && !data.Stats.ContainsKey(per.Stat))
                            errors.Add(new DataError(file, ownerId, $"{where}неизвестный стат «{per.Stat}» в «per»"));
                        break;
                    case MechanicEffect mech when !mechanics.Contains(mech.MechanicId):
                        errors.Add(new DataError(file, ownerId, $"{where}неизвестная механика «{mech.MechanicId}»"));
                        break;
                }
        }
    }

    /// <summary>Per-stat dependencies ("X per Y") must be acyclic over every modifier the data can produce.</summary>
    private static void CheckStatCycles(GameData data, List<DataError> errors)
    {
        var mods = new List<Modifier>();
        var none = Array.Empty<Condition>();
        foreach (var c in data.Classes.Values)
        {
            mods.AddRange(c.ClassBonuses);
            var src = new SourceRef("class", c.Id, c.Name);
            if (c.Resource.MaxPer is { } maxPer)
                mods.Add(new Modifier("resource_max", ModKind.Flat, 1, TagSet.Empty, none, maxPer, src));
            if (data.AttributeRules.PrimaryDamageMorePerPoint != 0)
                mods.Add(new Modifier("damage", ModKind.More, data.AttributeRules.PrimaryDamageMorePerPoint, TagSet.Empty, none,
                    new PerStat(c.Primary.StatId(), 1, null), new SourceRef("attribute", c.Primary.StatId(), c.Primary.StatId())));
        }
        foreach (var b in data.AttributeRules.Bonuses)
            mods.Add(new Modifier(b.Stat, b.Kind, b.ValuePerPoint, TagSet.Empty, none,
                new PerStat(b.Attribute.StatId(), 1, null), new SourceRef("attribute", b.Attribute.StatId(), b.Attribute.StatId())));
        foreach (var w in data.Weapons.Values)
            mods.AddRange(w.Implicits);
        foreach (var n in data.Passives.Nodes.Values)
            mods.AddRange(n.Effects.OfType<ModifierEffect>().Select(e => e.Modifier));
        foreach (var a in data.Ascendancies.Values)
            foreach (var node in a.Notables.Prepend(a.Start))
                mods.AddRange(node.Effects.OfType<ModifierEffect>().Select(e => e.Modifier));

        if (StatGraph.FindCycle(mods) is { } cycle)
            errors.Add(new DataError("stats.json", cycle[0], $"Цикл зависимостей статов: {string.Join(" → ", cycle)}"));
    }
}
