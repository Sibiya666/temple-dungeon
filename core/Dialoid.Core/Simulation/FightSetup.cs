using Dialoid.Core.Character;
using Dialoid.Core.Data;
using Dialoid.Core.Effects;
using Dialoid.Core.Skills;

namespace Dialoid.Core.Simulation;

/// <summary>
/// Everything a fight needs before its first step: the normalized build, the compiled character, the weapon, the slotted skills
/// (in slot order) and the dummy. The one place that turns a build into a <see cref="CombatEngine"/>, shared by the offline
/// <see cref="Simulator"/>, <see cref="LiveFight"/> and the tests, so their setups cannot drift apart.
/// </summary>
internal sealed class FightSetup
{
    private readonly GameData _data;
    private readonly MechanicRegistry _mechanics;

    public Build Build { get; }
    public CompiledCharacter Character { get; }
    public WeaponStub? Weapon { get; }
    /// <summary>One runtime per build slot, in slot order.</summary>
    public IReadOnlyList<SkillRuntime> Skills { get; }
    public DummyDefinition Dummy { get; }

    private FightSetup(GameData data, MechanicRegistry mechanics, Build build, DummyDefinition dummy)
    {
        _data = data;
        _mechanics = mechanics;
        Build = build;
        Character = new CharacterCompiler(data, mechanics).Compile(build);
        Weapon = build.WeaponId != null ? data.Weapons[build.WeaponId] : null;
        Skills = Character.Skills.Select(s => new SkillRuntime(data, Character, s)).ToList();
        Dummy = dummy;
    }

    /// <summary>
    /// The simulator's checks (build rules, dummy id, step/duration, pack size), then the setup. Returns null with the violations
    /// when anything is illegal; never throws for user-made illegality.
    /// </summary>
    public static FightSetup? Prepare(GameData data, MechanicRegistry mechanics, Build build, SimConfig config,
        out IReadOnlyList<RuleViolation> violations)
    {
        build = BuildRules.Normalized(build); // a hand-made build with null lists compiles as if they were empty
        var found = new List<RuleViolation>(BuildRules.Validate(build, data));
        if (!data.Dummies.TryGetValue(config.DummyId, out var dummy))
            found.Add(new RuleViolation("unknown_dummy", $"Неизвестный манекен «{config.DummyId}»"));
        // In defensive mode the run lasts until death or the survival cap, so the cap replaces DurationSec.
        var span = config.DummyAttacks && dummy?.Attack != null ? config.SurvivalCapSec : config.DurationSec;
        if (!(config.StepSec > 0) || !(span > 0) || double.IsInfinity(span))
            found.Add(new RuleViolation("invalid_sim_config", "Длительность и шаг симуляции должны быть положительными"));
        else if (config.StepSec > span)
            found.Add(new RuleViolation("invalid_sim_config", "Шаг симуляции не может быть больше её длительности"));
        else if (span / config.StepSec > Simulator.MaxSteps)
            found.Add(new RuleViolation("invalid_sim_config", $"Слишком много шагов симуляции: больше {Simulator.MaxSteps}"));
        if (config.PackSizeOverride is < 1 or > DummyDefinition.MaxPackSize)
            found.Add(new RuleViolation("invalid_sim_config", $"Размер пака должен быть от 1 до {DummyDefinition.MaxPackSize}"));
        violations = found;
        return found.Count > 0 ? null : new FightSetup(data, mechanics, build, dummy!);
    }

    /// <summary>The setup without any rule check (tests: hand-made builds the rules would reject).</summary>
    public static FightSetup Unchecked(GameData data, MechanicRegistry mechanics, Build build, string dummyId) =>
        new(data, mechanics, BuildRules.Normalized(build), data.Dummies[dummyId]);

    /// <summary>The engine over this setup's character and skills. One engine per setup: the skill runtimes carry the fight's
    /// active buffs.</summary>
    public CombatEngine Engine(SimConfig config, bool infiniteLife, Action<SimState>? stepObserver = null, SimLog? log = null,
        int damageKeep = 0, double? lifePerTarget = null) =>
        new(_data, _mechanics, Character, Weapon, Skills, Dummy, config, infiniteLife, log, damageKeep, lifePerTarget)
        {
            StepObserver = stepObserver,
        };

    /// <summary>The game data the setup was made from.</summary>
    public GameData Data => _data;

    /// <summary>The build's priority rotation over this setup's skills.</summary>
    public Rotation Rotation() => new(Build, Skills);
}
