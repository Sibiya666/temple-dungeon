using Dialoid.Core.Builds;
using Dialoid.Core.Simulation;
using Dialoid.Core.Effects;
using Dialoid.Sandbox;

namespace Dialoid.Sandbox.Tests;

public class ReferenceBuildsTests
{
    private static string DataDir => DataLocator.Find(AppContext.BaseDirectory)!;

    [Fact]
    public void Lists_all_reference_files_grouped()
    {
        var list = ReferenceBuilds.List(DataDir);

        Assert.Equal(6, list.Count(i => i.Group == "main"));
        foreach (var g in new[] { "l20", "parity", "keystones", "attributes" })
            Assert.Contains(list, i => i.Group == g);
        Assert.DoesNotContain(list, i => i.Path.EndsWith(".md", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("main", list[0].Group);

        var whirl = list.Single(i => i.Group == "main" && Path.GetFileName(i.Path) == "warrior_berserker_whirlwind.json");
        Assert.DoesNotContain("_", whirl.Title);
        Assert.DoesNotContain(".json", whirl.Title);
        Assert.Equal(list.Count, list.Select(i => i.Path).Distinct().Count());
    }

    [Fact]
    public void Missing_directory_lists_nothing()
    {
        Assert.Empty(ReferenceBuilds.List(Path.Combine(Path.GetTempPath(), "dialoid-none-" + Guid.NewGuid().ToString("N"))));
    }

    [Fact]
    public void Every_main_reference_loads_with_no_violations()
    {
        foreach (var info in ReferenceBuilds.List(DataDir).Where(i => i.Group == "main"))
        {
            var s = SandboxSession.Create(TestData.Data, "warrior");
            var r = ReferenceBuilds.Load(s, info);
            Assert.True(r.Ok, $"{info.Path}: {r.Error}");
            Assert.True(s.Violations.Count == 0, $"{info.Path}: " + string.Join("; ", s.Violations.Select(v => v.Message)));
            Assert.NotNull(s.Compiled);
            for (var i = 0; i < s.Build.Slots.Count; i++) Assert.NotEmpty(s.SkillLines(i));
        }
    }

    [Fact]
    public void Every_reference_loads()
    {
        foreach (var info in ReferenceBuilds.List(DataDir))
        {
            var s = SandboxSession.Create(TestData.Data, "warrior");
            var r = ReferenceBuilds.Load(s, info);
            Assert.True(r.Ok, $"{info.Path}: {r.Error}");
        }
    }

    [Fact]
    public void Loaded_reference_compiles_same_as_file()
    {
        var sim = new Simulator(TestData.Data, MechanicRegistry.Default);
        foreach (var info in ReferenceBuilds.List(DataDir).Where(i => i.Group == "main"))
        {
            var s = SandboxSession.Create(TestData.Data, "warrior");
            Assert.True(ReferenceBuilds.Load(s, info).Ok);
            Assert.True(BuildCodec.TryFromJson(File.ReadAllText(info.Path), TestData.Data, out var file, out var error), error);

            var config = new SimConfig("training", SimMode.Average);
            Assert.Equal(sim.Run(file!, config).DpsSingleTarget, sim.Run(s.Build, config).DpsSingleTarget);
        }
    }

    [Fact]
    public void Unreadable_reference_fails_in_russian()
    {
        var s = SandboxSession.Create(TestData.Data, "warrior");
        var rev = s.Revision;
        var r = ReferenceBuilds.Load(s, new ReferenceBuildInfo(Path.Combine(DataDir, "builds", "missing.json"), "Нет", "main"));
        Assert.False(r.Ok);
        Assert.Matches("[а-яА-Я]", r.Error!);
        Assert.Equal(rev, s.Revision);
    }
}
