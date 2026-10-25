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
        var violations = new List<RuleViolation>(BuildRules.Validate(build, _data));
        if (!_data.Dummies.TryGetValue(config.DummyId, out var dummy))
            violations.Add(new RuleViolation("unknown_dummy", $"Неизвестный манекен «{config.DummyId}»"));
        if (!(config.StepSec > 0) || !(config.DurationSec > 0) || double.IsInfinity(config.DurationSec))
            violations.Add(new RuleViolation("invalid_sim_config", "Длительность и шаг симуляции должны быть положительными"));
        if (config.PackSizeOverride is < 1 or > DummyDefinition.MaxPackSize)
            violations.Add(new RuleViolation("invalid_sim_config", $"Размер пака должен быть от 1 до {DummyDefinition.MaxPackSize}"));
        if (violations.Count > 0) return SimResult.Rejected(violations);

        var character = new CharacterCompiler(_data).Compile(build);
        WeaponStub? weapon = build.WeaponId != null ? _data.Weapons[build.WeaponId] : null;
        var skills = character.Skills.Select(s => new SkillRuntime(_data, character, s)).ToList();
        var run = new SimRun(_data, _mechanics, character, weapon, skills, new Rotation(build, skills), dummy!, config)
        {
            StepObserver = StepObserver,
        };
        return run.Execute();
    }
}
