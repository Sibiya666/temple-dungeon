using Dialoid.Core.Character;
using Dialoid.Core.Data;
using Dialoid.Core.Effects;
using Dialoid.Core.Skills;

namespace Dialoid.Core.Simulation;

/// <summary>
/// One offline simulation run: the <see cref="CombatEngine"/> driven by the build's <see cref="Rotation"/> for at most
/// DurationSec / StepSec steps (SurvivalCapSec in defensive mode), so it always terminates.
/// </summary>
internal sealed class SimRun
{
    private readonly CombatEngine _engine;
    private readonly Rotation _rotation;
    private readonly double _stepSec;

    public SimRun(GameData data, MechanicRegistry mechanics, CompiledCharacter character, WeaponStub? weapon,
        IReadOnlyList<SkillRuntime> skills, Rotation rotation, DummyDefinition dummy, SimConfig config,
        Action<SimState>? stepObserver = null)
    {
        _engine = new CombatEngine(data, mechanics, character, weapon, skills, dummy, config, infiniteLife: false)
        {
            StepObserver = stepObserver,
        };
        _rotation = rotation;
        _stepSec = config.StepSec;
    }

    public SimResult Execute()
    {
        var steps = Math.Max(1, (int)Math.Round(_engine.SpanSec / _stepSec));
        for (var i = 0; i < steps && _engine.Step(_rotation); i++) { }
        return _engine.Result();
    }
}
