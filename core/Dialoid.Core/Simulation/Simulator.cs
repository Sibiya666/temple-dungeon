using Dialoid.Core.Character;
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
        var setup = FightSetup.Prepare(_data, _mechanics, build, config, out var violations);
        return setup == null ? SimResult.Rejected(violations) : new SimRun(setup, config, StepObserver).Execute();
    }
}
