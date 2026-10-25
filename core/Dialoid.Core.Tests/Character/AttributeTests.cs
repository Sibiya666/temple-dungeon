using Dialoid.Core.Character;
using Dialoid.Core.Data;
using Dialoid.Core.Stats;
using Dialoid.Core.Tests.Fixtures;
using Attr = Dialoid.Core.Character.Attribute;

namespace Dialoid.Core.Tests.Character;

public class AttributeTests
{
    private static readonly GameData Data = DataLoader.Load(FixturePaths.Tiny).Data!;

    private static CompiledCharacter Compile(string cls, int level) =>
        new CharacterCompiler(Data).Compile(new Build { ClassId = cls, Level = level });

    private static double Val(CompiledCharacter c, string stat, params string[] tags) =>
        c.Evaluator.Evaluate(stat, TagSet.Of(tags)).Value;

    private static double Sum(CompiledCharacter c, string stat, ModKind kind, string sourceKind, params string[] tags) =>
        c.Evaluator.Evaluate(stat, TagSet.Of(tags)).Lines.Where(l => l.Kind == kind && l.Source.Kind == sourceKind).Sum(l => l.Value);

    [Fact]
    public void Attributes_grow_plus_one_all_and_plus_one_primary_per_level()
    {
        var w = Compile("test_warrior", 60);
        Assert.Equal(133, Val(w, "str"));
        Assert.Equal(69, Val(w, "dex"));
        Assert.Equal(69, Val(w, "will"));
        Assert.Equal(66, Val(w, "int"));
        Assert.Equal(15, Val(Compile("test_warrior", 1), "str"));
    }

    [Fact]
    public void Primary_attribute_gives_more_damage_025_per_point()
    {
        var w = Compile("test_warrior", 60);
        Assert.Equal(33.25, Sum(w, "damage", ModKind.More, "attribute"), 9);
    }

    [Fact]
    public void General_attribute_bonuses()
    {
        var m = Compile("test_mage", 60); // str 66, int 133, will 69, dex 67
        Assert.Equal(66, Val(m, "str"));
        Assert.Equal(132, Sum(m, "armor", ModKind.Flat, "attribute"), 9);
        foreach (var r in new[] { "resist_fire", "resist_cold", "resist_lightning", "resist_poison" })
            Assert.Equal(13.3, Sum(m, r, ModKind.Flat, "attribute"), 9);
        Assert.Equal(13.8, Sum(m, "resource_gen", ModKind.Increased, "attribute"), 9);
        Assert.Equal(13.8, Sum(m, "life_regen_pct", ModKind.Increased, "attribute"), 9);
        Assert.Equal(3.35, Sum(m, "dodge_chance", ModKind.Flat, "attribute"), 9);
    }

    [Fact]
    public void Life_formula_with_class_multiplier()
    {
        Assert.Equal(111, Val(Compile("test_mage", 1), "life"), 9);
        Assert.Equal(760, Val(Compile("test_mage", 60), "life"), 9);
        Assert.Equal(912, Val(Compile("test_warrior", 60), "life"), 9);
    }

    [Fact]
    public void Mana_max_grows_from_intelligence()
    {
        var m = Compile("test_mage", 60);
        Assert.Equal(166.5, Val(m, "resource_max"), 9);
        Assert.Equal(100, Val(Compile("test_warrior", 60), "resource_max"), 9);
        Assert.Equal(100, Data.Classes["test_mage"].Resource.BaseMax);
        Assert.Equal(new PerStat("int", 2, null), Data.Classes["test_mage"].Resource.MaxPer);
    }

    [Fact]
    public void Class_bonuses_apply()
    {
        var w = Compile("test_warrior", 60); // will 69, dex 69
        Assert.Equal(13.8, Sum(w, "resource_gen", ModKind.Increased, "class"), 9);
        Assert.Equal(69 * 0.04, Sum(w, "crit_chance", ModKind.Flat, "class", "attack"), 9);
        Assert.Equal(0, Sum(w, "crit_chance", ModKind.Flat, "class", "spell"), 9);
        var m = Compile("test_mage", 60);
        Assert.Equal(13.8, Sum(m, "resource_regen", ModKind.Increased, "class"), 9);
        Assert.Equal(67 * 0.04, Sum(m, "crit_chance", ModKind.Flat, "class", "spell"), 9);
    }

    [Fact]
    public void Warrior_fury_resource_definition()
    {
        var r = Data.Classes["test_warrior"].Resource;
        Assert.Equal((100, 0, 5, 3), (r.BaseMax, r.RegenPerSec, r.DecayPerSec, r.DecayDelaySec));
        Assert.Null(r.MaxPer);
    }

    [Fact]
    public void Life_regen_base_is_half_percent()
    {
        var w = Compile("test_warrior", 1);
        Assert.Equal(0.5, w.Evaluator.Evaluate("life_regen_pct", TagSet.Empty).Base);
    }

    [Fact]
    public void Unknown_class_throws()
    {
        Assert.Throws<ArgumentException>(() => Compile("nope", 1));
    }

    [Fact]
    public void Missing_classes_file_gives_empty_catalog()
    {
        var dir = FixturePaths.CopyTiny();
        File.Delete(Path.Combine(dir, "classes.json"));
        File.Delete(Path.Combine(dir, "attributes.json"));
        var r = DataLoader.Load(dir);
        Assert.True(r.Ok);
        Assert.Empty(r.Data!.Classes);
    }

    [Fact]
    public void Class_with_unknown_stat_reports_russian_error()
    {
        var dir = FixturePaths.CopyTiny();
        var p = Path.Combine(dir, "classes.json");
        File.WriteAllText(p, File.ReadAllText(p).Replace("\"crit_chance\"", "\"no_such_stat\""));
        var r = DataLoader.Load(dir);
        Assert.False(r.Ok);
        Assert.All(r.Errors, e => Assert.Equal("classes.json", e.File));
        Assert.Contains(r.Errors, e => e.Message.Contains("no_such_stat"));
    }
}
