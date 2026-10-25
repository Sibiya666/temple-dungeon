using System.Text.Json.Nodes;
using Dialoid.Core.Data;
using Dialoid.Core.Effects;
using Dialoid.Core.Tests.Fixtures;

namespace Dialoid.Core.Tests.Data;

public class ValidatorTests
{
    /// <summary>Copies tiny into a temp dir and edits one JSON array file.</summary>
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

    [Fact]
    public void Tiny_fixture_passes_validation()
    {
        var d = DataLoader.Load(FixturePaths.Tiny);
        Assert.Empty(d.Errors);
        Assert.Empty(Validator.Validate(d.Data!, MechanicRegistry.Default));
    }

    [Fact]
    public void Validator_reports_disconnected_tree()
    {
        var r = LoadMutated("passives.json", a => a.Add(JsonNode.Parse(
            "{\"id\":\"island\",\"name\":\"Остров\",\"description\":\"\",\"kind\":\"small\",\"region\":\"test\",\"x\":0,\"y\":0,\"links\":[]}")));
        Assert.Null(r.Data);
        var e = Assert.Single(r.Errors);
        Assert.Equal("passives.json", e.File);
        Assert.Equal("island", e.Id);
        Assert.Contains("недостижим", e.Message);
    }

    [Fact]
    public void Validator_reports_two_starts_for_one_class()
    {
        var r = LoadMutated("passives.json", a => Node(a, "test_start_mage")["start_for_class"] = "test_warrior");
        Assert.False(r.Ok);
        Assert.Contains(r.Errors, e => e.File == "passives.json" && e.Id == "test_warrior" && e.Message.Contains("стартов"));
        Assert.Contains(r.Errors, e => e.File == "classes.json" && e.Id == "test_mage");
    }

    [Fact]
    public void Validator_reports_unknown_reference_with_file_and_id()
    {
        var r = LoadMutated("classes.json", a => Node(a, "test_warrior")["passive_start_node"] = "nope_node");
        Assert.Contains(r.Errors, e => e.File == "classes.json" && e.Id == "test_warrior" && e.Message.Contains("nope_node"));

        r = LoadMutated("classes.json", a => Node(a, "test_warrior")["ascendancies"] = new JsonArray("test_asc", "no_asc"));
        Assert.Contains(r.Errors, e => e.File == "classes.json" && e.Id == "test_warrior" && e.Message.Contains("no_asc"));

        r = LoadMutated("passives.json", a => Node(a, "small_c")["links"] = new JsonArray("ghost"));
        Assert.Contains(r.Errors, e => e.File == "passives.json" && e.Id == "small_c" && e.Message.Contains("ghost"));

        r = LoadMutated("ascendancies.json", a => Node(a, "test_asc")["class_id"] = "test_mage");
        Assert.Contains(r.Errors, e => e.File == "ascendancies.json" && e.Id == "test_asc");

        r = LoadMutated("passives.json", a => Node(a, "small_a")["effects"] = JsonNode.Parse(
            "[{\"type\":\"modifier\",\"stat\":\"no_such_stat\",\"kind\":\"flat\",\"value\":1}]"));
        Assert.Contains(r.Errors, e => e.File == "passives.json" && e.Id == "small_a" && e.Message.Contains("no_such_stat"));
    }

    [Fact]
    public void Validator_reports_per_stat_cycle()
    {
        // The primary-attribute rule makes damage depend on str; make str depend on damage.
        var r = LoadMutated("passives.json", a => Node(a, "small_a")["effects"] = JsonNode.Parse(
            "[{\"type\":\"modifier\",\"stat\":\"str\",\"kind\":\"flat\",\"value\":1,\"per\":{\"stat\":\"damage\",\"every\":1}}]"));
        Assert.Null(r.Data);
        var e = Assert.Single(r.Errors);
        Assert.Contains("Цикл", e.Message);
        Assert.Contains("damage", e.Message);
        Assert.Contains("str", e.Message);
    }
}
