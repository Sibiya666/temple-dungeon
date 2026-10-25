namespace Dialoid.Core.Simulation;

public enum SimMode { Roll, Average }

public interface IRandomSource
{
    /// <summary>Roll: uniform in [min, max); Average: midpoint.</summary>
    double InRange(double min, double max);

    /// <summary>Probability in [0,1]. Roll: RNG; Average: per-channel accumulator.</summary>
    bool Chance(string channel, double probability);

    /// <summary>Chance is a percent 0..100. Roll: true/false; Average: null (caller uses expectation).</summary>
    bool? CritOrExpected(string channel, double chance);
}

public static class RandomSources
{
    public static IRandomSource Create(SimMode mode, int seed)
        => mode == SimMode.Roll ? new RollRandomSource(seed) : new AverageRandomSource();
}

internal sealed class RollRandomSource : IRandomSource
{
    private ulong _state;

    public RollRandomSource(int seed) => _state = (ulong)(uint)seed;

    // SplitMix64: identical output on every runtime.
    private ulong Next()
    {
        _state += 0x9E3779B97F4A7C15UL;
        var z = _state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    private double NextDouble() => (Next() >> 11) * (1.0 / (1UL << 53));

    public double InRange(double min, double max) => min + (max - min) * NextDouble();

    public bool Chance(string channel, double probability)
    {
        var p = Math.Clamp(probability, 0.0, 1.0);
        if (p <= 0) return false;
        if (p >= 1) return true;
        return NextDouble() < p;
    }

    public bool? CritOrExpected(string channel, double chance) => Chance(channel, chance / 100.0);
}

internal sealed class AverageRandomSource : IRandomSource
{
    private readonly Dictionary<string, double> _counters = new();

    public double InRange(double min, double max) => (min + max) / 2.0;

    public bool Chance(string channel, double probability)
    {
        var p = Math.Clamp(probability, 0.0, 1.0);
        var counter = (_counters.TryGetValue(channel, out var c) ? c : 0.0) + p;
        if (counter >= 1.0 - 1e-9)
        {
            _counters[channel] = counter - 1.0;
            return true;
        }
        _counters[channel] = counter;
        return false;
    }

    public bool? CritOrExpected(string channel, double chance) => null;
}
