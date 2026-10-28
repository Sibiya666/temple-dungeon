using Dialoid.Core.Effects;
using Dialoid.Core.Simulation;

namespace Dialoid.Sandbox;

public sealed record SimRequest(string DummyId, SimMode Mode, int Seed, int? PackSize, bool DummyAttacks);

/// <summary>The result of one run with the session revision it was started from.</summary>
public sealed record SimOutcome(int Revision, SimRequest Request, SimResult Result);

/// <summary>
/// Runs Dialoid.Core's simulator off the calling thread. Threading contract: the session is single-threaded (UI thread).
/// <see cref="RunAsync"/> must be called on that thread; it takes the build snapshot (a deep copy) and the revision
/// synchronously before any await, and the worker only touches that copy, never the session. <see cref="IsCurrent"/>
/// also compares on the calling thread.
/// </summary>
public sealed class SimulationRunner
{
    private readonly SandboxSession _session;
    private readonly Simulator _simulator;

    public SimulationRunner(SandboxSession s)
    {
        _session = s;
        _simulator = new Simulator(s.Data, MechanicRegistry.Default);
    }

    public Task<SimOutcome> RunAsync(SimRequest r, CancellationToken ct = default)
    {
        var build = _session.Build;
        var revision = _session.Revision;
        var config = new SimConfig(r.DummyId, r.Mode, r.Seed, PackSizeOverride: r.PackSize, DummyAttacks: r.DummyAttacks);
        return Task.Run(() => new SimOutcome(revision, r, _simulator.Run(build, config)), ct);
    }

    public bool IsCurrent(SimOutcome o) => o.Revision == _session.Revision;
}
