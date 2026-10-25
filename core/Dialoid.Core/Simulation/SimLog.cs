using System.Globalization;

namespace Dialoid.Core.Simulation;

/// <summary>Bounded event log: after <see cref="Cap"/> events logging stops with one "log_truncated" event.</summary>
internal sealed class SimLog
{
    public const int Cap = 10_000;

    private readonly List<SimEvent> _events = new();
    private bool _truncated;

    public IReadOnlyList<SimEvent> Events => _events;

    public void Add(double time, string kind, string text)
    {
        if (_truncated) return;
        if (_events.Count >= Cap)
        {
            _events.Add(new SimEvent(time, "log_truncated", $"log capped at {Cap} events"));
            _truncated = true;
            return;
        }
        _events.Add(new SimEvent(time, kind, text));
    }

    public static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
}
