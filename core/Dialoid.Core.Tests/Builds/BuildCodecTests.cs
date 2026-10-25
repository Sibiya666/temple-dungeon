using System.IO.Compression;
using System.Text;
using Dialoid.Core.Builds;
using Dialoid.Core.Character;
using Dialoid.Core.Data;
using Dialoid.Core.Tests.Fixtures;
using Attribute = Dialoid.Core.Character.Attribute;

namespace Dialoid.Core.Tests.Builds;

public class BuildCodecTests
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

    private static string Encode(string json, string prefix = "DLD1:")
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.SmallestSize, true)) gz.Write(Encoding.UTF8.GetBytes(json));
        return prefix + Convert.ToBase64String(ms.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    [Fact]
    public void Export_import_roundtrip_preserves_build()
    {
        var b = Sample();
        var code = BuildCodec.Export(b);
        Assert.StartsWith("DLD1:", code);
        Assert.DoesNotContain('+', code);
        Assert.DoesNotContain('/', code);
        Assert.DoesNotContain('=', code);
        Assert.True(BuildCodec.TryImport(code, Tiny, out var back, out var error), error);
        Assert.Null(error);
        Assert.NotNull(back);
        Assert.Equal(b.ClassId, back!.ClassId);
        Assert.Equal(b.Level, back.Level);
        Assert.Equal(b.AscendancyId, back.AscendancyId);
        Assert.Equal(b.WeaponId, back.WeaponId);
        Assert.Equal(b.PassiveNodes, back.PassiveNodes);
        Assert.Equal(Attribute.Dex, back.AttributeChoices["attr_node"]);
        Assert.Equal(b.ItemIds, back.ItemIds);
        Assert.Equal(b.Rotation, back.Rotation);
        var slot = Assert.Single(back.Slots);
        Assert.Equal("test_slash", slot.SkillId);
        Assert.Equal(3, slot.Level);
        Assert.Equal(2, slot.NodePoints["slash_depth"]);
        Assert.Equal(code, BuildCodec.Export(back));   // stable
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("DLD1:")]
    [InlineData("DLD1:%%%")]
    public void Import_rejects_garbage_with_reason(string code)
    {
        Assert.False(BuildCodec.TryImport(code, Tiny, out var build, out var error));
        Assert.Null(build);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Import_rejects_null_and_truncated_and_bad_payloads_without_throwing()
    {
        Assert.False(BuildCodec.TryImport(null!, Tiny, out _, out var e0));
        Assert.NotNull(e0);

        var code = BuildCodec.Export(Sample());
        Assert.False(BuildCodec.TryImport(code[..(5 + (code.Length - 5) / 8 * 4)], Tiny, out _, out var e1));
        Assert.Contains("повреждён", e1!);   // gzip or JSON level, depending on where the cut falls

        Assert.False(BuildCodec.TryImport("DLD1:" + Convert.ToBase64String(Encoding.UTF8.GetBytes("not gzip")), Tiny, out _, out var e2));
        Assert.Contains("gzip", e2!, StringComparison.OrdinalIgnoreCase);

        Assert.False(BuildCodec.TryImport(Encode("{ not json"), Tiny, out _, out var e3));
        Assert.Contains("JSON", e3!);
        Assert.False(BuildCodec.TryImport(Encode("null"), Tiny, out _, out var e4));
        Assert.NotNull(e4);
        Assert.False(BuildCodec.TryImport(Encode("{\"level\":\"много\"}"), Tiny, out _, out var e5));
        Assert.Contains("JSON", e5!);
    }

    [Fact]
    public void Import_rejects_wrong_prefix_version()
    {
        var payload = BuildCodec.Export(Sample())["DLD1:".Length..];
        Assert.False(BuildCodec.TryImport("DLD2:" + payload, Tiny, out _, out var v));
        Assert.Contains("верси", v!);
        Assert.False(BuildCodec.TryImport("XYZ1:" + payload, Tiny, out _, out var p));
        Assert.Contains("префикс", p!);
        Assert.False(BuildCodec.TryImport(payload, Tiny, out _, out var m));
        Assert.Contains("префикс", m!);
    }

    [Theory]
    [InlineData("class")]
    [InlineData("skill")]
    [InlineData("passive")]
    [InlineData("ascendancy")]
    [InlineData("ascendancy_node")]
    [InlineData("skill_node")]
    [InlineData("weapon")]
    [InlineData("item")]
    public void Import_rejects_unknown_ids_with_reason(string what)
    {
        var b = Sample();
        string id = "no_such_id";
        switch (what)
        {
            case "class": b.ClassId = id; break;
            case "skill": b.Slots[0].SkillId = id; break;
            case "passive": b.PassiveNodes.Add(id); break;
            case "ascendancy": b.AscendancyId = id; break;
            case "ascendancy_node": b.AscendancyNodes.Add(id); break;
            case "skill_node": b.Slots[0].NodePoints[id] = 1; break;
            case "weapon": b.WeaponId = id; break;
            case "item": b.ItemIds.Add(id); break;
        }
        Assert.False(BuildCodec.TryImport(BuildCodec.Export(b), Tiny, out var back, out var error));
        Assert.Null(back);
        Assert.Contains(id, error!);
    }

    [Fact]
    public void Legal_violations_do_not_block_import()
    {
        // Overspent passives, skill above cap, locked slot: illegal but all ids are known.
        var b = Sample();
        b.Level = 1;
        b.Slots[0].Level = 50;
        b.Slots.Add(new SkillSlot { SkillId = "test_slam" });
        b.PassiveNodes.Add("small_b");
        Assert.NotEmpty(BuildRules.Validate(b, Tiny));
        Assert.True(BuildCodec.TryImport(BuildCodec.Export(b), Tiny, out var back, out var error), error);
        Assert.Equal(1, back!.Level);
        Assert.Equal(2, back.Slots.Count);
    }
}
