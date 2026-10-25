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

    /// <summary>Tiny-fixture copy whose ascendancy start node (test_asc, warrior) carries exactly <paramref name="effectsJson"/>
    /// (comma-separated effect objects): the effects are sourced as an ascendancy, like Berserker/Thunder nodes.</summary>
    public static GameData Asc(string effectsJson, Action<string>? mutate = null)
    {
        var dir = FixturePaths.CopyTiny();
        Edit(dir, "ascendancies.json", a => Find(a, "test_asc")["start"]!["effects"] = JsonNode.Parse($"[{effectsJson}]"));
        mutate?.Invoke(dir);
        var r = DataLoader.Load(dir);
        Assert.Empty(r.Errors);
        return r.Data!;
    }

    public const string MageAscId = "test_mage_asc";

    /// <summary>
    /// Tiny-fixture copy with a mage ascendancy (test_mage_asc) whose start node carries exactly <paramref name="effectsJson"/>,
    /// plus the caster fixtures: test_fireball (Огненный шар stand-in with a small tree: fb_heat, fb_core +10 ignite chance,
    /// fb_residual +15% ignite damage, fork "orb": fb_frost / fb_storm), test_frostbolt (cold bolt, no cost), test_meteor
    /// (fire area spell) and the immortal 5-target dummy test_pack.
    /// </summary>
    public static GameData MageAsc(string effectsJson, Action<string>? mutate = null, MechanicRegistry? registry = null)
    {
        var dir = FixturePaths.CopyTiny();
        Edit(dir, "ascendancies.json", a => a.AsArray().Add(JsonNode.Parse(
            $"{{\"id\":\"{MageAscId}\",\"name\":\"Тестовый маг-асцендант\",\"class_id\":\"test_mage\"," +
            $"\"start\":{{\"id\":\"mage_asc_start\",\"name\":\"Старт\",\"description\":\"тест\",\"effects\":[{effectsJson}]}}," +
            "\"notables\":[{\"id\":\"mage_asc_n1\",\"name\":\"Узел\",\"description\":\"тест\",\"effects\":[]}]}")));
        Edit(dir, "classes.json", c => Find(c, "test_mage")["ascendancies"]!.AsArray().Add(MageAscId));
        AddSkill(dir, "test_mage", Fireball);
        AddSkill(dir, "test_mage", Frostbolt);
        AddSkill(dir, "test_mage", Meteor);
        Edit(dir, "dummies.json", a => a.AsArray().Add(JsonNode.Parse(
            "{\"id\":\"test_pack\",\"name\":\"Пак\",\"pack_size\":5,\"pack_radius\":4,\"life_per_target\":null,\"respawn\":false,\"armor\":0}")));
        mutate?.Invoke(dir);
        var r = DataLoader.Load(dir, registry);
        Assert.Empty(r.Errors);
        return r.Data!;
    }

    private static string Node(string id, int max, string? requires, string? group, string effects) =>
        $"{{\"id\":\"{id}\",\"name\":\"{id}\",\"description\":\"тест\",\"max_points\":{max}," +
        (requires == null ? "" : $"\"requires\":[{{\"node\":\"{requires}\",\"points\":2}}],") +
        (group == null ? "" : $"\"exclusive_group\":\"{group}\",") + $"\"effects\":[{effects}]}}";

    private static readonly string Fireball =
        "{\"id\":\"test_fireball\",\"name\":\"Огненный шар\",\"description\":\"тест\",\"class_id\":\"test_mage\",\"unlock_level\":1," +
        "\"tags\":[\"spell\",\"projectile\",\"area\",\"fire\"],\"spell_damage\":{\"fire\":[14,20]},\"cast_time\":0.75,\"resource_cost\":12," +
        "\"crit_chance\":6,\"ailment_chances\":{\"ignite\":20},\"behavior\":{\"projectile_count\":1,\"explosion_radius\":4},\"tree\":[" +
        Node("fb_heat", 4, null, null, "{\"type\":\"modifier\",\"stat\":\"damage\",\"kind\":\"increased\",\"value\":8}") + "," +
        Node("fb_core", 3, "fb_heat", null, "{\"type\":\"modifier\",\"stat\":\"ignite_chance\",\"kind\":\"flat\",\"value\":10}") + "," +
        Node("fb_residual", 3, "fb_heat", null,
            "{\"type\":\"modifier\",\"stat\":\"damage\",\"kind\":\"increased\",\"value\":15,\"tags\":[\"ailment_ignite\"]}") + "," +
        Node("fb_frost", 1, "fb_heat", "orb", "{\"type\":\"set_damage_type\",\"to\":\"cold\"}," +
            "{\"type\":\"modifier\",\"stat\":\"freeze_chance\",\"kind\":\"flat\",\"value\":10}") + "," +
        Node("fb_storm", 1, "fb_heat", "orb", "{\"type\":\"set_damage_type\",\"to\":\"lightning\"}," +
            "{\"type\":\"modifier\",\"stat\":\"shock_chance\",\"kind\":\"flat\",\"value\":25}") + "]}";

    private const string Frostbolt =
        "{\"id\":\"test_frostbolt\",\"name\":\"Ледяная стрела\",\"description\":\"тест\",\"class_id\":\"test_mage\",\"unlock_level\":1," +
        "\"tags\":[\"spell\",\"projectile\",\"cold\"],\"spell_damage\":{\"cold\":[14,20]},\"cast_time\":0.75,\"crit_chance\":6," +
        "\"behavior\":{\"projectile_count\":1}}";

    private const string Meteor =
        "{\"id\":\"test_meteor\",\"name\":\"Метеор\",\"description\":\"тест\",\"class_id\":\"test_mage\",\"unlock_level\":1," +
        "\"tags\":[\"spell\",\"area\",\"fire\"],\"spell_damage\":{\"fire\":[40,60]},\"cast_time\":1,\"crit_chance\":6," +
        "\"behavior\":{\"area_radius\":2}}";

    /// <summary>Sets fields of a skill in skills.json (values are JSON).</summary>
    public static void SetSkill(string dir, string skill, params (string Field, string Json)[] fields) =>
        Edit(dir, "skills.json", s =>
        {
            foreach (var (f, json) in fields) Find(s, skill)[f] = JsonNode.Parse(json);
        });

    /// <summary>A level 20 mage; <paramref name="asc"/> picks test_mage_asc (whose start node is free).</summary>
    public static Build Mage(bool asc, params string[] skills)
    {
        var b = Make("test_mage", 20, false, null, skills);
        if (asc) b.AscendancyId = MageAscId;
        return b;
    }

    /// <summary>A level 20 warrior; <paramref name="asc"/> picks test_asc (whose start node is free).</summary>
    public static Build Warrior(bool asc, string? weapon, params string[] skills)
    {
        var b = Make("test_warrior", 20, false, weapon, skills);
        if (asc) b.AscendancyId = "test_asc";
        return b;
    }

    /// <summary>Damage of each hit of <paramref name="skill"/> in log order (log numbers carry two decimals).</summary>
    public static double[] HitAmounts(SimResult r, string skill) =>
        r.Log.Where(e => e.Kind == "hit" && e.Text.StartsWith(skill + " ", StringComparison.Ordinal))
            .Select(e => double.Parse(e.Text[(e.Text.LastIndexOf(':') + 1)..], System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();

    /// <summary>Adds a skill object to skills.json and to the class's skill list.</summary>
    public static void AddSkill(string dir, string classId, string skillJson)
    {
        var skill = JsonNode.Parse(skillJson)!;
        Edit(dir, "skills.json", s => s.AsArray().Add(skill));
        Edit(dir, "classes.json", c => Find(c, classId)["skills"]!.AsArray().Add(skill["id"]!.GetValue<string>()));
    }

    public static void AddTags(string dir, params string[] tags) => Edit(dir, "tags.json", t =>
    {
        foreach (var tag in tags.Where(tag => t.AsArray().All(x => x!["id"]!.GetValue<string>() != tag))) t.AsArray().Add(JsonNode.Parse($"{{\"id\":\"{tag}\",\"name\":\"{tag}\"}}"));
    });

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
        Action<SimState>? observe = null, MechanicRegistry? registry = null, bool attacks = false, double cap = 180, int? pack = null)
    {
        var sim = new Simulator(d, registry ?? MechanicRegistry.Default);
        if (observe != null) sim.StepObserver = observe;
        var r = sim.Run(b, new SimConfig(dummy, SimMode.Average, DurationSec: duration, PackSizeOverride: pack, DummyAttacks: attacks,
            SurvivalCapSec: cap));
        Assert.Empty(r.Violations);
        return r;
    }

    public static double[] UseTimes(SimResult r, string skill) =>
        r.Log.Where(e => e.Kind == "use" && e.Text == skill).Select(e => Math.Round(e.Time, 6)).ToArray();

    public static int Count(SimResult r, string kind, string? prefix = null) =>
        r.Log.Count(e => e.Kind == kind && (prefix == null || e.Text.StartsWith(prefix, StringComparison.Ordinal)));
}
