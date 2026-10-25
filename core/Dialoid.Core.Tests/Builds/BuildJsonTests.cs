using Dialoid.Core.Builds;
using Dialoid.Core.Character;
using Dialoid.Core.Data;
using Dialoid.Core.Tests.Fixtures;
using Attribute = Dialoid.Core.Character.Attribute;

namespace Dialoid.Core.Tests.Builds;

public class BuildJsonTests
{
    private static readonly GameData Tiny = DataLoader.Load(FixturePaths.Tiny).Data!;

    private static Build Sample()
    {
        var b = new Build { ClassId = "test_warrior", Level = 20, WeaponId = "test_sword", AscendancyId = "test_asc" };
        b.PassiveNodes.AddRange(new[] { "small_a", "attr_node" });
        b.AttributeChoices["attr_node"] = Attribute.Dex;
        b.Slots.Add(new SkillSlot { SkillId = "test_slash", Level = 3, NodePoints = { ["slash_depth"] = 2 } });
        b.ItemIds.Add("test_cloud_ring");
        b.Rotation.Add("test_slash");
        return b;
    }

    [Fact]
    public void Build_json_roundtrip()
    {
        var b = Sample();
        var json = BuildCodec.ToJson(b);
        Assert.Contains("\"class_id\": \"test_warrior\"", json);   // snake_case, indented
        Assert.Contains("\"attr_node\": \"dex\"", json);
        Assert.True(BuildCodec.TryFromJson(json, Tiny, out var back, out var error), error);
        Assert.Null(error);
        Assert.Equal(json, BuildCodec.ToJson(back!));
        Assert.Equal(BuildCodec.Export(b), BuildCodec.Export(back!));
    }

    [Fact]
    public void Build_json_unknown_id_fails_with_reason()
    {
        var b = Sample();
        b.PassiveNodes.Add("no_such_node");
        Assert.False(BuildCodec.TryFromJson(BuildCodec.ToJson(b), Tiny, out var back, out var error));
        Assert.Null(back);
        Assert.Contains("no_such_node", error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{\"class_id\": ")]
    [InlineData("[1,2,3]")]
    [InlineData("null")]
    [InlineData("{\"slots\":[null]}")]
    [InlineData("{\"level\":\"high\"}")]
    public void Build_json_garbage_fails_without_throwing(string json)
    {
        Assert.False(BuildCodec.TryFromJson(json, Tiny, out var back, out var error));
        Assert.Null(back);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void Build_json_null_input_fails_without_throwing()
    {
        Assert.False(BuildCodec.TryFromJson(null!, Tiny, out var back, out var error));
        Assert.Null(back);
        Assert.False(string.IsNullOrEmpty(error));
    }
}
