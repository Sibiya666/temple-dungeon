using System.Text.Json.Nodes;
using Dialoid.Core.Character;
using Dialoid.Core.Data;
using Dialoid.Core.Effects;
using Dialoid.Core.Simulation;
using Dialoid.Core.Tests.Fixtures;

namespace Dialoid.Core.Tests.Data;

public class DataOnlyExtensionTests
{
    /// <summary>Tiny fixture plus the entries of Fixtures/data_only appended to the matching catalogs.</summary>
    private static string TinyPlusDataOnly()
    {
        var dir = FixturePaths.CopyTiny();
        var extra = Path.Combine(AppContext.BaseDirectory, "Fixtures", "data_only");
        foreach (var file in Directory.GetFiles(extra, "*.json"))
        {
            var target = Path.Combine(dir, Path.GetFileName(file));
            var arr = JsonNode.Parse(File.ReadAllText(target))!.AsArray();
            foreach (var item in JsonNode.Parse(File.ReadAllText(file))!.AsArray().ToList())
                arr.Add(JsonNode.Parse(item!.ToJsonString()));
            File.WriteAllText(target, arr.ToJsonString());
        }
        return dir;
    }

    [Fact]
    public void New_skill_and_node_from_json_only_load_and_simulate()
    {
        var res = DataLoader.Load(TinyPlusDataOnly());
        Assert.Empty(res.Errors);
        var data = res.Data!;
        Assert.Contains("data_only_flame", data.Skills.Keys);
        Assert.Contains("data_only_node", data.Passives.Nodes.Keys);
        Assert.Empty(Validator.Validate(data, MechanicRegistry.Default));

        // The skill reaches the mage through a data-only item, the node through the existing passive tree.
        var build = new Build { ClassId = "test_mage", Level = 10 };
        build.PassiveNodes.AddRange(new[] { "small_c", "data_only_node" });
        build.ItemIds.Add("data_only_ring");
        build.Slots.Add(new SkillSlot { SkillId = "data_only_flame", Level = 2, NodePoints = { ["flame_power"] = 2 } });
        build.Rotation.Add("data_only_flame");
        Assert.Empty(BuildRules.Validate(build, data));

        var sim = new Simulator(data, MechanicRegistry.Default);
        var result = sim.Run(build, new SimConfig("test_training", SimMode.Average, DurationSec: 30));
        Assert.Empty(result.Violations);
        Assert.True(result.DpsSingleTarget > 0);

        // The new node really contributes: without it damage is lower.
        build.PassiveNodes.Remove("data_only_node");
        var without = sim.Run(build, new SimConfig("test_training", SimMode.Average, DurationSec: 30));
        Assert.True(result.DpsSingleTarget > without.DpsSingleTarget);
    }
}
