using Dialoid.Core.Effects;
using Dialoid.Core.Simulation;
using Dialoid.Sandbox;

namespace Dialoid.Sandbox.Tests;

public class SimulationRunnerTests
{
    private static SandboxSession LoadedWhirlwind()
    {
        var dir = DataLocator.Find(AppContext.BaseDirectory)!;
        var info = ReferenceBuilds.List(dir).Single(i => i.Group == "main" && Path.GetFileName(i.Path) == "warrior_berserker_whirlwind.json");
        var s = SandboxSession.Create(TestData.Data, "warrior");
        Assert.True(ReferenceBuilds.Load(s, info).Ok);
        return s;
    }

    private static readonly SimRequest Training = new("training", SimMode.Average, 1, null, false);

    [Fact]
    public async Task Run_returns_same_dps_as_simulator()
    {
        var s = LoadedWhirlwind();
        var runner = new SimulationRunner(s);

        var outcome = await runner.RunAsync(Training);
        var direct = new Simulator(TestData.Data, MechanicRegistry.Default)
            .Run(s.Build, new SimConfig("training", SimMode.Average, 1));

        Assert.True(direct.DpsSingleTarget > 0);
        Assert.Equal(direct.DpsSingleTarget, outcome.Result.DpsSingleTarget);
        Assert.Equal(direct.DpsPack, outcome.Result.DpsPack);
        Assert.Equal(s.Revision, outcome.Revision);
        Assert.Same(Training, outcome.Request);
        Assert.True(runner.IsCurrent(outcome));
    }

    [Fact]
    public async Task Outcome_is_stale_after_session_change()
    {
        var s = LoadedWhirlwind();
        var runner = new SimulationRunner(s);
        var node = s.AllocatablePassives.OrderBy(x => x, StringComparer.Ordinal).First();

        var task = runner.RunAsync(Training);
        Assert.True(s.TogglePassive(node).Ok);
        var outcome = await task;

        Assert.False(runner.IsCurrent(outcome));
    }

    [Fact]
    public async Task Outcome_carries_revision_captured_at_call_time()
    {
        var s = LoadedWhirlwind();
        var runner = new SimulationRunner(s);
        var before = s.Revision;

        var task = runner.RunAsync(Training);
        Assert.True(s.SetLevel(s.Build.Level - 1).Ok);
        var outcome = await task;

        Assert.Equal(before, outcome.Revision);
        Assert.NotEqual(s.Revision, outcome.Revision);
    }

    [Fact]
    public async Task Illegal_build_returns_rejected_with_violations()
    {
        var s = LoadedWhirlwind();
        Assert.True(s.SetLevel(1).Ok);
        Assert.NotEmpty(s.Violations);

        var outcome = await new SimulationRunner(s).RunAsync(Training);

        Assert.Equal(0, outcome.Result.DpsSingleTarget);
        Assert.NotEmpty(outcome.Result.Violations);
    }
}
