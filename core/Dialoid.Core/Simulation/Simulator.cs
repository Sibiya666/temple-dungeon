using Dialoid.Core.Character;
using Dialoid.Core.Skills;
using Dialoid.Core.Data;
using Dialoid.Core.Effects;

namespace Dialoid.Core.Simulation;

/// <summary>
/// Deterministic fixed-step combat simulator (spec 11). Validates the build, compiles it, then runs one
/// <see cref="SimRun"/>. Never throws for user-made illegality: such builds return an empty result with violations.
/// </summary>
public sealed class Simulator
{
    public const int MaxSteps = 1_000_000;

    private readonly GameData _data;
    private readonly MechanicRegistry _mechanics;

    public Simulator(GameData data, MechanicRegistry mechanics)
    {
        _data = data;
        _mechanics = mechanics;
    }

    /// <summary>Test/debug hook: called once per step after the character acted, with the live state.</summary>
    internal Action<SimState>? StepObserver { get; set; }

    public SimResult Run(Build build, SimConfig config)
    {
        build = BuildRules.Normalized(build); // a hand-made build with null lists compiles as if they were empty
        var violations = new List<RuleViolation>(BuildRules.Validate(build, _data));
        if (!_data.Dummies.TryGetValue(config.DummyId, out var dummy))
            violations.Add(new RuleViolation("unknown_dummy", $"Неизвестный манекен «{config.DummyId}»"));
        // In defensive mode the run lasts until death or the survival cap, so the cap replaces DurationSec.
        var span = config.DummyAttacks && dummy?.Attack != null ? config.SurvivalCapSec : config.DurationSec;
        if (!(config.StepSec > 0) || !(span > 0) || double.IsInfinity(span))
            violations.Add(new RuleViolation("invalid_sim_config", "Длительность и шаг симуляции должны быть положительными"));
        else if (config.StepSec > span)
            violations.Add(new RuleViolation("invalid_sim_config", "Шаг симуляции не может быть больше её длительности"));
        else if (span / config.StepSec > MaxSteps)
            violations.Add(new RuleViolation("invalid_sim_config", $"Слишком много шагов симуляции: больше {MaxSteps}"));
        if (config.PackSizeOverride is < 1 or > DummyDefinition.MaxPackSize)
            violations.Add(new RuleViolation("invalid_sim_config", $"Размер пака должен быть от 1 до {DummyDefinition.MaxPackSize}"));
        if (violations.Count > 0) return SimResult.Rejected(violations);

        var character = new CharacterCompiler(_data, _mechanics).Compile(build);
        WeaponStub? weapon = build.WeaponId != null ? _data.Weapons[build.WeaponId] : null;
        var skills = character.Skills.Select(s => new SkillRuntime(_data, character, s)).ToList();
        var run = new SimRun(_data, _mechanics, character, weapon, skills, new Rotation(build, skills), dummy!, config)
        {
            StepObserver = StepObserver,
        };
        return run.Execute();
    }
}
