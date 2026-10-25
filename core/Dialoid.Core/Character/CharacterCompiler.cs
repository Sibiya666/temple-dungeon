using Dialoid.Core.Data;
using Dialoid.Core.Effects;
using Dialoid.Core.Passives;
using Dialoid.Core.Skills;
using Dialoid.Core.Stats;

namespace Dialoid.Core.Character;

public sealed class CharacterCompiler
{
    private static readonly Attribute[] AllAttributes = { Attribute.Str, Attribute.Int, Attribute.Will, Attribute.Dex };

    private readonly GameData _data;
    private readonly MechanicRegistry _mechanics;

    /// <param name="mechanics">Registry whose OnCompile hooks run (default: <see cref="MechanicRegistry.Default"/>).</param>
    public CharacterCompiler(GameData data, MechanicRegistry? mechanics = null)
    {
        _data = data;
        _mechanics = mechanics ?? MechanicRegistry.Default;
    }

    /// <summary>Compiles a build. Callers must run <see cref="BuildRules.Validate"/> first: unknown ids (class, passive, ascendancy, weapon) throw ArgumentException.</summary>
    public CompiledCharacter Compile(Build build)
    {
        if (!_data.Classes.TryGetValue(build.ClassId, out var cls))
            throw new ArgumentException($"Unknown class '{build.ClassId}'.", nameof(build));
        if (build.Level < 1)
            throw new ArgumentException($"Level must be at least 1, got {build.Level}.", nameof(build));

        var mods = new List<Modifier>();
        var classSrc = new SourceRef("class", cls.Id, cls.Name);
        var levelSrc = new SourceRef("level", "level", $"Уровень {build.Level}");
        int grow = build.Level - 1;

        // Attributes: class base, +1 all per level, +1 primary per level.
        foreach (var a in AllAttributes)
        {
            var stat = a.StatId();
            if (cls.BaseAttributes.TryGetValue(a, out var baseValue) && baseValue != 0)
                mods.Add(new Modifier(stat, ModKind.Flat, baseValue, TagSet.Empty, Array.Empty<Condition>(), null, classSrc));
            if (grow > 0)
                mods.Add(new Modifier(stat, ModKind.Flat, grow, TagSet.Empty, Array.Empty<Condition>(), null, levelSrc));
        }
        if (grow > 0)
            mods.Add(new Modifier(cls.Primary.StatId(), ModKind.Flat, grow, TagSet.Empty, Array.Empty<Condition>(), null, levelSrc));

        // General attribute bonuses and the primary attribute damage multiplier (data-driven).
        var rules = _data.AttributeRules;
        foreach (var b in rules.Bonuses)
        {
            var src = new SourceRef("attribute", b.Attribute.StatId(), $"Атрибут: {b.Attribute.StatId()}");
            mods.Add(new Modifier(b.Stat, b.Kind, b.ValuePerPoint, TagSet.Empty, Array.Empty<Condition>(),
                new PerStat(b.Attribute.StatId(), 1, null), src));
        }
        if (rules.PrimaryDamageMorePerPoint != 0)
        {
            var p = cls.Primary.StatId();
            mods.Add(new Modifier("damage", ModKind.More, rules.PrimaryDamageMorePerPoint, TagSet.Empty,
                Array.Empty<Condition>(), new PerStat(p, 1, null), new SourceRef("attribute", p, $"Основной атрибут: {p}")));
        }

        // Life: 100 + 11 per level, then the class multiplier as a More modifier.
        mods.Add(new Modifier("life", ModKind.Flat, 100 + 11 * build.Level, TagSet.Empty, Array.Empty<Condition>(), null, levelSrc));
        if (cls.LifeMultiplier != 1)
            mods.Add(new Modifier("life", ModKind.More, (cls.LifeMultiplier - 1) * 100, TagSet.Empty,
                Array.Empty<Condition>(), null, classSrc));

        // Resource.
        var res = cls.Resource;
        var resSrc = new SourceRef("class", cls.Id, $"{cls.Name}: {res.Name}");
        mods.Add(new Modifier("resource_max", ModKind.Flat, res.BaseMax, TagSet.Empty, Array.Empty<Condition>(), null, resSrc));
        if (res.MaxPer is { } maxPer)
            mods.Add(new Modifier("resource_max", ModKind.Flat, 1, TagSet.Empty, Array.Empty<Condition>(), maxPer, resSrc));
        if (res.RegenPerSec != 0)
            mods.Add(new Modifier("resource_regen", ModKind.Flat, res.RegenPerSec, TagSet.Empty, Array.Empty<Condition>(), null, resSrc));

        mods.AddRange(cls.ClassBonuses);

        if (build.WeaponId != null)
        {
            if (!_data.Weapons.TryGetValue(build.WeaponId, out var weapon))
                throw new ArgumentException($"Unknown weapon '{build.WeaponId}'.", nameof(build));
            var weaponSrc = new SourceRef("weapon", weapon.Id, weapon.Name);
            foreach (var m in weapon.Implicits) mods.Add(m with { Source = weaponSrc });
        }

        var mechanics = new List<MechanicEffect>();
        var sourcedMechanics = new List<SourcedEffect>();
        var deferred = new List<SourcedEffect>();
        AddPassives(build, cls, mods, mechanics, sourcedMechanics, deferred);
        AddAscendancy(build, cls, mods, mechanics, sourcedMechanics, deferred);

        // Skills compile from data alone; then mechanic hooks: the character phase edits the modifier pool (it can see the
        // compiled skills), then the skill phase edits each compiled skill.
        var compiledSkills = CompileSkills(build, cls, mechanics, deferred);
        var globals = MechanicCompiler.Order(sourcedMechanics);
        var characterRules = new Dictionary<string, double>();
        MechanicCompiler.CharacterPhase(_mechanics, _data, build, cls, globals, mods, characterRules, compiledSkills);
        var evaluator = new StatEvaluator(_data.Stats, mods);

        var skills = compiledSkills
            .Select(s => MechanicCompiler.SkillPhase(_mechanics, _data, build, cls, globals, evaluator, s, compiledSkills)).ToList();
        return new CompiledCharacter(build, cls, evaluator, mods, skills, mechanics, deferred)
        {
            GlobalMechanics = globals,
            Rules = characterRules,
        };
    }

    private IReadOnlyList<CompiledSkill> CompileSkills(Build build, ClassDefinition cls, List<MechanicEffect> mechanics, List<SourcedEffect> deferred)
    {
        IReadOnlyList<SourcedEffect> Of(Func<string, bool> kind) => deferred.Where(d => kind(d.Source.Kind)).ToList();
        var granted = mechanics.Where(m => m.MechanicId == SkillCompiler.GrantSkillNodeId)
            .Select(m => (SkillId: GrantParam(m, "skill"), NodeId: GrantParam(m, "node"))).ToList();
        var ctx = new CompilationContext(_data, Of(k => k == "ascendancy"), Of(k => k == "keystone"),
            Of(k => k == "passive"), granted, cls.Id);
        var result = new List<CompiledSkill>();
        foreach (var slot in build.Slots)
        {
            if (!_data.Skills.TryGetValue(slot.SkillId, out var def))
                throw new ArgumentException($"Unknown skill '{slot.SkillId}'.", nameof(build));
            result.Add(SkillCompiler.Compile(slot, def, ctx));
        }
        return result;
    }

    private static string GrantParam(MechanicEffect m, string name) =>
        m.Params.TryGetValue(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String
            ? v.GetString()!
            : throw new ArgumentException($"Mechanic '{m.MechanicId}' needs a string param '{name}'.");

    private void AddPassives(Build build, ClassDefinition cls, List<Modifier> mods, List<MechanicEffect> mechanics, List<SourcedEffect> sourcedMechanics, List<SourcedEffect> deferred)
    {
        foreach (var id in build.PassiveNodes.Distinct())
        {
            if (!_data.Passives.Nodes.TryGetValue(id, out var node))
                throw new ArgumentException($"Unknown passive node '{id}'.", nameof(build));
            if (node.Kind == PassiveKind.Start) continue; // the class start is free and carries no allocation
            var src = new SourceRef(node.Kind == PassiveKind.Keystone ? "keystone" : "passive", node.Id, node.Name);
            if (node.Kind == PassiveKind.Attribute)
            {
                if (!build.AttributeChoices.TryGetValue(id, out var choice))
                    throw new ArgumentException($"Attribute node '{id}' has no attribute choice.", nameof(build));
                mods.Add(new Modifier(choice.StatId(), ModKind.Flat, PassiveTree.AttributeNodeBonus, TagSet.Empty,
                    Array.Empty<Condition>(), null, src));
            }
            AddEffects(node.Effects, src, mods, mechanics, sourcedMechanics, deferred);
        }
    }

    private void AddAscendancy(Build build, ClassDefinition cls, List<Modifier> mods, List<MechanicEffect> mechanics, List<SourcedEffect> sourcedMechanics, List<SourcedEffect> deferred)
    {
        if (build.AscendancyId == null) return;
        if (!_data.Ascendancies.TryGetValue(build.AscendancyId, out var asc) || asc.ClassId != cls.Id)
            throw new ArgumentException($"Unknown ascendancy '{build.AscendancyId}' for class '{cls.Id}'.", nameof(build));
        AddEffects(asc.Start.Effects, new SourceRef("ascendancy", asc.Start.Id, asc.Start.Name), mods, mechanics, sourcedMechanics, deferred);
        foreach (var id in build.AscendancyNodes.Distinct())
        {
            var node = asc.Notables.FirstOrDefault(n => n.Id == id)
                ?? throw new ArgumentException($"Unknown ascendancy node '{id}' in '{asc.Id}'.", nameof(build));
            AddEffects(node.Effects, new SourceRef("ascendancy", node.Id, node.Name), mods, mechanics, sourcedMechanics, deferred);
        }
    }

    /// <summary>Global modifiers go to the stat pool, mechanics to the mechanic list, everything else waits for skill compilation.</summary>
    private static void AddEffects(IReadOnlyList<Effect> effects, SourceRef src, List<Modifier> mods,
        List<MechanicEffect> mechanics, List<SourcedEffect> sourcedMechanics, List<SourcedEffect> deferred)
    {
        foreach (var e in effects)
            switch (e)
            {
                case ModifierEffect { Scope: Scope.Global } me: mods.Add(me.Modifier with { Source = src }); break;
                case MechanicEffect mech:
                    mechanics.Add(mech);
                    sourcedMechanics.Add(new SourcedEffect(mech, src));
                    if (mech.Scope is not Scope.Global) deferred.Add(new SourcedEffect(mech, src)); // also lands on matching skills
                    break;
                case ModifierEffect me: deferred.Add(new SourcedEffect(me with { Modifier = me.Modifier with { Source = src } }, src)); break;
                default: deferred.Add(new SourcedEffect(e, src)); break;
            }
    }
}
