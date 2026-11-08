// CharmCLI (S.I.V.A.): where an export spends its time. Parts add their seconds here; the -siva CLI prints them ("PART <seconds> <count> <name>").
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Tiger.Exporters;

public static class SivaTimes
{
    private static readonly ConcurrentDictionary<string, (double Seconds, long Count)> _parts = new();

    public static void Add(string part, double seconds) =>
        _parts.AddOrUpdate(part, (seconds, 1), (_, v) => (v.Seconds + seconds, v.Count + 1));

    public static void Add(string part, Stopwatch sw) => Add(part, sw.Elapsed.TotalSeconds);

    /// <summary>Lines "PART seconds count name", biggest first; clears the table.</summary>
    public static List<string> TakeLines()
    {
        var lines = _parts.OrderByDescending(kv => kv.Value.Seconds)
            .Select(kv => $"PART {kv.Value.Seconds:F1} {kv.Value.Count} {kv.Key}").ToList();
        _parts.Clear();
        return lines;
    }
}
