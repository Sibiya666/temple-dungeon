using System.Text.Json.Nodes;
using Dialoid.Core.Data;
using Dialoid.Core.Tests.Fixtures;

namespace Dialoid.Core.Tests.Data;

public class StrictJsonTests
{
    private const string Bogus = "bogus_field";

    // (file, setup path, setup json, target path). Target is the object that receives the unknown field.
    // Paths: '/'-separated, integers are array indices. The first segment of a catalog path is the record index.
    public static IEnumerable<object?[]> Cases()
    {
        object?[] C(string file, string target, string? setupPath = null, string? setupJson = null) => new object?[] { file, target, setupPath, setupJson };

        yield return C("stats.json", "0");
        yield return C("tags.json", "0");
        yield return C("ailments.json", "0");
        yield return C("passives.json", "0");
        yield return C("items.json", "0");
        yield return C("weapons.json", "0");
        yield return C("skills.json", "0");
        yield return C("skills.json", "0/tree/0");
        yield return C("skills.json", "0/tree/1/requires/0");
        yield return C("classes.json", "0");
        yield return C("classes.json", "0/resource");
        yield return C("classes.json", "0/resource/max_per", "0/resource/max_per", """{"stat":"will","every":1}""");
        yield return C("classes.json", "0/class_bonuses/0");
        yield return C("classes.json", "0/class_bonuses/0/per");
        yield return C("classes.json", "0/class_bonuses/0/conditions/0", "0/class_bonuses/0/conditions", """[{"id":"x"}]""");
        yield return C("dummies.json", "0");
        yield return C("dummies.json", "0/attack", "0/attack", """{"damage_per_hit":{"physical":5},"interval_sec":1}""");
        yield return C("ascendancies.json", "0");
        yield return C("ascendancies.json", "0/start");
        yield return C("ascendancies.json", "0/notables/0");
        yield return C("attributes.json", "");
        yield return C("attributes.json", "bonuses/0");
        yield return C("progression.json", "");

        // effects (owner: first skill tree node / first passive)
        const string E = "0/tree/0/effects/0";
        yield return C("skills.json", E);                                  // modifier (flat form)
        yield return C("skills.json", E + "/scope", E, """{"type":"modifier","stat":"damage","kind":"increased","value":1,"scope":{"tags":["spell"]}}""");
        yield return C("skills.json", E + "/modifier", E, """{"type":"modifier","modifier":{"stat":"damage","kind":"increased","value":1}}""");
        yield return C("skills.json", E, E, """{"type":"modifier","modifier":{"stat":"damage","kind":"increased","value":1}}""");
        yield return C("skills.json", E, E, """{"type":"add_tag","tag":"fire"}""");
        yield return C("skills.json", E, E, """{"type":"remove_tag","tag":"fire"}""");
        yield return C("skills.json", E, E, """{"type":"set_behavior","param":"chain_count","value":1}""");
        yield return C("skills.json", E, E, """{"type":"add_behavior","param":"chain_count","delta":1}""");
        yield return C("skills.json", E, E, """{"type":"add_conversion","from":"physical","to":"fire","percent":10}""");
        yield return C("skills.json", E, E, """{"type":"set_damage_type","to":"fire"}""");
        yield return C("skills.json", E, E, """{"type":"set_resource","field":"cost","value":1}""");
        yield return C("skills.json", E, E, """{"type":"mechanic","id":"shatter"}""");
        yield return C("skills.json", E, E, """{"type":"grant_proc","trigger":"on_hit","action":{"apply_ailment":"bleed"}}""");
        yield return C("skills.json", E + "/trigger", E, """{"type":"grant_proc","trigger":{"every_n_hits":3},"action":{"apply_ailment":"bleed"}}""");
        yield return C("skills.json", E + "/action", E, """{"type":"grant_proc","trigger":"on_hit","action":{"apply_ailment":"bleed"}}""");
        yield return C("skills.json", E + "/action/cast_skill", E, """{"type":"grant_proc","trigger":"on_hit","action":{"cast_skill":{"id":"test_slash","damage_percent":50}}}""");
        yield return C("skills.json", E + "/action/apply_buff", E, """{"type":"grant_proc","trigger":"on_hit","action":{"apply_buff":{"id":"b","duration":3,"modifiers":[{"stat":"damage","kind":"more","value":5}]}}}""");
        yield return C("skills.json", E + "/action/apply_buff/modifiers/0", E, """{"type":"grant_proc","trigger":"on_hit","action":{"apply_buff":{"id":"b","duration":3,"modifiers":[{"stat":"damage","kind":"more","value":5}]}}}""");
        yield return C("passives.json", "0/effects/0", "0/effects", """[{"type":"modifier","stat":"damage","kind":"increased","value":1}]""");
        yield return C("ascendancies.json", "0/start/effects/0");
        yield return C("ascendancies.json", "0/notables/0/effects/0");
        yield return C("weapons.json", "0/implicits/0", "0/implicits", """[{"stat":"damage","kind":"increased","value":1}]""");
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Unknown_field_is_a_load_error(string file, string target, string? setupPath, string? setupJson)
    {
        var dir = FixturePaths.CopyTiny();
        var path = Path.Combine(dir, file);
        var root = JsonNode.Parse(File.ReadAllText(path), documentOptions: new() { CommentHandling = System.Text.Json.JsonCommentHandling.Skip })!;
        if (setupPath != null) Set(root, setupPath, JsonNode.Parse(setupJson!)!);

        var owner = Navigate(root, target);
        Assert.IsType<JsonObject>(owner);
        owner.AsObject()[Bogus] = 1;
        File.WriteAllText(path, root.ToJsonString());

        var recordId = root is JsonArray arr ? arr[int.Parse(target.Split('/')[0])]!["id"]!.GetValue<string>() : null;
        var res = DataLoader.Load(dir);
        Assert.Null(res.Data);
        var err = Assert.Single(res.Errors, e => e.Message.Contains(Bogus));
        Assert.Equal(file, err.File);
        Assert.Equal(recordId, err.Id);
        Assert.Contains("Неизвестное поле", err.Message);
        Assert.All(res.Errors, e => Assert.Contains(Bogus, e.Message));
    }

    [Fact]
    public void Per_point_is_unknown_on_effect_types_that_do_not_use_it()
    {
        var dir = FixturePaths.CopyTiny();
        var path = Path.Combine(dir, "skills.json");
        var root = JsonNode.Parse(File.ReadAllText(path))!;
        Set(root, "0/tree/0/effects/0", JsonNode.Parse("""{"type":"add_tag","tag":"fire","per_point":false}"""));
        File.WriteAllText(path, root.ToJsonString());
        var res = DataLoader.Load(dir);
        var err = Assert.Single(res.Errors);
        Assert.Contains("Неизвестное поле «per_point»", err.Message);
    }

    [Fact]
    public void Per_point_is_allowed_on_modifier_add_behavior_and_add_conversion()
    {
        var dir = FixturePaths.CopyTiny();
        var path = Path.Combine(dir, "skills.json");
        var root = JsonNode.Parse(File.ReadAllText(path))!;
        Set(root, "0/tree/0/effects", JsonNode.Parse("""
            [{"type":"modifier","stat":"damage","kind":"increased","value":1,"per_point":false},
             {"type":"add_behavior","param":"chain_count","delta":1,"per_point":false},
             {"type":"add_conversion","from":"physical","to":"fire","percent":10,"per_point":false}]
            """));
        File.WriteAllText(path, root.ToJsonString());
        Assert.Empty(DataLoader.Load(dir).Errors);
    }

    [Fact]
    public void Dictionary_like_objects_keep_their_own_key_validation()
    {
        var dir = FixturePaths.CopyTiny();
        var path = Path.Combine(dir, "skills.json");
        var root = JsonNode.Parse(File.ReadAllText(path))!;
        root[0]!["behavior"] = new JsonObject { ["not_a_key"] = 1 };
        File.WriteAllText(path, root.ToJsonString());
        var err = Assert.Single(DataLoader.Load(dir).Errors);
        Assert.Contains("Неизвестный параметр поведения", err.Message);
    }

    [Fact]
    public void Tiny_and_data_only_fixtures_still_load_cleanly()
    {
        Assert.Empty(DataLoader.Load(FixturePaths.Tiny).Errors);
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
        Assert.Empty(DataLoader.Load(dir).Errors);
    }

    [Fact]
    public void Real_data_still_loads_cleanly()
        => Assert.Empty(DataLoader.Load(FixturePaths.RealData).Errors);

    private static JsonNode Navigate(JsonNode root, string path)
    {
        var node = root;
        foreach (var seg in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
            node = int.TryParse(seg, out var i) ? node[i]! : node[seg]!;
        return node;
    }

    private static void Set(JsonNode root, string path, JsonNode? value)
    {
        var segs = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var parent = Navigate(root, string.Join('/', segs[..^1]));
        if (int.TryParse(segs[^1], out var i)) parent.AsArray()[i] = value;
        else parent.AsObject()[segs[^1]] = value;
    }
}
