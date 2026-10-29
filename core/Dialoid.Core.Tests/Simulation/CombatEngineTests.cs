using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dialoid.Core.Builds;
using Dialoid.Core.Character;
using Dialoid.Core.Effects;
using Dialoid.Core.Simulation;
using Dialoid.Core.Tests.Content;
using Dialoid.Core.Tests.Fixtures;

namespace Dialoid.Core.Tests.Simulation;

/// <summary>
/// The shared step engine (training room plan, task 1). The reference results in engine-baseline.json were taken from the simulator
/// before the step loop moved into <see cref="CombatEngine"/>; every build file × dummy preset × mode must still match them to the bit.
/// Set DIALOID_WRITE_BASELINE=1 to rewrite the baseline from the current code.
/// </summary>
public class CombatEngineTests
{
    private const int LoggedEvents = 200;

    private static string BaselinePath =>
        Path.Combine(FixturePaths.RealData, "..", "core", "Dialoid.Core.Tests", "Simulation", "engine-baseline.json");

    private static bool Writing => Environment.GetEnvironmentVariable("DIALOID_WRITE_BASELINE") == "1";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    [Fact]
    public void Write_engine_baseline()
    {
        if (!Writing) return;
        File.WriteAllText(BaselinePath, JsonSerializer.Serialize(Fingerprints(), Json) + "\n");
    }

    [Fact]
    public void Engine_extraction_keeps_every_reference_result_identical()
    {
        if (Writing) return; // the baseline is being rewritten by the test above
        var expected = JsonSerializer.Deserialize<SortedDictionary<string, SortedDictionary<string, string>>>(File.ReadAllText(BaselinePath))!;
        var actual = Fingerprints();
        Assert.Equal(expected.Keys, actual.Keys);
        foreach (var (run, fields) in expected)
            foreach (var (field, value) in fields)
                Assert.True(value == actual[run][field], $"{run} {field}: expected {value}, got {actual[run][field]}");
    }

    [Fact]
    public void Infinite_life_only_removes_target_death()
    {
        var build = ContentHarness.LoadBuild("sorcerer_pyromancer_fireball");
        var config = new SimConfig("boss", SimMode.Average);
        var normal = new Simulator(ContentHarness.Data, MechanicRegistry.Default).Run(build, config);
        Assert.NotNull(normal.TimeToKill); // the boss dies within the run without the flag

        // The whole run: the boss never dies, so the fight lasts every step.
        var (endless, endlessRotation) = Engine(build, config, infiniteLife: true);
        var steps = (int)Math.Round(config.DurationSec / config.StepSec);
        for (var i = 0; i < steps; i++) Assert.True(endless.Step(endlessRotation), $"the fight ended at step {i}");
        var full = endless.Result();
        Assert.Null(full.TimeToKill);
        Assert.Equal(config.DurationSec, endless.Time, 9);
        Assert.DoesNotContain(full.Log, e => e.Kind == "kill");
        Assert.True(full.HitDamage + full.DotDamage > normal.HitDamage + normal.DotDamage);

        // Until the boss would die, both engines deal the same damage to the bit.
        var beforeDeath = (int)Math.Floor(normal.TimeToKill!.Value / config.StepSec) - 1;
        var (mortal, mortalRotation) = Engine(build, config, infiniteLife: false);
        var (immortal, immortalRotation) = Engine(build, config, infiniteLife: true);
        for (var i = 0; i < beforeDeath; i++)
        {
            Assert.True(mortal.Step(mortalRotation));
            Assert.True(immortal.Step(immortalRotation));
        }
        var a = mortal.Result();
        var b = immortal.Result();
        Assert.True(a.HitDamage + a.DotDamage > 0);
        Assert.Equal(a.DamageBySkill, b.DamageBySkill);
        Assert.Equal(a.DamageByType, b.DamageByType);
        Assert.Equal(a.HitDamage, b.HitDamage);
        Assert.Equal(a.DotDamage, b.DotDamage);
    }

    /// <summary>The engine for a build, set up the way <see cref="Simulator.Run"/> sets up its run.</summary>
    private static (CombatEngine Engine, Rotation Rotation) Engine(Build build, SimConfig config, bool infiniteLife)
    {
        var data = ContentHarness.Data;
        build = BuildRules.Normalized(build);
        var character = new CharacterCompiler(data, MechanicRegistry.Default).Compile(build);
        var weapon = build.WeaponId != null ? data.Weapons[build.WeaponId] : null;
        var skills = character.Skills.Select(s => new SkillRuntime(data, character, s)).ToList();
        var engine = new CombatEngine(data, MechanicRegistry.Default, character, weapon, skills, data.Dummies[config.DummyId], config, infiniteLife);
        return (engine, new Rotation(build, skills));
    }

    /// <summary>Every build file × dummy preset × (Average, Roll seed 1, Roll seed 7). Presets with an attack run in defensive mode.</summary>
    private static SortedDictionary<string, SortedDictionary<string, string>> Fingerprints()
    {
        var data = ContentHarness.Data;
        var sim = new Simulator(data, MechanicRegistry.Default);
        var buildsDir = Path.Combine(FixturePaths.RealData, "builds");
        var runs = new SortedDictionary<string, SortedDictionary<string, string>>(StringComparer.Ordinal);
        foreach (var file in Directory.GetFiles(buildsDir, "*.json", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
        {
            var name = Path.GetRelativePath(buildsDir, file).Replace('\\', '/');
            Assert.True(BuildCodec.TryFromJson(File.ReadAllText(file), data, out var build, out var error), $"{name}: {error}");
            foreach (var dummy in data.Dummies.Values.OrderBy(d => d.Id, StringComparer.Ordinal))
            {
                var attacks = dummy.Attack != null;
                foreach (var (mode, seed, label) in new[] { (SimMode.Average, 1, "average"), (SimMode.Roll, 1, "roll1"), (SimMode.Roll, 7, "roll7") })
                    runs[$"{name}|{dummy.Id}|{label}"] = Fingerprint(sim.Run(build!, new SimConfig(dummy.Id, mode, seed, DummyAttacks: attacks)));
            }
        }
        return runs;
    }

    /// <summary>Exact ("R") text of every compared value; the resource series and the event log
    /// as counts plus a SHA-256 of their exact text (the log: first 200 events), so the baseline file stays small.</summary>
    private static SortedDictionary<string, string> Fingerprint(SimResult r)
    {
        var f = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["dps_single"] = N(r.DpsSingleTarget),
            ["dps_pack"] = N(r.DpsPack),
            ["hit_damage"] = N(r.HitDamage),
            ["dot_damage"] = N(r.DotDamage),
            ["ttk"] = N(r.TimeToKill),
            ["ttd"] = N(r.TimeToDeath),
            ["damage_by_skill"] = string.Join(";", r.DamageBySkill.Select(kv => $"{kv.Key}={N(kv.Value)}")),
            ["damage_by_type"] = string.Join(";", r.DamageByType.Select(kv => $"{kv.Key}={N(kv.Value)}")),
            ["damage_taken"] = r.DamageTaken == null ? "null" : string.Join(";", r.DamageTaken.Select(kv => $"{kv.Key}={N(kv.Value)}")),
            ["defense"] = r.Defense is not { } d ? "null"
                : $"{d.AttacksReceived};{d.AttacksDodged};{string.Join(",", d.IncomingBeforeMitigation.Select(kv => $"{kv.Key}={N(kv.Value)}"))};" +
                  $"{N(d.MitigatedByArmor)};{N(d.MitigatedByResist)};{N(d.LifeRegenerated)}",
            ["resource_series_count"] = r.ResourceSeries.Count.ToString(CultureInfo.InvariantCulture),
            ["resource_series_last"] = r.ResourceSeries.Count == 0 ? "none" : Sample(r.ResourceSeries[^1]),
            ["resource_series_sha256"] = Sha256(string.Join(";", r.ResourceSeries.Select(Sample))),
            ["event_count"] = r.Log.Count.ToString(CultureInfo.InvariantCulture),
            ["events_sha256"] = Sha256(string.Concat(r.Log.Take(LoggedEvents).Select(e => $"{N(e.Time)}|{e.Kind}|{e.Text}\n"))),
        };
        return f;
    }

    private static string Sample(ResourceSample s) => $"{N(s.Time)},{N(s.Resource)},{N(s.Life)}";

    private static string N(double v) => v.ToString("R", CultureInfo.InvariantCulture);
    private static string N(double? v) => v is { } x ? N(x) : "null";

    private static string Sha256(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
