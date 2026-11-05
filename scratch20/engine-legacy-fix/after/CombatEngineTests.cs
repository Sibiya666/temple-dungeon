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
/// before the step loop moved into <see cref="CombatEngine"/>; the frozen legacy codes × dummy preset × mode must still match them to the bit.
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
        File.WriteAllText(BaselinePath, JsonSerializer.Serialize(Fingerprints(historical: LegacyBuilds()), Json) + "\n");
    }

    [Fact]
    public void Engine_extraction_keeps_frozen_32_legacy_reference_results_identical()
    {
        if (Writing) return; // the baseline is being rewritten by the test above
        AssertMatchesBaseline(_ => true);
    }

    /// <summary>The build files the filter keeps (path relative to data/builds, e.g. "keystones/x.json") still give their baseline
    /// results to the bit using historical code inputs; every baseline run of those builds must be present.</summary>
    internal static void AssertMatchesBaseline(Func<string, bool> buildFilter)
    {
        var expected = JsonSerializer.Deserialize<SortedDictionary<string, SortedDictionary<string, string>>>(File.ReadAllText(BaselinePath))!;
        var legacy = LegacyBuilds();
        var baselinePaths = expected.Keys.Select(run => run[..run.IndexOf('|')]).Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal);
        Assert.Equal(baselinePaths, legacy.Keys); // exact corpus before any caller's filter is applied
        var actual = Fingerprints(buildFilter, legacy);
        var kept = expected.Where(kv => buildFilter(kv.Key[..kv.Key.IndexOf('|')])).ToList();
        Assert.NotEmpty(kept);
        Assert.Equal(kept.Select(kv => kv.Key), actual.Keys);
        foreach (var (run, fields) in kept)
            foreach (var (field, value) in fields)
                Assert.True(value == actual[run][field], $"{run} {field}: expected {value}, got {actual[run][field]}");
    }

    // Historical inputs are shared with the codec compatibility corpus, never current optimized references.
    private static SortedDictionary<string, Build> LegacyBuilds()
    {
        var legacy = new SortedDictionary<string, Build>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "legacy_codes.txt")).Where(line => line.Length > 0))
        {
            var row = line.Split('\t');
            Assert.Equal(3, row.Length);
            Assert.True(BuildCodec.TryImport(row[1], ContentHarness.Data, out var build, out var error), row[0] + ": " + error);
            Assert.True(build!.ClassId is "warrior" or "sorcerer", row[0]);
            Assert.True(legacy.TryAdd(row[0], build), "Duplicate legacy path: " + row[0]);
        }
        Assert.Equal(32, legacy.Count);
        return legacy;
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

    /// <summary>The engine for a build, set up by the same factory as <see cref="Simulator.Run"/>.</summary>
    private static (CombatEngine Engine, Rotation Rotation) Engine(Build build, SimConfig config, bool infiniteLife)
    {
        var setup = FightSetup.Prepare(ContentHarness.Data, MechanicRegistry.Default, build, config, out var violations);
        Assert.True(setup != null, string.Join("; ", violations.Select(v => v.Message)));
        return (setup.Engine(config, infiniteLife), setup.Rotation());
    }

    /// <summary>Every build file × dummy preset × (Average, Roll seed 1, Roll seed 7). Presets with an attack run in defensive mode.</summary>
    private static SortedDictionary<string, SortedDictionary<string, string>> Fingerprints(Func<string, bool>? buildFilter = null, IReadOnlyDictionary<string, Build>? historical = null)
    {
        var data = ContentHarness.Data;
        var sim = new Simulator(data, MechanicRegistry.Default);
        var buildsDir = Path.Combine(FixturePaths.RealData, "builds");
        var runs = new SortedDictionary<string, SortedDictionary<string, string>>(StringComparer.Ordinal);
        var names = historical != null ? historical.Keys : Directory.GetFiles(buildsDir, "*.json", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(buildsDir, file).Replace(Path.DirectorySeparatorChar, '/'));
        foreach (var name in names.OrderBy(name => name, StringComparer.Ordinal))
        {
            if (buildFilter != null && !buildFilter(name)) continue;
            Build? build;
            if (historical != null) build = historical[name];
            else Assert.True(BuildCodec.TryFromJson(File.ReadAllText(Path.Combine(buildsDir, name)), data, out build, out var error), $"{name}: {error}");
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
