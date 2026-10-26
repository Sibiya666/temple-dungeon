using Dialoid.Core.Builds;
using Dialoid.Core.Character;
using Dialoid.Core.Data;
using Dialoid.Core.Effects;
using Dialoid.Core.Simulation;

public static class H
{
    /// <summary>The repository data directory: $DIALOID_DATA, else the first "data" folder with classes.json above the working directory.</summary>
    public static readonly string DataDir = FindDataDir();
    static string FindDataDir()
    {
        var env = Environment.GetEnvironmentVariable("DIALOID_DATA");
        if (!string.IsNullOrEmpty(env)) return env;
        for (var d = new DirectoryInfo(Directory.GetCurrentDirectory()); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "data", "classes.json"))) return Path.Combine(d.FullName, "data");
        throw new InvalidOperationException("data/ not found: run inside the repository or set DIALOID_DATA");
    }
    public static readonly GameData Data = Load();
    static GameData Load()
    {
        var r = DataLoader.Load(DataDir);
        if (r.Errors.Count > 0) throw new Exception(string.Join("\n", r.Errors.Select(e => e.Message)));
        return r.Data!;
    }
    public static Build LoadBuild(string rel)
    {
        var path = rel.EndsWith(".json") ? rel : Path.Combine(DataDir, "builds", rel + ".json");
        if (!BuildCodec.TryFromJson(File.ReadAllText(path), Data, out var b, out var err)) throw new Exception(err);
        return b!;
    }
    public static SimResult Run(Build b, string dummy, bool attacks = false) =>
        new Simulator(Data, MechanicRegistry.Default).Run(b, new SimConfig(dummy, SimMode.Average, DummyAttacks: attacks));
    public static Build Clone(Build b) { BuildCodec.TryFromJson(BuildCodec.ToJson(b), Data, out var c, out _); return c!; }
    public static IReadOnlyList<RuleViolation> V(Build b) => BuildRules.Validate(b, Data);
    public static (double tr, double sw, double surv, double boss) Metrics(Build b)
    {
        var t = Run(b, "training"); var s = Run(b, "swarm"); var d = Run(b, "attacking_boss", true); var k = Run(b, "boss");
        return (t.DpsSingleTarget, s.DpsPack, d.TimeToDeath ?? 999, k.TimeToKill ?? 999);
    }
    public static void Report(string name, Build b)
    {
        var v = V(b);
        if (v.Count > 0) Console.WriteLine($"{name}: VIOLATIONS " + string.Join("; ", v.Select(x => x.Code + ":" + x.Message)));
        var t = Run(b, "training"); var s = Run(b, "swarm"); var d = Run(b, "attacking_boss", true); var k = Run(b, "boss");
        double tot = t.DamageBySkill.Values.Sum(); double tt = t.DamageByType.Values.Sum();
        var uses = t.Log.Where(e => e.Kind == "use").GroupBy(e => e.Text).ToDictionary(g => g.Key, g => g.Count());
        Console.WriteLine($"{name}: train {t.DpsSingleTarget:F0}  swarm {s.DpsPack:F0}  surv {d.TimeToDeath?.ToString("F1") ?? "alive"}  bossTTK {k.TimeToKill?.ToString("F1") ?? "-"}");
        Console.WriteLine("   skills: " + string.Join(", ", t.DamageBySkill.OrderByDescending(x => x.Value).Select(x => $"{x.Key} {100 * x.Value / tot:F0}%")));
        Console.WriteLine("   types: " + string.Join(", ", t.DamageByType.OrderByDescending(x => x.Value).Select(x => $"{x.Key} {100 * x.Value / tt:F0}%")));
        Console.WriteLine("   uses: " + string.Join(", ", b.Rotation.Select(r => $"{r} {uses.GetValueOrDefault(r)}")) + (t.Log.Any(e => e.Kind == "log_truncated") ? " [TRUNC]" : ""));
    }
}
