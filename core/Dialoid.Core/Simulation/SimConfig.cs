namespace Dialoid.Core.Simulation;

/// <param name="DummyId">Dummy preset id from dummies.json.</param>
/// <param name="StepSec">Fixed time step; the loop always runs DurationSec / StepSec steps at most.</param>
/// <param name="DummyAttacks">"Dummy hits the character" mode (Task 15).</param>
public sealed record SimConfig(string DummyId, SimMode Mode, int Seed = 1, double DurationSec = 60, double StepSec = 0.05,
    int? PackSizeOverride = null, bool DummyAttacks = false, double SurvivalCapSec = 180);

/// <summary>One log line. Kind: use|hit|crit|ailment|kill|no_resource|death|proc|log_truncated.</summary>
public sealed record SimEvent(double Time, string Kind, string Text);
