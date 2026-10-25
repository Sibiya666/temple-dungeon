using System.Text.Json.Nodes;
using Dialoid.Core.Character;
using Dialoid.Core.Data;
using Dialoid.Core.Effects;
using Dialoid.Core.Passives;
using Dialoid.Core.Stats;
using Dialoid.Core.Tests.Fixtures;
using Attr = Dialoid.Core.Character.Attribute;

namespace Dialoid.Core.Tests.Passives;

public class PassiveTreeTests
{
    private static readonly GameData Data = DataLoader.Load(FixturePaths.Tiny).Data!;
    private const string Start = "test_start_warrior";

    private static CompiledCharacter Compile(Build b) => new CharacterCompiler(Data).Compile(b);

    private static Build Warrior(int level, params string[] passives) =>
        new() { ClassId = "test_warrior", Level = level, PassiveNodes = passives.ToList() };

    private static double Sum(CompiledCharacter c, string stat, ModKind kind, string sourceKind) =>
        c.Evaluator.Evaluate(stat, TagSet.Empty).Lines.Where(l => l.Kind == kind && l.Source.Kind == sourceKind).Sum(l => l.Value);

    [Fact]
    public void Passive_points_are_level_minus_one()
    {
        Assert.Equal(59, PassiveTree.PointsForLevel(60));
        Assert.Equal(0, PassiveTree.PointsForLevel(1));
    }

    [Fact]
    public void Tiny_tree_has_eight_nodes_and_symmetric_edges()
    {
        var t = Data.Passives;
        Assert.Equal(8, t.Nodes.Count);
        Assert.Contains("small_a", t.Edges[Start]);
        Assert.Contains(Start, t.Edges["small_a"]);
        Assert.Equal(PassiveKind.Keystone, t.Nodes["test_keystone"].Kind);
        Assert.Equal("test_warrior", t.Nodes[Start].StartForClassId);
        Assert.Equal(Start, t.StartForClass("test_warrior")!.Id);
    }

    [Fact]
    public void Can_allocate_only_adjacent_to_allocated()
    {
        var t = Data.Passives;
        var set = new HashSet<string> { Start };
        Assert.True(t.CanAllocate(set, "small_a"));
        Assert.False(t.CanAllocate(set, "small_b"));
        Assert.False(t.CanAllocate(set, "test_start_mage"));
        Assert.False(t.CanAllocate(set, "nope"));
        set.Add("small_a");
        Assert.True(t.CanAllocate(set, "small_b"));
        Assert.True(t.CanAllocate(set, "attr_node"));
        Assert.False(t.CanAllocate(set, "small_a"));
    }

    [Fact]
    public void Cannot_deallocate_node_that_disconnects_others()
    {
        var t = Data.Passives;
        var set = new HashSet<string> { Start, "small_a", "small_b" };
        Assert.False(t.CanDeallocate(set, "small_a", Start));
        Assert.True(t.CanDeallocate(set, "small_b", Start));
        Assert.False(t.CanDeallocate(set, Start, Start));
        Assert.False(t.CanDeallocate(set, "small_c", Start));
    }

    [Fact]
    public void Connectivity_is_checked_from_the_class_start()
    {
        var t = Data.Passives;
        Assert.True(t.IsConnectedToStart(new[] { "small_a", "small_b" }, Start));
        Assert.True(t.IsConnectedToStart(Array.Empty<string>(), Start));
        Assert.False(t.IsConnectedToStart(new[] { "small_b" }, Start));
        Assert.False(t.IsConnectedToStart(new[] { "small_a", "nope" }, Start));
    }

    [Fact]
    public void Attribute_node_requires_choice_and_gives_plus_five()
    {
        Assert.Throws<ArgumentException>(() => Compile(Warrior(60, "small_a", "attr_node")));
        var b = Warrior(60, "small_a", "attr_node");
        b.AttributeChoices["attr_node"] = Attr.Dex;
        var c = Compile(b);
        Assert.Equal(5, Sum(c, "dex", ModKind.Flat, "passive"));
        Assert.Equal(69 + 5, c.Evaluator.Evaluate("dex", TagSet.Empty).Value);
        Assert.Equal(66, c.Evaluator.Evaluate("int", TagSet.Empty).Value);
    }

    [Fact]
    public void Ascendancy_unlocks_at_20_with_points_2_2_2_2()
    {
        Assert.Equal(0, Ascendancy.PointsForLevel(1));
        Assert.Equal(0, Ascendancy.PointsForLevel(19));
        Assert.Equal(2, Ascendancy.PointsForLevel(20));
        Assert.Equal(2, Ascendancy.PointsForLevel(29));
        Assert.Equal(4, Ascendancy.PointsForLevel(35));
        Assert.Equal(6, Ascendancy.PointsForLevel(40));
        Assert.Equal(8, Ascendancy.PointsForLevel(50));
        Assert.Equal(8, Ascendancy.PointsForLevel(60));
    }

    [Fact]
    public void Ascendancy_notables_cost_two()
    {
        var asc = Data.Ascendancies["test_asc"];
        Assert.Equal(0, asc.Start.Cost);
        Assert.All(asc.Notables, n => Assert.Equal(2, n.Cost));
        Assert.Equal(4, Ascendancy.PointsForLevel(50) / asc.Notables[0].Cost);
        Assert.Equal("test_warrior", asc.ClassId);
    }

    [Fact]
    public void Passive_effects_split_into_global_modifiers_and_deferred()
    {
        var c = Compile(Warrior(60, "small_a", "small_b", "notable_a", "test_keystone"));
        Assert.Equal(15, Sum(c, "damage", ModKind.Increased, "passive"));
        Assert.Equal(30, Sum(c, "damage", ModKind.More, "keystone"));
        Assert.Equal(10, Sum(c, "life", ModKind.Flat, "passive"));
        Assert.Contains(c.GlobalModifiers, m => m.Stat == "damage" && m.Source.Kind == "keystone" && m.Source.Id == "test_keystone");
        var d = Assert.Single(c.DeferredEffects);
        Assert.Equal(new AddBehaviorEffect("pierce_count", 1, new Scope.Skill("test_bolt")), d.Effect);
        Assert.Equal("passive", d.Source.Kind);
        Assert.Equal("notable_a", d.Source.Id);
    }

    [Fact]
    public void Unallocated_nodes_have_no_effect()
    {
        var c = Compile(Warrior(60));
        Assert.Equal(0, Sum(c, "damage", ModKind.Increased, "passive"));
        Assert.Empty(c.DeferredEffects);
    }

    [Fact]
    public void Ascendancy_start_and_notables_add_effects()
    {
        var b = Warrior(50);
        b.AscendancyId = "test_asc";
        b.AscendancyNodes = new() { "asc_n1", "asc_n3" };
        var c = Compile(b);
        Assert.Equal(15, Sum(c, "damage", ModKind.Increased, "ascendancy"));
        Assert.Equal(20, Sum(c, "damage", ModKind.More, "ascendancy"));
        var d = Assert.Single(c.DeferredEffects);
        Assert.Equal("ascendancy", d.Source.Kind);
        Assert.Equal("asc_n3", d.Source.Id);
        Assert.Equal(new Scope.Tags(TagSet.Of("spell")), d.Effect.Scope);

        var none = Compile(Warrior(50));
        Assert.Equal(0, Sum(none, "damage", ModKind.Increased, "ascendancy"));
    }

    [Fact]
    public void Compiling_unknown_nodes_throws()
    {
        Assert.Throws<ArgumentException>(() => Compile(Warrior(10, "nope")));
        var b = Warrior(50); b.AscendancyId = "nope";
        Assert.Throws<ArgumentException>(() => Compile(b));
        b = Warrior(50); b.AscendancyId = "test_asc"; b.AscendancyNodes = new() { "nope" };
        Assert.Throws<ArgumentException>(() => Compile(b));
    }

    [Fact]
    public void Mechanic_effects_go_to_mechanics()
    {
        var reg = new MechanicRegistry();
        reg.Register(new TestMech());
        var dir = FixturePaths.CopyTiny();
        var p = Path.Combine(dir, "passives.json");
        var arr = JsonNode.Parse(File.ReadAllText(p))!.AsArray();
        var small = arr.First(n => n!["id"]!.GetValue<string>() == "small_a")!;
        small["effects"] = JsonNode.Parse("[{\"type\":\"mechanic\",\"id\":\"test_mech\"}]");
        File.WriteAllText(p, arr.ToJsonString());
        var r = DataLoader.Load(dir, reg);
        Assert.Empty(r.Errors);
        var c = new CharacterCompiler(r.Data!).Compile(Warrior(10, "small_a"));
        Assert.Equal("test_mech", Assert.Single(c.Mechanics).MechanicId);
        Assert.Empty(c.DeferredEffects);
    }

    private sealed class TestMech : IMechanic { public string Id => "test_mech"; }
}
