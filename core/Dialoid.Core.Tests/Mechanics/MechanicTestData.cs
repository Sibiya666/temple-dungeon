using System.Text.Json.Nodes;
using Dialoid.Core.Character;
using Dialoid.Core.Data;
using Dialoid.Core.Effects;
using Dialoid.Core.Simulation;
using Dialoid.Core.Tests.Fixtures;

namespace Dialoid.Core.Tests.Mechanics;

/// <summary>Tiny-fixture copies with a passive node "ks" (linked to both class starts) that carries one mechanic effect.</summary>
internal static class MechanicTestData
{
    public const string NodeId = "ks";

    public static void Edit(string dir, string file, Action<JsonNode> mutate)
    {
        var path = Path.Combine(dir, file);
        var node = JsonNode.Parse(File.ReadAllText(path))!;
        mutate(node);
        File.WriteAllText(path, node.ToJsonString());
    }

    public static JsonNode Find(JsonNode arr, string id) => arr.AsArray().First(n => n!["id"]!.GetValue<string>() == id)!;

    /// <param name="effectJson">The node's single effect, e.g. {"type":"mechanic","id":"blood_pact"}.</param>
    public static GameData With(string effectJson, Action<string>? mutate = null, MechanicRegistry? registry = null,
        string kind = "keystone")
    {
        var dir = FixturePaths.CopyTiny();
        Edit(dir, "passives.json", n => n.AsArray().Add(JsonNode.Parse(
            $"{{\"id\":\"{NodeId}\",\"name\":\"Тест\",\"description\":\"тест\",\"kind\":\"{kind}\",\"region\":\"test\",\"x\":0,\"y\":100," +
            $"\"links\":[\"test_start_warrior\",\"test_start_mage\"],\"effects\":[{effectJson}]}}")));
        mutate?.Invoke(dir);
        var r = DataLoader.Load(dir, registry);
        Assert.Empty(r.Errors);
        return r.Data!;
    }

    public static GameData Keystone(string mechanicId, Action<string>? mutate = null) =>
        With($"{{\"type\":\"mechanic\",\"id\":\"{mechanicId}\"}}", mutate);

    public static Build Make(string cls, int level, bool node, string? weapon, params string[] skills)
    {
        var b = new Build { ClassId = cls, Level = level, WeaponId = weapon };
        if (node) b.PassiveNodes.Add(NodeId);
        foreach (var s in skills) b.Slots.Add(new SkillSlot { SkillId = s, Level = 1 });
        b.Rotation.AddRange(skills);
        return b;
    }

    public static SimResult Run(GameData d, Build b, string dummy = "test_training", double duration = 6,
        Action<SimState>? observe = null, MechanicRegistry? registry = null, bool attacks = false, double cap = 180)
    {
        var sim = new Simulator(d, registry ?? MechanicRegistry.Default);
        if (observe != null) sim.StepObserver = observe;
        var r = sim.Run(b, new SimConfig(dummy, SimMode.Average, DurationSec: duration, DummyAttacks: attacks, SurvivalCapSec: cap));
        Assert.Empty(r.Violations);
        return r;
    }

    public static double[] UseTimes(SimResult r, string skill) =>
        r.Log.Where(e => e.Kind == "use" && e.Text == skill).Select(e => Math.Round(e.Time, 6)).ToArray();

    public static int Count(SimResult r, string kind, string? prefix = null) =>
        r.Log.Count(e => e.Kind == kind && (prefix == null || e.Text.StartsWith(prefix, StringComparison.Ordinal)));
}
