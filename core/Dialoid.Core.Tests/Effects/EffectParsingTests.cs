using System.Text.Json;
using Dialoid.Core.Combat;
using Dialoid.Core.Data;
using Dialoid.Core.Data.Json;
using Dialoid.Core.Effects;
using Dialoid.Core.Stats;

namespace Dialoid.Core.Tests.Effects;

public class EffectParsingTests
{
    private sealed class TestMech : IMechanic { public string Id => "test_mech"; }

    private static (Effect? e, List<DataError> errs) Parse(string json, Scope? scope = null)
    {
        var reg = new MechanicRegistry();
        reg.Register(new TestMech());
        using var doc = JsonDocument.Parse(json);
        var errs = new List<DataError>();
        var e = EffectJson.Parse(doc.RootElement, scope ?? new Scope.Global(), reg, "f.json", "owner_id", errs);
        return (e, errs);
    }

    [Theory]
    [InlineData("modifier", "{\"type\":\"modifier\",\"stat\":\"damage\",\"kind\":\"increased\",\"value\":10}", typeof(ModifierEffect))]
    [InlineData("add_tag", "{\"type\":\"add_tag\",\"tag\":\"fire\"}", typeof(AddTagEffect))]
    [InlineData("remove_tag", "{\"type\":\"remove_tag\",\"tag\":\"cold\"}", typeof(RemoveTagEffect))]
    [InlineData("set_behavior", "{\"type\":\"set_behavior\",\"param\":\"pierce_count\",\"value\":2}", typeof(SetBehaviorEffect))]
    [InlineData("add_behavior", "{\"type\":\"add_behavior\",\"param\":\"chain_count\",\"delta\":1}", typeof(AddBehaviorEffect))]
    [InlineData("add_conversion", "{\"type\":\"add_conversion\",\"from\":\"physical\",\"to\":\"fire\",\"percent\":30}", typeof(AddConversionEffect))]
    [InlineData("set_damage_type", "{\"type\":\"set_damage_type\",\"to\":\"cold\"}", typeof(SetDamageTypeEffect))]
    [InlineData("set_resource", "{\"type\":\"set_resource\",\"field\":\"cost\",\"value\":5}", typeof(SetResourceEffect))]
    [InlineData("grant_proc", "{\"type\":\"grant_proc\",\"trigger\":{\"every_n_hits\":10},\"action\":{\"apply_buff\":{\"id\":\"b\",\"duration\":8,\"modifiers\":[{\"stat\":\"damage\",\"kind\":\"more\",\"value\":20}]}}}", typeof(GrantProcEffect))]
    [InlineData("mechanic", "{\"type\":\"mechanic\",\"id\":\"test_mech\",\"params\":{\"x\":3}}", typeof(MechanicEffect))]
    public void Every_effect_type_parses(string name, string json, Type expected)
    {
        var (e, errs) = Parse(json);
        Assert.Empty(errs);
        Assert.IsType(expected, e);
        Assert.Equal(new Scope.Global(), e!.Scope);
        _ = name;
    }

    [Fact]
    public void Fields_are_read_correctly()
    {
        var (e, _) = Parse("{\"type\":\"add_conversion\",\"from\":\"physical\",\"to\":\"fire\",\"percent\":30,\"per_point\":false}");
        Assert.Equal(new AddConversionEffect(DamageType.Physical, DamageType.Fire, 30, new Scope.Global(), false), e);
        var (p, _) = Parse("{\"type\":\"grant_proc\",\"trigger\":\"on_crit\",\"action\":{\"cast_skill\":{\"id\":\"nova\",\"damage_percent\":50}}}");
        Assert.Equal(new GrantProcEffect(new Trigger.OnCrit(), new ProcAction.CastSkill("nova", 50), new Scope.Global()), p);
        var (m, _) = Parse("{\"type\":\"mechanic\",\"id\":\"test_mech\",\"params\":{\"x\":3}}");
        Assert.Equal(3, ((MechanicEffect)m!).Params["x"].GetInt32());
    }

    [Fact]
    public void Scopes_parse()
    {
        Assert.Equal(new Scope.Skill("fireball"), Parse("{\"type\":\"add_tag\",\"tag\":\"x\",\"scope\":\"skill:fireball\"}").e!.Scope);
        Assert.Equal(new Scope.Tags(TagSet.Of("fire", "spell")), Parse("{\"type\":\"add_tag\",\"tag\":\"x\",\"scope\":{\"tags\":[\"spell\",\"fire\"]}}").e!.Scope);
        Assert.Equal(new Scope.Global(), Parse("{\"type\":\"add_tag\",\"tag\":\"x\",\"scope\":\"global\"}", new Scope.Skill("o")).e!.Scope);
        Assert.NotEmpty(Parse("{\"type\":\"add_tag\",\"tag\":\"x\",\"scope\":\"nonsense\"}").errs);
    }

    [Fact]
    public void Unknown_effect_type_reports_data_error()
    {
        var (e, errs) = Parse("{\"type\":\"explode\"}");
        Assert.Null(e);
        var err = Assert.Single(errs);
        Assert.Equal("f.json", err.File);
        Assert.Equal("owner_id", err.Id);
        Assert.Contains("explode", err.Message);
    }

    [Fact]
    public void Skill_node_default_scope_is_owner_skill()
    {
        var (e, errs) = Parse("{\"type\":\"add_tag\",\"tag\":\"x\"}", new Scope.Skill("owner_id"));
        Assert.Empty(errs);
        Assert.Equal(new Scope.Skill("owner_id"), e!.Scope);
    }

    [Fact]
    public void Unknown_mechanic_id_reports_data_error()
    {
        var (e, errs) = Parse("{\"type\":\"mechanic\",\"id\":\"nope\"}");
        Assert.Null(e);
        Assert.Contains("nope", Assert.Single(errs).Message);
    }

    [Theory]
    [InlineData("{\"type\":\"add_tag\"}")]
    [InlineData("{\"type\":\"add_conversion\",\"from\":\"fire\",\"to\":\"physical\",\"percent\":10}")]
    [InlineData("{\"type\":\"add_conversion\",\"from\":\"water\",\"to\":\"fire\",\"percent\":10}")]
    [InlineData("{\"type\":\"set_resource\",\"field\":\"mana\",\"value\":1}")]
    [InlineData("{\"type\":\"grant_proc\",\"trigger\":\"on_sneeze\",\"action\":{\"apply_ailment\":\"ignite\"}}")]
    [InlineData("{\"type\":\"grant_proc\",\"trigger\":\"on_hit\",\"action\":{\"teleport\":1}}")]
    [InlineData("{\"type\":\"grant_proc\",\"trigger\":{\"every_n_uses\":0},\"action\":{\"apply_ailment\":\"ignite\"}}")]
    [InlineData("{\"type\":\"modifier\",\"kind\":\"flat\",\"value\":1}")]
    public void Bad_input_reports_error_not_exception(string json)
    {
        var (e, errs) = Parse(json);
        Assert.Null(e);
        Assert.NotEmpty(errs);
    }

    [Fact]
    public void Dark_damage_type_parses_in_effects()
    {
        var (set, e1) = Parse("{\"type\":\"set_damage_type\",\"to\":\"dark\"}");
        Assert.Empty(e1);
        Assert.Equal(DamageType.Dark, Assert.IsType<SetDamageTypeEffect>(set).To);
        var (conv, e2) = Parse("{\"type\":\"add_conversion\",\"from\":\"physical\",\"to\":\"dark\",\"percent\":30}");
        Assert.Empty(e2);
        Assert.Equal(DamageType.Dark, Assert.IsType<AddConversionEffect>(conv).To);
    }

    [Fact]
    public void Unknown_damage_type_message_lists_dark()
    {
        var (e, errs) = Parse("{\"type\":\"set_damage_type\",\"to\":\"water\"}");
        Assert.Null(e);
        Assert.Contains("ожидается physical, lightning, cold, fire, poison или dark", Assert.Single(errs).Message);
    }

    [Fact]
    public void ParseList_collects_valid_effects_and_errors()
    {
        using var doc = JsonDocument.Parse("[{\"type\":\"add_tag\",\"tag\":\"a\"},{\"type\":\"zzz\"}]");
        var errs = new List<DataError>();
        var list = EffectJson.ParseList(doc.RootElement, new Scope.Global(), new MechanicRegistry(), "f.json", "o", errs);
        Assert.Single(list);
        Assert.Single(errs);
    }

    [Fact]
    public void Registry_rejects_duplicates_and_default_is_singleton()
    {
        var reg = new MechanicRegistry();
        reg.Register(new TestMech());
        Assert.Throws<ArgumentException>(() => reg.Register(new TestMech()));
        Assert.True(reg.Contains("test_mech"));
        Assert.Same(MechanicRegistry.Default, MechanicRegistry.Default);
    }
}
