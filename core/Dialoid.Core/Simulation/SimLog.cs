using System.Globalization;

namespace Dialoid.Core.Simulation;

/// <summary>
/// Event log. Capped (the simulator): after <see cref="Cap"/> events logging stops with one "log_truncated" event.
/// Rolling (live fights, <see cref="Rolling"/>): never stops; <see cref="Events"/> keeps only the most recent events and
/// <see cref="Drain"/> hands out the events since the previous drain, both bounded so an endless fight uses bounded memory.
/// </summary>
internal sealed class SimLog
{
    public const int Cap = 10_000;

    private readonly List<SimEvent> _events = new();
    private readonly List<SimEvent>? _pending;
    private readonly int _keep;
    private bool _truncated;

    /// <summary>The simulator's capped log.</summary>
    public SimLog() { }

    private SimLog(int keep)
    {
        _keep = keep;
        _pending = new List<SimEvent>();
    }

    /// <summary>A log that never stops: <see cref="Events"/> holds the last <paramref name="keep"/> to 2 × <paramref name="keep"/>
    /// events, and so do the undrained events (when nobody drains, the oldest are dropped).</summary>
    public static SimLog Rolling(int keep) =>
        keep > 0 ? new SimLog(keep) : throw new ArgumentOutOfRangeException(nameof(keep), "keep must be positive");

    public IReadOnlyList<SimEvent> Events => _events;

    public void Add(double time, string kind, string text)
    {
        if (_pending != null)
        {
            var e = new SimEvent(time, kind, text);
            Append(_events, e);
            Append(_pending, e);
            return;
        }
        if (_truncated) return;
        if (_events.Count >= Cap)
        {
            _events.Add(new SimEvent(time, "log_truncated", $"log capped at {Cap} events"));
            _truncated = true;
            return;
        }
        _events.Add(new SimEvent(time, kind, text));
    }

    /// <summary>Rolling log: the events since the previous call (oldest first). A capped log has nothing to drain.</summary>
    public IReadOnlyList<SimEvent> Drain()
    {
        if (_pending == null || _pending.Count == 0) return Array.Empty<SimEvent>();
        var fresh = _pending.ToArray();
        _pending.Clear();
        return fresh;
    }

    // Trimmed in batches of _keep, so adding stays amortized O(1).
    private void Append(List<SimEvent> list, SimEvent e)
    {
        list.Add(e);
        if (list.Count >= 2 * _keep) list.RemoveRange(0, list.Count - _keep);
    }

    public static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
}
