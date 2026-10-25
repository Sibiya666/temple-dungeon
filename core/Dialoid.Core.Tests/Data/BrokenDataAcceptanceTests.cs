using System.Text.Json.Nodes;
using Dialoid.Core.Data;
using Dialoid.Core.Tests.Fixtures;

namespace Dialoid.Core.Tests.Data;

public class BrokenDataAcceptanceTests
{
    private static LoadResult LoadMutated(string file, Action<JsonArray> mutate)
    {
        var dir = FixturePaths.CopyTiny();
        var path = Path.Combine(dir, file);
        var arr = JsonNode.Parse(File.ReadAllText(path))!.AsArray();
        mutate(arr);
        File.WriteAllText(path, arr.ToJsonString());
        return DataLoader.Load(dir);
    }

    private static JsonNode Node(JsonArray a, string id) => a.First(n => n!["id"]!.GetValue<string>() == id)!;

    /// <summary>Spec 15.2: every kind of broken data is reported with file, id and a Russian reason; nothing is loaded.</summary>
    [Fact]
    public void All_broken_fixtures_report_expected_errors()
    {
        var cycle = LoadMutated("skills.json", a => Node(Node(a, "test_slash")["tree"]!.AsArray(), "slash_depth")["requires"] =
            JsonNode.Parse("[{\"node\":\"slash_rend\",\"points\":1}]"));
        Assert.Null(cycle.Data);
        Assert.Contains(cycle.Errors, e => e.File == "skills.json" && e.Id == "test_slash" && e.Message.Contains("Цикл"));

        var island = LoadMutated("passives.json", a => a.Add(JsonNode.Parse(
            "{\"id\":\"island\",\"name\":\"Остров\",\"description\":\"\",\"kind\":\"small\",\"region\":\"test\",\"x\":0,\"y\":0,\"links\":[]}")));
        Assert.Null(island.Data);
        Assert.Contains(island.Errors, e => e.File == "passives.json" && e.Id == "island" && e.Message.Contains("недостижим"));

        var unknown = LoadMutated("classes.json", a => Node(a, "test_warrior")["passive_start_node"] = "nope_node");
        Assert.Null(unknown.Data);
        Assert.Contains(unknown.Errors, e => e.File == "classes.json" && e.Id == "test_warrior" && e.Message.Contains("nope_node"));

        var leftward = LoadMutated("skills.json", a => Node(Node(a, "test_slash")["tree"]!.AsArray(), "slash_depth")["effects"] =
            JsonNode.Parse("[{\"type\":\"add_conversion\",\"from\":\"fire\",\"to\":\"cold\",\"percent\":30}]"));
        Assert.Null(leftward.Data);
        Assert.Contains(leftward.Errors, e => e.File == "skills.json" && e.Id == "test_slash" && e.Message.Contains("только вправо"));
        Assert.All(new[] { cycle, island, unknown, leftward }, r => Assert.All(r.Errors, e => Assert.False(string.IsNullOrWhiteSpace(e.Message))));
    }
}
