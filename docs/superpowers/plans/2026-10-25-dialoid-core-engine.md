# Dialoid Core Engine Implementation Plan (план 1 из 3)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Чистая C#-библиотека `Dialoid.Core`: статы и модификаторы, классы и атрибуты, пассивное дерево, асценданты, скиллы с деревьями, боевая математика, недуги, детерминированная симуляция, валидатор данных и сериализация билдов. Все проверяется `dotnet test` на небольших тестовых данных.

**Architecture:** Библиотека без зависимости от Godot. Содержимое игры грузится из JSON в неизменяемую модель `GameData`, валидатор проверяет ее целиком. Билд (`Build`) компилируется в `CompiledCharacter`: глобальные модификаторы и скомпилированные скиллы. Расчет любого стата идет через один `StatEvaluator` с разбором по источникам. Симулятор пошаговый, один движок с двумя источниками случайности: «бросок» и «среднее».

**Tech Stack:** .NET 8 SDK (`net8.0`), C# 12, `System.Text.Json`, xUnit, `dotnet test`. Godot в этом плане не нужен.

**Spec:** `docs/superpowers/specs/2026-10-25-dialoid-core-systems-design.md` (утвержден владельцем 2026-10-25). Исполнитель читает и спеку, и план. Ссылки вида «§6.3» ведут на разделы спеки.

## Дорожная карта трех планов

1. **План 1 (этот): ядро.** Механизмы и тесты на минимальных данных-фикстурах, без полного контента.
2. **План 2: контент и баланс.** Пишется после плана 1, на его интерфейсах. Включает `data/`: 2 класса, 12 скиллов с полными деревьями, 4 асценданта, пассивное дерево на ~160 узлов, 2 скилла из лута, оружие, 6 манекенов, 6 эталонных билдов. Плюс приемочные тесты §15.4–15.6, §15.9, §15.10 и калибровка §11.1.
3. **План 3: отладочная сцена в Godot.** Установка Godot 4 .NET, проект `game/` со ссылкой на `Dialoid.Core`, панели §13, headless-проверки §3.2, README.

## Global Constraints

- Целевая платформа `net8.0`. Если установленный Godot 4 .NET (план 3) потребует другую версию, ее меняют только в `core/Directory.Build.props`.
- `Dialoid.Core` **не ссылается** ни на какие пакеты Godot. Зависимости только BCL (`System.Text.Json`).
- Все `id` в данных записываются в `snake_case` и уникальны внутри своего типа. Ссылки идут только по `id`.
- Тексты для игрока (`name`, `description`) пишутся на русском и хранятся в данных, в коде их нет.
- Increased и More задаются в процентах (25 значит +25%), Flat задается в единицах стата. More ≥ −100.
- Формула стата: `(база + ΣFlat) × (1 + ΣIncreased/100) × Π(1 + More/100)`, затем ограничение min и max. Override с наибольшим `Priority` отменяет все остальные модификаторы (при равенстве берется наибольшее значение) (§4.3).
- Порядок конверсии: `Physical → Lightning → Cold → Fire → Poison`, конверсия идет только вправо (§6.3).
- Рост скилла: `1 + 0.05 × (L − 1)`. G: `1 + 0.06 × (L − 1)` (§6.1).
- Кап уровня 60. Пассивных очков 59 (1 за уровень с 2-го). Слоты скиллов открываются на уровнях 1, 4, 8, 14, 20. Кап уровня родного скилла 20, скилла из лута 10, с предметом до 14. Асцендант открывается на 20 уровне, очки 2/2/2/2 на уровнях 20, 30, 40 и 50.
- Режим «среднее» детерминирован: одинаковые входные данные дают одинаковые результаты до последнего бита.
- Код и имена в коде на английском. Сообщения валидатора для владельца на русском.
- Коммит после каждой задачи. Сообщения коммитов на английском, в стиле conventional commits.

## Review Focus

1. **Билд стал нелегальным после снижения уровня** (ползунок в песочнице двигают вниз): очков потрачено больше, чем доступно, скилл стоит в закрытом слоте. Ожидание: `BuildRules.Validate` возвращает понятные нарушения, `Simulator.Run` отказывает с ними же, без исключения. Тест в задаче 8.
2. **Ротация без урона** (в слотах только Боевой клич или пусто). Ожидание: DPS 0, симуляция завершается за отведенное время и не зацикливается. Тест в задаче 13.
3. **Ресурса никогда не хватает** (только спендер, Ярость стартует с 0). Ожидание: симуляция не зацикливается и не падает, DPS 0, в логе событие «нет ресурса». Тест в задаче 13.
4. **Битая строка импорта билда** (мусор, неизвестные `id`, обрезанный base64). Ожидание: `BuildCodec.TryImport` возвращает `false` с причиной, а не исключение. Тест в задаче 17.
5. **Крайние значения математики:** конверсия из нескольких источников больше 100%, сопротивление цели минус пробитие ниже −100, шанс крита выше 100%, More −100 при наличии других More. Ожидание: масштабирование до 100%, нижний предел −100, кап 100%, итог 0 (не отрицательный). Тесты в задачах 2 и 10.

---

## Структура файлов

```
core/
  Dialoid.sln
  Directory.Build.props              net8.0, nullable, LangVersion 12, TreatWarningsAsErrors
  Dialoid.Core/
    Dialoid.Core.csproj
    Stats/       TagSet.cs, Modifier.cs, StatDefinition.cs, StatEvaluator.cs, StatResult.cs, Conditions.cs
    Data/        GameData.cs, DataError.cs, DataLoader.cs, Json/*.cs (DTO и конвертеры), Validator.cs
    Effects/     Effect.cs (иерархия), Scope.cs, MechanicRegistry.cs, IMechanic.cs
    Character/   Attribute.cs, ClassDefinition.cs, Build.cs, BuildRules.cs, CharacterCompiler.cs, CompiledCharacter.cs
    Passives/    PassiveTree.cs, PassiveNode.cs, Ascendancy.cs
    Skills/      SkillDefinition.cs, SkillTree.cs, CompiledSkill.cs, SkillCompiler.cs
    Combat/      DamageType.cs, DamagePortion.cs, Growth.cs, BaseDamage.cs, Conversion.cs, HitCalculator.cs,
                 Mitigation.cs, Ailments.cs, Coverage.cs
    Simulation/  SimConfig.cs, SimResult.cs, Simulator.cs, RandomSource.cs, TargetState.cs, Rotation.cs, Procs.cs
    Mechanics/   Keystones.cs, BerserkerMechanics.cs, ThunderMechanics.cs, PyroMechanics.cs, CryoMechanics.cs
    Builds/      BuildCodec.cs
  Dialoid.Core.Tests/
    Dialoid.Core.Tests.csproj
    Fixtures/    tiny/ (минимальный набор JSON), broken/ (заведомо битые наборы), FixtureData.cs (C#-конструкторы)
    Stats/ Data/ Character/ Passives/ Skills/ Combat/ Simulation/ Mechanics/ Builds/   по одному *Tests.cs на модуль
data/                               (наполняется в плане 2; в этом плане только stats.json, tags.json, ailments.json)
README.md
.gitignore
```

Пояснения к разбиению:
- Каждый файл отвечает за одно. Самые сложные места (пайплайн удара, недуги, симулятор, механики) лежат отдельно, чтобы каждое можно было отревьюить само по себе.
- Фикстура `tiny` — минимальный мир для тестов: 1 класс «test_warrior» с 2 скиллами, 1 класс «test_mage» с 2 скиллами, пассивное дерево на 8 узлов, 1 асцендант на 3 notable, 2 оружия, манекены. Полный контент появится в плане 2.

---

### Task 0: Окружение, репозиторий, каркас решения

**Files:**
- Create: `.gitignore`, `README.md`, `core/Dialoid.sln`, `core/Directory.Build.props`, `core/Dialoid.Core/Dialoid.Core.csproj`, `core/Dialoid.Core.Tests/Dialoid.Core.Tests.csproj`, `core/Dialoid.Core.Tests/SmokeTests.cs`

**Interfaces:**
- Produces: собирающееся решение, команда тестов `dotnet test core/Dialoid.sln`.

- [ ] **Step 1: Убедиться, что .NET 8 SDK установлен.** Установку делает владелец: она требует пароль администратора.

```bash
brew install --cask dotnet-sdk
```
Проверка: `dotnet --list-sdks` выводит строку `8.0.x` или новее. Если SDK новее (9 или 10), это допустимо, потому что `net8.0` в нем собирается.

- [ ] **Step 2: `git init`, затем `.gitignore`** (`bin/`, `obj/`, `.godot/`, `*.user`, `.DS_Store`, `TestResults/`). Сохранить уже существующие `.claude/` и `docs/`.

- [ ] **Step 3: Создать решение и проекты**

```bash
cd core
dotnet new sln -n Dialoid
dotnet new classlib -n Dialoid.Core -f net8.0
dotnet new xunit -n Dialoid.Core.Tests -f net8.0
dotnet sln add Dialoid.Core Dialoid.Core.Tests
dotnet add Dialoid.Core.Tests reference Dialoid.Core
```
`Directory.Build.props`: `TargetFramework=net8.0`, `Nullable=enable`, `LangVersion=12`, `TreatWarningsAsErrors=true`, `ImplicitUsings=enable`. Удалить шаблонные `Class1.cs` и `UnitTest1.cs`.

- [ ] **Step 4: Смоук-тест** `SmokeTests.Solution_builds_and_runs` с `Assert.True(true)`.

- [ ] **Step 5: Запуск:** `dotnet test core/Dialoid.sln`. Ожидание: `Passed: 1`.

- [ ] **Step 6: README.md**: что за проект, ссылки на спеку и этот план, команда тестов, таблица «Статус» (план 1 в работе, планы 2 и 3 впереди).

- [ ] **Step 7: Commit:** `git add -A && git commit -m "chore: scaffold Dialoid.Core solution"`

---

### Task 1: Теги, модификаторы, вычисление стата с разбором

**Files:**
- Create: `core/Dialoid.Core/Stats/{TagSet,Modifier,StatDefinition,StatResult,StatEvaluator,Conditions}.cs`
- Test: `core/Dialoid.Core.Tests/Stats/StatEvaluatorTests.cs`

**Interfaces:**
- Produces:
```csharp
namespace Dialoid.Core.Stats;
public readonly record struct TagSet  // неизменяемое множество строк, сравнение по значению
{ static TagSet Empty; static TagSet Of(params string[] tags); bool Contains(string t); bool ContainsAll(TagSet other);
  TagSet With(string t); TagSet Without(string t); TagSet Union(TagSet o); IReadOnlyCollection<string> Items { get; } }
public enum ModKind { Flat, Increased, More, Override }
public enum StatDisplay { Number, Percent }
public sealed record StatDefinition(string Id, double Default, double Min, double Max, StatDisplay Display);
public sealed record SourceRef(string Kind, string Id, string Name);   // Kind: base|class|level|attribute|passive|ascendancy|keystone|skill_node|weapon|item|buff|mechanic
public sealed record Condition(string Id, double? Param = null, string? Ref = null);
public sealed record PerStat(string Stat, double Every, double? Cap);
public sealed record Modifier(string Stat, ModKind Kind, double Value, TagSet RequiredTags,
    IReadOnlyList<Condition> Conditions, PerStat? Per, SourceRef Source, int Priority = 0);
public interface IConditionContext { bool IsMet(Condition condition); }
public sealed record BreakdownLine(SourceRef Source, ModKind Kind, double Value);  // Value уже с учетом per
public sealed record StatResult(string Stat, double Base, double Uncapped, double Value,
    IReadOnlyList<BreakdownLine> Lines, BreakdownLine? AppliedOverride);
public sealed class StatEvaluator
{ public StatEvaluator(IReadOnlyDictionary<string, StatDefinition> defs, IReadOnlyList<Modifier> modifiers);
  public StatResult Evaluate(string stat, TagSet context, IConditionContext? conditions = null, double? baseValue = null);
  public StatEvaluator WithExtra(IReadOnlyList<Modifier> extra); }   // новый вычислитель с доп. модификаторами (баффы, скилл)
```

- [ ] **Step 1: Тесты** (определения статов в тестах задаются на месте: `life` Default 0, Min 0, Max 1e9; `resist_fire` Default 0, Min −100, Max 75):

```csharp
[Fact] void Flat_increased_more_follow_formula()
// base 100; Flat +20; Increased +30, +20; More +10, More +50 → (120)×1.5×1.1×1.5 = 297
[Fact] void Required_tags_filter_modifiers()
// Increased +50 requiredTags {fire}; context {spell} → 100; context {spell,fire} → 150
[Fact] void Override_highest_priority_wins_then_highest_value()
// Override 1 prio 5, Override 3 prio 5, Override 9 prio 1, Flat +100 → Value 3, AppliedOverride.Value 3
[Fact] void Result_is_capped_and_uncapped_kept()
// resist_fire: Flat 90 → Value 75, Uncapped 90
[Fact] void More_minus_100_zeroes_result_never_negative()
// More −100 and More +50 → 0
[Fact] void Breakdown_lists_every_applied_modifier_with_source()
// 3 модификатора из разных SourceRef → Lines.Count == 3, источники совпадают
[Fact] void Unmet_condition_excludes_modifier()
// Condition("target_ignited") с контекстом, где IsMet=false → не применяется
```

- [ ] **Step 2: Запуск** `dotnet test core/Dialoid.sln --filter FullyQualifiedName~StatEvaluatorTests`. Ожидание: FAIL (типы не определены).

- [ ] **Step 3: Реализация** в перечисленных файлах. `TagSet` основан на `ImmutableSortedSet<string>`, а `Equals` и `GetHashCode` считаются по элементам. Неизвестный `stat` в `Evaluate` бросает `KeyNotFoundException` с именем стата: это ошибка программиста, а не данных, данные ловит валидатор.

- [ ] **Step 4: Запуск тестов.** Ожидание: PASS.

- [ ] **Step 5: Commit:** `feat(stats): modifiers, tag filtering and stat evaluation with breakdown`

---

### Task 2: Модификаторы `per` и граф зависимостей статов

**Files:**
- Modify: `core/Dialoid.Core/Stats/StatEvaluator.cs`
- Create: `core/Dialoid.Core/Stats/StatGraph.cs`
- Test: `core/Dialoid.Core.Tests/Stats/PerStatTests.cs`

**Interfaces:**
- Produces: `public static class StatGraph { public static IReadOnlyList<string>? FindCycle(IReadOnlyList<Modifier> mods); }` возвращает путь цикла (`["a","b","a"]`) или `null`.

- [ ] **Step 1: Тесты:**

```csharp
[Fact] void Per_multiplies_value_by_other_stat_over_every()
// str = 133 (Flat), damage More 0.25 per {str, every 1} → множитель 1.3325
[Fact] void Per_respects_cap()
// bleed_stacks = 20, attack_speed Increased 1 per {bleed_stacks,1,cap 15} → +15%
[Fact] void Per_uses_same_tag_context()
[Fact] void Cycle_is_detected()
// a per b, b per a → FindCycle != null; Evaluate бросает InvalidOperationException с текстом цикла
[Fact] void Breakdown_line_shows_resolved_per_value()
```

- [ ] **Step 2: Запуск, ожидание FAIL.**
- [ ] **Step 3: Реализация.** `Evaluate` рекурсивно вычисляет `per.Stat` в том же контексте и с теми же условиями. Защита от циклов: стек вычисляемых статов и мемоизация в пределах одного вызова `Evaluate`. Формула `(stat / Every)`, затем `min(…, Cap)`.
- [ ] **Step 4: Запуск, ожидание PASS.**
- [ ] **Step 5: Commit:** `feat(stats): per-stat modifiers with cycle detection`

---

### Task 3: Загрузка данных, ошибки данных, каталоги статов, тегов и недугов

**Files:**
- Create: `core/Dialoid.Core/Data/{GameData,DataError,DataLoader}.cs`, `core/Dialoid.Core/Data/Json/{StatJson,TagJson,AilmentJson,ModifierJson}.cs`
- Create: `data/stats.json`, `data/tags.json`, `data/ailments.json`
- Create: `core/Dialoid.Core.Tests/Fixtures/tiny/` (`stats.json`, `tags.json`, `ailments.json`), `core/Dialoid.Core.Tests/Fixtures/FixturePaths.cs`
- Test: `core/Dialoid.Core.Tests/Data/DataLoaderTests.cs`

**Interfaces:**
- Produces:
```csharp
namespace Dialoid.Core.Data;
public sealed record DataError(string File, string? Id, string Message);   // Message на русском
public sealed record LoadResult(GameData? Data, IReadOnlyList<DataError> Errors) { bool Ok => Data != null && Errors.Count == 0; }
public static class DataLoader { public static LoadResult Load(string dataDirectory); }
public sealed class GameData   // неизменяемая; поля будут добавляться в задачах 4–8
{ IReadOnlyDictionary<string, StatDefinition> Stats; IReadOnlySet<string> Tags; IReadOnlyDictionary<string, AilmentDefinition> Ailments; ... }
public sealed record AilmentDefinition(string Id, DamageType SourceType, string ChanceStat, double DamageFraction,
    double DurationSec, string MaxStacksStat, StackRule StackRule, string Tag);   // StackRule: ReplaceWeakest | StrongestOnly | Unlimited
```
`DamageType` (задача 9) нужен уже здесь: объявить `Combat/DamageType.cs` в этой задаче: `enum DamageType { Physical = 0, Lightning = 1, Cold = 2, Fire = 3, Poison = 4 }`. Порядок значений совпадает с порядком конверсии.

- [ ] **Step 1: Тесты:**

```csharp
[Fact] void Loads_tiny_fixture_without_errors()
[Fact] void Missing_file_reports_error_with_file_name()
[Fact] void Malformed_json_reports_error_not_exception()
[Fact] void Duplicate_id_reports_error_with_id()
[Fact] void Ailments_table_matches_spec_defaults()
// bleed 0.70/5s/bleed_max_stacks; ignite 0.90/4s; poison 0.30/3s, Unlimited; shock StrongestOnly; freeze 1s; chill 2s
```

- [ ] **Step 2: Запуск, ожидание FAIL.**
- [ ] **Step 3: Реализация.** Один JSON-файл соответствует одному каталогу (массив объектов). `ModifierJson` читает краткую форму `{"stat":"damage","kind":"increased","value":8,"tags":["fire"],"per":{...},"conditions":[...]}`. `data/stats.json` содержит все статы, которые называет спека: `life`, `life_regen_pct`, `armor`, `dodge_chance`, `resist_{fire,cold,lightning,poison}` и `resist_*_max`, `damage`, `added_{type}_damage_{min,max}`, `crit_chance`, `crit_damage` (Default 50), `attack_speed`, `cast_speed`, `area`, `projectile_count`, `pierce_count`, `chain_count`, `duration`, `cooldown_recovery`, `resource_max`, `resource_regen`, `resource_gen`, `resource_cost`, `penetration_{type}`, `{bleed,ignite,poison,shock,freeze}_chance`, `{bleed,ignite,poison}_max_stacks` (8/1/1e9), `shock_effect` (Default 10, Max 40), `freeze_duration`, `damage_taken`, `str`, `int`, `will`, `dex`. `data/tags.json` содержит все теги §4.4 и `ailment_*`. Префикс `skill:` разрешен динамически и в каталоге не лежит.
- [ ] **Step 4: Запуск, ожидание PASS.**
- [ ] **Step 5: Commit:** `feat(data): JSON loader with Russian data errors; stat/tag/ailment catalogs`

---

### Task 4: Эффекты, область действия, реестр механик

**Files:**
- Create: `core/Dialoid.Core/Effects/{Effect,Scope,IMechanic,MechanicRegistry}.cs`, `core/Dialoid.Core/Data/Json/EffectJsonConverter.cs`
- Test: `core/Dialoid.Core.Tests/Effects/EffectParsingTests.cs`

**Interfaces:**
- Produces:
```csharp
namespace Dialoid.Core.Effects;
public abstract record Scope { sealed record Global; sealed record Skill(string SkillId); sealed record Tags(TagSet Required); }
public abstract record Effect(Scope Scope, bool PerPoint);
public sealed record ModifierEffect(Modifier Modifier, Scope Scope, bool PerPoint = true) : Effect(Scope, PerPoint);
public sealed record AddTagEffect(string Tag, Scope Scope) ...; public sealed record RemoveTagEffect(string Tag, Scope Scope) ...;
public sealed record SetBehaviorEffect(string Param, double Value, Scope Scope) ...; public sealed record AddBehaviorEffect(string Param, double Delta, Scope Scope, bool PerPoint = true) ...;
public sealed record AddConversionEffect(DamageType From, DamageType To, double Percent, Scope Scope, bool PerPoint = true) ...;
public sealed record SetDamageTypeEffect(DamageType To, Scope Scope) ...;
public sealed record SetResourceEffect(ResourceField Field, double Value, Scope Scope) ...;   // ResourceField: Cost | Gain
public sealed record GrantProcEffect(Trigger Trigger, ProcAction Action, Scope Scope) ...;
public sealed record MechanicEffect(string MechanicId, IReadOnlyDictionary<string, JsonElement> Params, Scope Scope) ...;
public abstract record Trigger { OnUse; OnHit; OnCrit; OnKill; EveryNUses(int N); EveryNHits(int N); }
public abstract record ProcAction { ApplyBuff(string BuffId, double DurationSec, IReadOnlyList<Modifier> Mods); ApplyAilment(string AilmentId);
                                    CastSkill(string SkillId, double DamagePercent); }
public interface IMechanic { string Id { get; } /* хуки задаются в задаче 15 */ }
public sealed class MechanicRegistry { void Register(IMechanic m); bool Contains(string id); IMechanic Get(string id); static MechanicRegistry Default { get; } }
```
JSON эффекта: `{"type":"modifier"|"add_tag"|...|"mechanic", "scope":"global"|"skill:<id>"|{"tags":[...]}, ...}`. По умолчанию scope = `skill:<owner>` для узлов скилла и `global` для остальных (решает загрузчик по месту узла).

- [ ] **Step 1: Тесты** (по одному на каждый тип эффекта, круговой разбор JSON → record):

```csharp
[Theory] [InlineData("modifier")] ... [InlineData("mechanic")] void Every_effect_type_parses(string type)
[Fact] void Unknown_effect_type_reports_data_error()
[Fact] void Skill_node_default_scope_is_owner_skill()
[Fact] void Unknown_mechanic_id_reports_data_error()   // проверка против MechanicRegistry
```
- [ ] **Step 2: FAIL. Step 3: Реализация. Step 4: PASS.**
- [ ] **Step 5: Commit:** `feat(effects): unified effect model with scopes, procs and mechanic registry`

---

### Task 5: Классы, атрибуты, базовый компилятор персонажа

**Files:**
- Create: `core/Dialoid.Core/Character/{Attribute,ClassDefinition,Build,CompiledCharacter,CharacterCompiler}.cs`, `core/Dialoid.Core/Data/Json/ClassJson.cs`
- Fixture: `Fixtures/tiny/classes.json` (`test_warrior`: основной `str`, база str 15 / dex 10 / will 10 / int 7, ресурс fury; `test_mage`: основной `int`, база int 15 / will 10 / dex 8 / str 7, ресурс mana)
- Test: `core/Dialoid.Core.Tests/Character/AttributeTests.cs`

**Interfaces:**
- Produces:
```csharp
namespace Dialoid.Core.Character;
public enum Attribute { Str, Int, Will, Dex }   // стат-id: "str","int","will","dex"
public sealed record ResourceDefinition(string Id, string Name, double BaseMax, double RegenPerSec, double DecayPerSec,
    double DecayDelaySec, PerStat? MaxPer);       // мана: BaseMax 100, MaxPer {int, every 2}
public sealed record ClassDefinition(string Id, string Name, Attribute Primary, IReadOnlyDictionary<Attribute,int> BaseAttributes,
    double LifeMultiplier, ResourceDefinition Resource, IReadOnlyList<Modifier> ClassBonuses, string PassiveStartNodeId,
    IReadOnlyList<string> SkillIds, IReadOnlyList<string> AscendancyIds);
public sealed class Build   // изменяемый DTO, сериализуется в задаче 17
{ public string ClassId = ""; public int Level = 1; public string? AscendancyId; public List<string> AscendancyNodes = new();
  public List<string> PassiveNodes = new(); public Dictionary<string, Attribute> AttributeChoices = new();
  public List<SkillSlot> Slots = new(); public string? WeaponId; public List<string> ItemIds = new(); public List<string> Rotation = new(); }
public sealed class SkillSlot { public string SkillId = ""; public int Level = 1; public Dictionary<string,int> NodePoints = new(); }
public sealed record CompiledCharacter(Build Build, ClassDefinition Class, StatEvaluator Evaluator,
    IReadOnlyList<Modifier> GlobalModifiers, IReadOnlyList<CompiledSkill> Skills, IReadOnlyList<MechanicEffect> Mechanics);
public sealed class CharacterCompiler { public CharacterCompiler(GameData data); public CompiledCharacter Compile(Build build); }
```
`CompiledSkill` в этой задаче — пустая заглушка, ее наполняет задача 8.

- [ ] **Step 1: Тесты** (значения из §5.1, §5.2, §5.4, §10):

```csharp
[Fact] void Attributes_grow_plus_one_all_and_plus_one_primary_per_level()
// warrior L60: str 15+59+59=133, dex 69, will 69, int 66
[Fact] void Primary_attribute_gives_more_damage_025_per_point()
// warrior L60: damage More из источника attribute = 33.25
[Fact] void General_attribute_bonuses()
// str 69 → armor +138 (2/очко); int 66 → resist_fire +6.6; will 69 → resource_gen Increased +13.8 и life_regen_pct Increased +13.8; dex 69 → dodge_chance +3.45
[Fact] void Life_formula_with_class_multiplier()
// L60 base 100+11×60=760; warrior ×1.2 = 912; mage 760
[Fact] void Mana_max_grows_from_intelligence()
// mage L60 int 133 → 100 + 66.5 = 166.5
[Fact] void Class_bonuses_apply()
// warrior: will → +0.2% resource_gen; dex → +0.04 п.п. crit_chance с тегом attack
[Fact] void Life_regen_base_is_half_percent()
```
- [ ] **Step 2: FAIL. Step 3: Реализация.** Атрибуты и бонусы атрибутов — обычные модификаторы с источником `attribute`, `class` или `level`, `per` по стату атрибута. «Основной атрибут дает More урона» — это модификатор `damage More 0.25 per {primary, 1}` с источником `attribute`. **Step 4: PASS.**
- [ ] **Step 5: Commit:** `feat(character): classes, D4 attributes and base character compilation`

---

### Task 6: Пассивное дерево и асценданты: граф, правила, валидатор

**Files:**
- Create: `core/Dialoid.Core/Passives/{PassiveNode,PassiveTree,Ascendancy}.cs`, `core/Dialoid.Core/Data/Json/{PassiveJson,AscendancyJson}.cs`, `core/Dialoid.Core/Data/Validator.cs`
- Fixture: `Fixtures/tiny/passives.json` (8 узлов: 2 старта, 3 малых, 1 атрибутный, 1 notable, 1 keystone `test_keystone`), `Fixtures/tiny/ascendancies.json` (`test_asc` для test_warrior: старт и 3 notable по 2 очка)
- Fixture: `Fixtures/broken/` (по каталогу на случай: `disconnected_tree/`, `two_starts_one_class/`, `unknown_ref/`)
- Test: `core/Dialoid.Core.Tests/Passives/PassiveTreeTests.cs`, `core/Dialoid.Core.Tests/Data/ValidatorTests.cs`

**Interfaces:**
- Produces:
```csharp
namespace Dialoid.Core.Passives;
public enum PassiveKind { Start, Small, Attribute, Notable, Keystone }
public sealed record PassiveNode(string Id, string Name, string Description, PassiveKind Kind, string Region, double X, double Y,
    IReadOnlyList<Effect> Effects, string? StartForClassId);
public sealed class PassiveTree
{ IReadOnlyDictionary<string, PassiveNode> Nodes; IReadOnlyDictionary<string, IReadOnlyList<string>> Edges;
  bool IsConnectedToStart(IEnumerable<string> allocated, string startId);   // все взятые связаны со стартом
  bool CanAllocate(IReadOnlySet<string> allocated, string nodeId); bool CanDeallocate(IReadOnlySet<string> allocated, string nodeId, string startId); }
public sealed record AscendancyNode(string Id, string Name, string Description, int Cost, IReadOnlyList<Effect> Effects);
public sealed record Ascendancy(string Id, string Name, string ClassId, AscendancyNode Start, IReadOnlyList<AscendancyNode> Notables);
namespace Dialoid.Core.Data;
public static class Validator { public static IReadOnlyList<DataError> Validate(GameData data, MechanicRegistry mechanics); }
```
Валидатор вызывается внутри `DataLoader.Load`, его ошибки попадают в `LoadResult.Errors`.

- [ ] **Step 1: Тесты:**

```csharp
[Fact] void Passive_points_are_level_minus_one()   // L60 → 59; L1 → 0
[Fact] void Can_allocate_only_adjacent_to_allocated()
[Fact] void Cannot_deallocate_node_that_disconnects_others()
[Fact] void Attribute_node_requires_choice_and_gives_plus_five()
[Fact] void Ascendancy_unlocks_at_20_with_points_2_2_2_2()   // L19 → 0; L20 → 2; L35 → 4; L50 → 8
[Fact] void Ascendancy_notables_cost_two()                     // 8 очков → не больше 4 notable
[Fact] void Validator_reports_disconnected_tree()
[Fact] void Validator_reports_two_starts_for_one_class()
[Fact] void Validator_reports_unknown_reference_with_file_and_id()
[Fact] void Validator_reports_per_stat_cycle()                 // через StatGraph из задачи 2
```
- [ ] **Step 2: FAIL. Step 3: Реализация.** `CharacterCompiler` добавляет эффекты взятых узлов и асцендантов в глобальные модификаторы (scope `global`) или в отложенный список для скиллов (scope `skill` или `tags`, их применяет задача 8). **Step 4: PASS.**
- [ ] **Step 5: Commit:** `feat(passives): shared passive tree, ascendancies and data validator`

---

### Task 7: Скиллы: определения, деревья, слоты, лут, правила билда

**Files:**
- Create: `core/Dialoid.Core/Skills/{SkillDefinition,SkillTree}.cs`, `core/Dialoid.Core/Data/Json/{SkillJson,WeaponJson,ItemJson}.cs`, `core/Dialoid.Core/Character/BuildRules.cs`
- Fixture: `Fixtures/tiny/skills.json`:
  - `test_slash`: атака, melee, physical, generator; weaponEffectiveness 110, gain 12, bleed 10%. Дерево на 4 узла, с exclusiveGroup и узлом с maxPoints 3.
  - `test_slam`: атака, area, spender; cost 35.
  - `test_bolt`: заклинание, projectile, fire; база 14–20, cast 0.75, cost 12.
  - `test_spark`: generator; gain 3, chain 2.
  - `test_cloud`: `classId: null`, poison dot.
- Fixture: `weapons.json` (`test_sword` one_hand 10–15, 1.4 aps, crit 5; `test_staff` staff 8–12, имплицит cast_speed +10), `items.json` (`test_cloud_ring`: grantsSkill test_cloud, skillLevelBonus 2)
- Test: `core/Dialoid.Core.Tests/Skills/SkillTreeTests.cs`, `core/Dialoid.Core.Tests/Character/BuildRulesTests.cs`

**Interfaces:**
- Produces:
```csharp
namespace Dialoid.Core.Skills;
public sealed record SkillBase(IReadOnlyDictionary<DamageType,(double Min,double Max)> SpellDamage, double? WeaponEffectiveness,
    double AddedDamageEffectiveness, double CastTime, double AttackTimeMultiplier, double? Cooldown, double ResourceCost,
    double ResourceGain, double CritChance, int HitsPerUse, IReadOnlyDictionary<string,double> Behavior,
    IReadOnlyDictionary<string,double> AilmentChances, IReadOnlyList<string> WeaponTypes);
public sealed record SkillTreeNode(string Id, string Name, string Description, int MaxPoints,
    IReadOnlyList<(string NodeId,int Points)> Requires, string? ExclusiveGroup, IReadOnlyList<Effect> Effects);
public sealed record SkillDefinition(string Id, string Name, string Description, string? ClassId, int UnlockLevel, TagSet Tags,
    SkillBase Base, IReadOnlyList<SkillTreeNode> Tree, bool IsTriggeredOnly);
public sealed record WeaponStub(string Id, string Name, string Type, double PhysMin, double PhysMax, double AttacksPerSecond,
    double CritChance, IReadOnlyList<Modifier> Implicits);
public sealed record ItemStub(string Id, string Name, string GrantsSkill, int SkillLevelBonus);
namespace Dialoid.Core.Character;
public sealed record RuleViolation(string Code, string Message);   // Message на русском; Code: "too_many_passive_points" и т.п.
public static class BuildRules
{ public static IReadOnlyList<RuleViolation> Validate(Build build, GameData data);
  public static int SlotsUnlocked(int level);                    // 1→1, 4→2, 8→3, 14→4, 20→5
  public static int SkillLevelCap(Build build, string skillId, GameData data); }  // родной 20; из лута 10 + бонус предмета, не больше 14
namespace Dialoid.Core.Skills;
public static class SkillProgression { public static int LevelForXp(double xp, IReadOnlyList<double> thresholds, int cap); }
// таблица опыта лежит в data/progression.json (§5.3): пороги уровней персонажа и скилла; в песочнице уровень задается вручную
```

- [ ] **Step 1: Тесты:**

```csharp
[Fact] void Skill_tree_points_equal_skill_level()               // уровень 7 → 7 очков; потратить 8 → нарушение
[Fact] void Node_requires_points_in_parent()
[Fact] void Exclusive_group_allows_only_one()
[Fact] void Slots_unlock_at_1_4_8_14_20()
[Fact] void Native_skill_cap_20_loot_cap_10_item_bonus_up_to_14()
[Fact] void Loot_skill_requires_item_equipped()
[Fact] void Other_class_skill_without_item_is_violation()
[Fact] void Attack_skill_requires_matching_weapon_type()
[Fact] void Skill_locked_by_unlock_level_is_violation()
[Fact] void Validator_reports_skill_tree_requirement_cycle()
[Fact] void Weapon_implicits_become_global_modifiers()          // test_staff → cast_speed +10, источник weapon
[Fact] void Skill_level_from_xp_table()                         // SkillProgression.LevelForXp(xp, table): 0 → 1; ровно порог 2-го → 2; огромный опыт → кап
[Fact] void Illegal_build_after_level_decrease_reports_all_violations()   // Review Focus 1
// L60 билд (59 пассивок, 5 слотов, асцендант 8) → Level=10: нарушения too_many_passive_points,
// slot_locked, ascendancy_locked, skill_level_above_cap, исключений нет
```
- [ ] **Step 2: FAIL. Step 3: Реализация. Step 4: PASS.**
- [ ] **Step 5: Commit:** `feat(skills): skill definitions, LE-style trees, slots, loot skills and build rules`

---

### Task 8: Компиляция скилла: порядок эффектов, теги, смена стихии, конфликты

**Files:**
- Create: `core/Dialoid.Core/Skills/{CompiledSkill,SkillCompiler}.cs`
- Modify: `core/Dialoid.Core/Character/CharacterCompiler.cs` (наполняет `Skills`)
- Test: `core/Dialoid.Core.Tests/Skills/SkillCompilerTests.cs`

**Interfaces:**
- Consumes: `SkillDefinition`, `Effect` (задачи 4 и 7), отложенные эффекты пассивок, асцендантов и keystones (задача 6).
- Produces:
```csharp
namespace Dialoid.Core.Skills;
public sealed record CompiledSkill(string SkillId, int Level, bool IsLoot, TagSet Tags, DamageType? DamageTypeOverride,
    SkillBase Base, IReadOnlyDictionary<string,double> Behavior, IReadOnlyList<Conversion> Conversions,
    double ResourceCost, double ResourceGain, IReadOnlyList<Modifier> Modifiers /* scope skill и tags, уже с тегом skill:<id> */,
    IReadOnlyList<GrantProcEffect> Procs, IReadOnlyList<MechanicEffect> Mechanics,
    IReadOnlyDictionary<string,int> EffectiveNodePoints /* после grant_skill_node */);
public sealed record Conversion(DamageType From, DamageType To, double Percent, SourceRef Source);  // создать здесь: core/Dialoid.Core/Combat/Conversion.cs
public static class SkillCompiler { public static CompiledSkill Compile(SkillSlot slot, SkillDefinition def, CompilationContext ctx); }
public sealed record CompilationContext(GameData Data, IReadOnlyList<Effect> AscendancyEffects, IReadOnlyList<Effect> KeystoneEffects,
    IReadOnlyList<Effect> PassiveScopedEffects, IReadOnlyList<(string SkillId,string NodeId)> GrantedNodes);
```

- [ ] **Step 1: Тесты:**

```csharp
[Fact] void Effects_apply_in_order_tree_then_ascendancy_then_keystone()
// SetDamageType(cold) из дерева, SetDamageType(lightning) из асцендантa → итог Lightning
[Fact] void SetDamageType_replaces_base_type_and_tag()
// test_bolt fire→cold: Tags содержит cold, не содержит fire; DamageTypeOverride = Cold
[Fact] void PerPoint_effects_scale_with_points()      // +8% × 3 очка = +24%
[Fact] void Tag_scoped_passive_reaches_matching_skills_only()
[Fact] void Granted_node_takes_exclusive_group_and_refunds_other()
// в группе B выбран node_b2; асцендант выдает node_b1 → EffectiveNodePoints: b1=1, b2 отсутствует; свободных очков +1
[Fact] void RemoveTag_area_removes_area_behaviour()
[Fact] void Every_skill_has_skill_id_tag()
```
- [ ] **Step 2: FAIL. Step 3: Реализация. Step 4: PASS.**
- [ ] **Step 5: Commit:** `feat(skills): skill compilation with effect ordering and conflict rules`

---

### Task 9: Базовый урон, рост, конверсия

**Files:**
- Create: `core/Dialoid.Core/Combat/{DamagePortion,Growth,BaseDamage,ConversionResolver}.cs`
- Test: `core/Dialoid.Core.Tests/Combat/BaseDamageTests.cs`, `core/Dialoid.Core.Tests/Combat/ConversionTests.cs`

**Interfaces:**
- Produces:
```csharp
namespace Dialoid.Core.Combat;
public sealed record DamagePortion(DamageType Type, double Min, double Max, IReadOnlySet<DamageType> Lineage);   // Lineage включает Type
public static class Growth { public static double Skill(int level); public static double Gear(int characterLevel); }
public static class BaseDamage
{ public static IReadOnlyList<DamagePortion> Compute(CompiledSkill skill, int characterLevel, WeaponStub? weapon, StatEvaluator ev, TagSet ctx); }
public static class ConversionResolver
{ public static IReadOnlyList<DamagePortion> Apply(IReadOnlyList<DamagePortion> portions, IReadOnlyList<Conversion> conversions); }
```

- [ ] **Step 1: Тесты** (§6.1, §6.3):

```csharp
[Fact] void Skill_growth_is_linear_5_percent()        // L1 1.0; L10 1.45; L20 1.95
[Fact] void Gear_growth_is_linear_6_percent()         // L1 1.0; L60 4.54
[Fact] void Spell_base_damage()                       // 14–20 огонь, skill L20, char L60 → 14×1.95×4.54 … 20×1.95×4.54
[Fact] void Attack_base_damage_from_weapon()          // меч 10–15, eff 110%, skill L1, char L1 → 11–16.5 физ
[Fact] void Added_flat_damage_scaled_by_added_effectiveness()   // +10 огня, eff 120% → +12
[Fact] void DamageTypeOverride_converts_base_before_conversion()
[Fact] void Conversion_moves_damage_rightwards()      // 60% физ → огонь: физ 40%, огонь 60%, Lineage огня {Physical, Fire}
[Fact] void Conversion_over_100_scales_proportionally()   // физ→огонь 60 + физ→молния 60 → каждая по 50%
[Fact] void Conversion_chain_keeps_full_lineage()     // физ→молния 100, молния→огонь 50 → огонь Lineage {Physical, Lightning, Fire}
[Fact] void Leftward_conversion_is_rejected_by_validator()   // огонь→холод в данных → DataError
```
- [ ] **Step 2: FAIL. Step 3: Реализация.** Конверсия идет по типам в порядке enum: для каждого исходного типа складываются все конверсии из него, при сумме больше 100 они масштабируются до 100, затем порции переносятся. **Step 4: PASS.**
- [ ] **Step 5: Commit:** `feat(combat): base damage, growth curves and rightward conversion`

---

### Task 10: Пайплайн удара: модификаторы, крит, защита, получаемый урон

**Files:**
- Create: `core/Dialoid.Core/Combat/{Mitigation,HitCalculator,HitResult}.cs`
- Test: `core/Dialoid.Core.Tests/Combat/HitPipelineTests.cs`, `core/Dialoid.Core.Tests/Combat/MitigationTests.cs`

**Interfaces:**
- Produces:
```csharp
namespace Dialoid.Core.Combat;
public sealed record TargetDefense(double Armor, IReadOnlyDictionary<DamageType,double> Resists, double DamageTakenIncreased);
public static class Mitigation
{ public static double ArmorReduction(double armor, double hitDamage);      // armor/(armor+10×hit), кап 0.85
  public static double ResistMultiplier(double resist, double penetration); } // eff = max(-100, resist−pen); 1 − eff/100
public enum CritOutcome { Roll, Expected }
public sealed record HitInput(CompiledCharacter Character, CompiledSkill Skill, TargetDefense Target,
    IConditionContext Conditions, bool? IsCrit /* null = матожидание */, Func<double,double,double> PickInRange);
public sealed record HitResult(IReadOnlyDictionary<DamageType,double> Final, IReadOnlyDictionary<DamageType,double> AilmentBase,
    bool IsCrit, double CritMultiplierApplied, IReadOnlyList<StatResult> Breakdown);
public static class HitCalculator { public static HitResult Resolve(HitInput input); }
```
`AilmentBase` берется после шагов 1, 2 и 4 и до шагов 3, 5 и 6 (§6.1 п.7).

- [ ] **Step 1: Тесты** (каждый шаг §6.1 отдельно и крайние значения):

```csharp
[Fact] void Portion_gets_modifiers_of_every_lineage_type()   // физ→огонь получает «+% физ» и «+% огня»
[Fact] void Primary_attribute_more_applies_to_hits()
[Fact] void Crit_chance_uses_general_formula_and_caps_at_100()   // (5+2)×(1+1.0) = 14; Flat 80, Inc 100 → 100
[Fact] void Crit_damage_is_flat_points()                   // 50 + 15 → ×1.65
[Fact] void Expected_crit_multiplier()                     // шанс 20%, крит 50 → 1.10
[Fact] void Armor_formula_and_cap()                        // 1000 брони, удар 100 → 0.5; 1e6 брони → 0.85
[Fact] void Big_hits_penetrate_armor_better()              // 1000 брони: удар 100 → 50%, удар 1000 → 9.1%
[Fact] void Resist_minus_penetration_floor_minus_100()     // сопр 30, пен 200 → eff −100 → ×2
[Fact] void Damage_taken_bucket_is_additive()              // шок 20 + 15 → ×1.35
[Fact] void Ailment_base_excludes_mods_and_defense()       // база 100, +100% урона, сопр 40 → AilmentBase 100, Final 120
[Fact] void Ailment_base_includes_crit()                   // крит ×1.5 → AilmentBase 150
[Fact] void More_minus_100_zeroes_damage()                 // Review Focus 5
[Fact] void Breakdown_contains_damage_and_crit_stats()
```
- [ ] **Step 2: FAIL. Step 3: Реализация. Step 4: PASS.**
- [ ] **Step 5: Commit:** `feat(combat): hit pipeline with crit, armor, resist, penetration and ailment base`

---

### Task 11: Источники случайности и модель покрытия

**Files:**
- Create: `core/Dialoid.Core/Simulation/RandomSource.cs`, `core/Dialoid.Core/Combat/Coverage.cs`
- Test: `core/Dialoid.Core.Tests/Simulation/RandomSourceTests.cs`, `core/Dialoid.Core.Tests/Combat/CoverageTests.cs`

**Interfaces:**
- Produces:
```csharp
namespace Dialoid.Core.Simulation;
public enum SimMode { Roll, Average }
public interface IRandomSource
{ double InRange(double min, double max);          // Roll: равномерно; Average: середина
  bool Chance(string channel, double probability);  // Roll: RNG; Average: накопитель по каналу (§6.8)
  bool? CritOrExpected(string channel, double chance); } // Roll: true/false; Average: null → HitCalculator берет матожидание
public static class RandomSources { public static IRandomSource Create(SimMode mode, int seed); }
namespace Dialoid.Core.Combat;
public static class Coverage
{ public static double AreaTargets(int pack, double radius, double packRadius = 4.0);   // 1 + (pack−1)·min(1,(r/R)²)
  public static double DistinctTargets(int pack, int projectiles, int pierce, int chain, double? explosionRadius, double packRadius = 4.0); }
```
Канал накопителя — строка, например `"ignite:test_bolt"` или `"crit:test_bolt"`. У каждого канала свой счетчик.

- [ ] **Step 1: Тесты:**

```csharp
[Fact] void Average_accumulator_fires_every_fourth_at_25_percent()   // 8 вызовов → true на 4-м и 8-м
[Fact] void Average_channels_are_independent()
[Fact] void Roll_is_deterministic_for_same_seed()
[Fact] void Average_returns_midpoint_and_null_crit()
[Fact] void Area_coverage_examples()          // pack 5: r 1.5 → 1.5625; r 2.5 → 2.5625; r 10 → 5; pack 1 → 1
[Fact] void Projectiles_no_shotgun()          // pack 5, 3 проектиля без взрыва → 3; pack 1 → 1
[Fact] void Projectiles_with_explosion()      // pack 5, 3 проектиля, взрыв 1.5 → min(5, 3×1.5625) = 4.6875
[Fact] void Pierce_and_chain_add_targets_capped_by_pack()
```
- [ ] **Step 2: FAIL. Step 3: Реализация. Step 4: PASS.**
- [ ] **Step 5: Commit:** `feat(sim): roll/average random sources and coverage model`

---

### Task 12: Недуги и зоны урона по времени

**Files:**
- Create: `core/Dialoid.Core/Combat/Ailments.cs`, `core/Dialoid.Core/Simulation/TargetState.cs`
- Test: `core/Dialoid.Core.Tests/Combat/AilmentTests.cs`

**Interfaces:**
- Produces:
```csharp
namespace Dialoid.Core.Simulation;
public sealed class TargetState
{ public double Life, MaxLife; public bool Immortal; public TargetDefense Defense;
  public IReadOnlyList<AilmentInstance> Ailments { get; }
  public double FreezeRemaining, FreezeImmunityRemaining;
  public void Apply(AilmentInstance a, int maxStacks, StackRule rule);
  public double Tick(double dt);          // возвращает урон DoT за шаг; продвигает таймеры, снимает истекшие
  public bool HasAilment(string ailmentId); }
public sealed record AilmentInstance(string AilmentId, DamageType Type, double DamagePerSec, double Remaining, string SourceSkillId);
namespace Dialoid.Core.Combat;
public static class AilmentCalculator
{ public static double DotDps(double ailmentBase, AilmentDefinition def, CompiledCharacter ch, CompiledSkill skill, TargetDefense t);
  // база × доля / длительность → модификаторы с тегами {dot, тип, skill:<id>, ailment_x} (без тегов доставки) → защита (без брони у bleed) → damage_taken
  public static double GroundZoneDps(double ailmentBase, double fraction, CompiledCharacter ch, CompiledSkill skill, TargetDefense t); }  // с тегами скилла + dot
```

- [ ] **Step 1: Тесты** (§6.7):

```csharp
[Fact] void Ignite_dps_is_90_percent_over_4_seconds()                // база 100 → 22.5/с без модификаторов
[Fact] void Ailment_ignores_delivery_tag_modifiers()                 // «+50% урона заклинаний» не влияет; «+50% огня» влияет
[Fact] void Ailment_gets_skill_tree_modifiers_and_primary_more()
[Fact] void Bleed_ignores_armor_but_not_resist()
[Fact] void Ignite_replaces_weakest_when_at_max_stacks()             // max 3: 10,20,30 + 25 → 20,25,30; + 5 → без изменений
[Fact] void Bleed_stacks_to_8_poison_unlimited()
[Fact] void Shock_strongest_only_and_capped_at_40()
[Fact] void Freeze_then_three_second_immunity()
[Fact] void Boss_freeze_duration_halved()                             // множитель freeze из манекена 0.5
[Fact] void Ground_zone_keeps_skill_tags_and_uses_ailment_base()
[Fact] void Chill_always_applied_on_cold_damage()
```
- [ ] **Step 2: FAIL. Step 3: Реализация. Step 4: PASS.**
- [ ] **Step 5: Commit:** `feat(combat): ailments, stacking rules, freeze DR and ground zones`

---

### Task 13: Симулятор: время, ротация, ресурс, цели, результаты

**Files:**
- Create: `core/Dialoid.Core/Simulation/{SimConfig,SimResult,Simulator,Rotation,DummyDefinition}.cs`, `core/Dialoid.Core/Data/Json/DummyJson.cs`
- Fixture: `Fixtures/tiny/dummies.json` (`test_training` бессмертный 0 защиты; `test_swarm` 5 целей, здоровье 100, respawn; `test_boss` здоровье 1000, броня 1000, без respawn; `test_attacker` бессмертный, атака 14/9 раз в 3.5 с, freezeDurationMultiplier 0.5)
- Test: `core/Dialoid.Core.Tests/Simulation/SimulatorTests.cs`

**Interfaces:**
- Produces:
```csharp
namespace Dialoid.Core.Simulation;
public sealed record DummyDefinition(string Id, string Name, int PackSize, double PackRadius, double LifePerTarget /* ×G, +∞ = бессмертный */,
    bool Respawn, double Armor, IReadOnlyDictionary<DamageType,double> Resists, double FreezeDurationMultiplier, DummyAttack? Attack);
public sealed record DummyAttack(IReadOnlyDictionary<DamageType,double> DamagePerHit /* ×G */, double IntervalSec);
public sealed record SimConfig(string DummyId, SimMode Mode, int Seed = 1, double DurationSec = 60, double StepSec = 0.05,
    int? PackSizeOverride = null, bool DummyAttacks = false, double SurvivalCapSec = 180);
public sealed record SimEvent(double Time, string Kind, string Text);   // Kind: use|hit|crit|ailment|kill|no_resource|death|proc
public sealed record SimResult(double DpsSingleTarget, double DpsPack, IReadOnlyDictionary<string,double> DamageBySkill,
    IReadOnlyDictionary<DamageType,double> DamageByType, double HitDamage, double DotDamage,
    double? TimeToKill, double? TimeToDeath, IReadOnlyList<SimEvent> Log, IReadOnlyList<RuleViolation> Violations);
public sealed class Simulator
{ public Simulator(GameData data, MechanicRegistry mechanics);
  public SimResult Run(Build build, SimConfig config); }   // при нарушениях BuildRules: пустой результат с Violations, без исключения
```
Условия §4.5 вычисляет `internal sealed class SimConditionContext : IConditionContext` по состоянию симуляции: основная цель, ресурс, здоровье, время последнего крита, история применений, длительность текущего канала, активные баффы. Каталог `id` условий: `target_ignited`, `target_chilled`, `target_shocked`, `target_bleeding`, `target_poisoned`, `target_frozen`, `resource_above_pct`, `life_below_pct`, `recently_crit` (4 с), `recently_used_skill`, `channelling_for`, `buff_active`. Валидатор (задача 6) отклоняет неизвестный `id` условия.

Ротация (§11.3): каждый шаг, если персонаж свободен (каст или атака закончились), берется первый скилл из `Build.Rotation`, у которого готова перезарядка, хватает ресурса и есть подходящее оружие. Генератор уходит в конец очереди, когда спендерам не хватает ресурса. Время применения: заклинание — `CastTime / (1 + cast_speed%)`, атака — `AttackTimeMultiplier / (aps оружия × (1 + attack_speed%))`. Канал: применение каждые `interval` секунд, пока хватает ресурса. Ресурс: генерация за применение, если попало хоть по одной цели (§8.4), умножается на `resource_gen`. Регенерация в секунду. Ярость затухает −5/с после 3 с без попаданий.

- [ ] **Step 1: Тесты:**

```csharp
[Fact] void Average_mode_is_bit_identical_across_runs()
[Fact] void Single_spell_dps_matches_hand_calculation()
// test_mage L1, только test_bolt L1, тренировочный, без маны-ограничения (cost 0 в фикстуре-варианте):
// (14+20)/2 × ожидаемый крит (6%, ×1.5 → 1.03) × More Интеллекта (15 × 0.25% → 1.0375) / 0.75 с ≈ 24.22 DPS (±0.01)
[Fact] void Generator_used_when_spender_lacks_resource()
[Fact] void Mana_regen_limits_spender_rate()
[Fact] void Fury_decays_after_three_seconds_without_hits()
[Fact] void Resource_gain_counted_per_use_not_per_target()      // swarm 5 целей: +12, а не +60
[Fact] void Pack_dps_counts_coverage_targets()
[Fact] void Respawning_targets_trigger_kills_and_continue()
[Fact] void Boss_without_respawn_gives_time_to_kill()
[Fact] void Empty_or_non_damaging_rotation_returns_zero_without_hanging()   // Review Focus 2
[Fact] void Never_enough_resource_returns_zero_without_hanging()            // Review Focus 3, событие no_resource в логе
[Fact] void Illegal_build_returns_violations_without_exception()            // Review Focus 1
[Fact] void Damage_by_skill_and_type_sum_to_total()
[Theory] /* по одному случаю на каждый id условия */ void Each_condition_evaluates_from_sim_state(string conditionId)
[Fact] void Channel_condition_grows_with_channel_time()         // channelling_for(2) ложно на 1.0 с, истинно на 2.0 с
```
- [ ] **Step 2: FAIL. Step 3: Реализация. Step 4: PASS.**
- [ ] **Step 5: Commit:** `feat(sim): deterministic step simulator with rotation, resources and targets`

---

### Task 14: Проки и триггеры

**Files:**
- Create: `core/Dialoid.Core/Simulation/Procs.cs`
- Modify: `core/Dialoid.Core/Simulation/Simulator.cs`
- Test: `core/Dialoid.Core.Tests/Simulation/ProcTests.cs`

**Interfaces:**
- Consumes: `GrantProcEffect`, `Trigger`, `ProcAction` (задача 4).
- Produces: `internal sealed class ProcEngine { void OnUse(...); void OnHit(...); void OnKill(...); }`, который вызывает `Simulator`.

- [ ] **Step 1: Тесты** (§8.4):

```csharp
[Fact] void On_hit_and_on_crit_fire_at_most_once_per_use()     // применение по 5 целям → 1 срабатывание
[Fact] void Every_n_uses_counts_uses()                          // every_n_uses(4): 8 применений → 2 срабатывания
[Fact] void Every_n_hits_counts_hits()                          // every_n_hits(10): 2 применения × 5 целей → 1
[Fact] void Triggered_copy_uses_slotted_tree_and_level()
[Fact] void Triggered_copy_of_unslotted_skill_uses_source_level_no_tree()
[Fact] void Triggered_copy_costs_nothing_and_generates_nothing()
[Fact] void Triggered_copy_does_not_count_for_counters()
[Fact] void Triggered_skill_never_fires_procs()                 // цепочка A→B→A → B не запускает A
[Fact] void Validator_rejects_proc_on_triggered_only_skill()
[Fact] void On_crit_in_average_mode_uses_accumulator()          // шанс 25% → каждое 4-е применение
[Fact] void Buff_skill_applies_buff_on_use_and_expires()        // клич: on_use → ApplyBuff(+20% урона, 8 с), перезарядка 20 с; урон другого скилла ×1.2 первые 8 с
[Fact] void Rotation_casts_buff_only_when_not_active()
```
- [ ] **Step 2: FAIL. Step 3: Реализация. Step 4: PASS.**
- [ ] **Step 5: Commit:** `feat(sim): proc triggers with triggered-copy rules and loop protection`

---

### Task 15: Защитный режим: атаки манекена, смерть, метрика выживания

**Files:**
- Modify: `core/Dialoid.Core/Simulation/Simulator.cs`
- Test: `core/Dialoid.Core.Tests/Simulation/SurvivalTests.cs`

**Interfaces:**
- Produces: в `SimResult.TimeToDeath` секунды до смерти или `SurvivalCapSec`, если персонаж выжил (§11.3).

- [ ] **Step 1: Тесты:**

```csharp
[Fact] void Dummy_attack_mitigated_by_armor_resist_and_dodge()
// удар физ 14×G(1)=14 при броне 140: 140/(140+140)=50% → 7; огонь 9 при сопр 10% → 8.1
[Fact] void Dodge_uses_accumulator_in_average_mode()             // 25% → каждый 4-й удар
[Fact] void Life_regen_heals_per_second()                        // 0.5% от 912 = 4.56/с
[Fact] void Character_death_stops_sim_and_reports_time()
[Fact] void Survival_capped_at_180()
[Fact] void Frozen_dummy_skips_attacks()
[Fact] void Self_damage_never_below_one_life()
[Fact] void Life_payment_refused_when_life_not_above_cost()      // Неутолимый, откат на Ярость в задаче 16
[Fact] void Character_cannot_die_when_dummy_does_not_attack()
```
- [ ] **Step 2: FAIL. Step 3: Реализация. Step 4: PASS.**
- [ ] **Step 5: Commit:** `feat(sim): defensive mode, death and survival metric`

---

### Task 16: Каталог механик для keystones и асцендантов

**Files:**
- Modify: `core/Dialoid.Core/Effects/IMechanic.cs` (хуки)
- Create: `core/Dialoid.Core/Mechanics/{Keystones,BerserkerMechanics,ThunderMechanics,PyroMechanics,CryoMechanics}.cs`
- Test: `core/Dialoid.Core.Tests/Mechanics/*Tests.cs` (по файлу на группу)

**Interfaces:**
- Produces (хуки, которые вызывают компилятор и симулятор; механика реализует только нужные ей):
```csharp
public interface IMechanic
{ string Id { get; }
  void OnCompile(MechanicCompileContext ctx) {}       // меняет модификаторы, стоимость, перезарядки, стаки (Override и т.п.)
  void OnUse(MechanicSimContext ctx) {}               // заряды, оплата здоровьем
  void OnHit(MechanicSimContext ctx) {}
  void OnKill(MechanicSimContext ctx) {}
  void OnTick(MechanicSimContext ctx, double dt) {} }
```
Каталог `id` (каждый с параметрами в JSON и отдельным тестом с числами из спеки):

| id | Где в спеке | Ключевая проверка теста |
|---|---|---|
| `blood_pact` | §7.4 п.1 | ресурс 0, стоимость 12 → перезарядка 1.8 с; канал 2 с, затем перезарядка 3 с; здоровье ×0.7; регенерация 0 |
| `titan_will` | §7.4 п.2 | основной атрибут не дает More; Воля 69 → +34.5% More |
| `inevitability` | §7.4 п.3 | крит 0, on_crit не срабатывает; +18% More попаданий; шанс недугов ×0.7 |
| `avatar_of_fire` | §7.4 п.4 | молния и холод → огонь 100%, физ 50%; остаток физы 0; пен огня +15; ignite_max_stacks Override 1 (высший приоритет); resist_fire_max −15 |
| `blade_dance` | §7.4 п.5 | уклонение 0.15/Ловкость, кап 60; броня 0 |
| `stone_skin` | §7.4 п.6 | половина брони против стихий; Интеллект без сопротивлений; уклонение 0 |
| `berserk_frenzy` | §10.4 Берсерк старт | на 100 Ярости +20% More атак, +10% получаемого урона; Ярость не затухает |
| `insatiable` | Неутолимый | при 100 Ярости спендер платит 2% здоровья за 10 Ярости; если здоровья не хватает, платит Ярость |
| `reckless` | Без оглядки | сопротивления 0; +1% More за 1% недостающего здоровья, кап 40 |
| `whirl_rage` | Вихрь ярости | без затрат при Ярости > 50%; +10% скорости ударов в секунду канала, кап 50 |
| `flesh_rend` | Рассечение плоти | крит по кровоточащей → +1 стак |
| `battle_trance` | Боевой транс | пока действует клич: атаки +5 Ярости |
| `static_charge` | Статический заряд | заряд за применение с уроном молнией; на 10: +100% More следующему спендеру; триггерные копии зарядов не дают |
| `grounding` | Заземление | минимальный эффект шока 20, длительность 5 с |
| `spread_ignite` | Распространение | через 1 с поджог на всех целях пака |
| `ashes` | Пепел | +1% More за стак поджога в паке, кап 25 |
| `eternal_fire` | Вечный огонь | длительность поджога ×1.5 при той же скорости урона |
| `soul_heat` | Жар души | +1% маны за подожженную задетую цель, максимум 5% за применение |
| `fire_armor` | Огненная броня | при скилле с горящей землей: +30% брони, resist_fire_max +10 |
| `shatter` | Раскол | по замороженной цели: снять заморозку, +100% More этого удара |
| `ice_armor` | Ледяная броня | наложена заморозка → +200 брони на 4 с |
| `grant_skill_node` | Морозный шар | вызывает правило задачи 8 |
| `ailment_swap` | Морозный шар | шанс поджога → шанс заморозки; урон поджога → длительность заморозки (1% → 0.5%) |
| `brittle` | Хрупкость | +15% урона от критов по охлажденным |

Простые асцендантские эффекты без механики (Грозовое оружие, Цепной удар, Буря в жилах, Северное сияние, Вечная мерзлота, Метеоритный дождь, Ледник, Громовой топор, Разряд, Кровавый пир) выражаются через `ModifierEffect`, `AddConversionEffect`, `AddBehaviorEffect` и `SetDamageTypeEffect` в данных плана 2. В этой задаче для каждого пишется по одному тесту на фикстуре: он доказывает, что существующих эффектов хватает. Если не хватает, добавляется механика и строка в таблицу выше.

- [ ] **Step 1: Тесты** — по одному на строку таблицы, с числами из колонки «Ключевая проверка» (минимум 24 теста).
- [ ] **Step 2: FAIL. Step 3: Реализация** механик по одной, после каждой тесты. **Step 4: PASS** всего набора.
- [ ] **Step 5: Commit** по группе: `feat(mechanics): keystones`, `feat(mechanics): berserker`, `feat(mechanics): thunder`, `feat(mechanics): pyromancer`, `feat(mechanics): cryomancer`

---

### Task 17: Экспорт и импорт билда, тест «новое только данными»

**Files:**
- Create: `core/Dialoid.Core/Builds/BuildCodec.cs`
- Create: `core/Dialoid.Core.Tests/Fixtures/data_only/` (тестовый скилл и тестовый узел, собранные только из существующих эффектов)
- Test: `core/Dialoid.Core.Tests/Builds/BuildCodecTests.cs`, `core/Dialoid.Core.Tests/Data/DataOnlyExtensionTests.cs`

**Interfaces:**
- Produces:
```csharp
namespace Dialoid.Core.Builds;
public static class BuildCodec
{ public static string Export(Build build);                                  // "DLD1:" + base64url(gzip(json))
  public static bool TryImport(string code, GameData data, out Build? build, out string? error); }   // error на русском
```

- [ ] **Step 1: Тесты:**

```csharp
[Fact] void Export_import_roundtrip_preserves_build()
[Fact] void Import_rejects_garbage_with_reason()               // Review Focus 4: "abc", "DLD1:%%%", обрезанный код
[Fact] void Import_rejects_unknown_ids_with_reason()
[Fact] void Import_rejects_wrong_prefix_version()
[Fact] void New_skill_and_node_from_json_only_load_and_simulate()   // §15.7: data_only грузится без ошибок, DPS > 0
[Fact] void All_broken_fixtures_report_expected_errors()             // §15.2: цикл требований, несвязное дерево, неизвестный id, конверсия влево
```
- [ ] **Step 2: FAIL. Step 3: Реализация. Step 4: PASS.**
- [ ] **Step 5: README:** раздел «Ядро: как запустить тесты», статус «план 1 выполнен», ссылка на план 2.
- [ ] **Step 6: Полный прогон:** `dotnet test core/Dialoid.sln`. Ожидание: все тесты PASS, предупреждений нет.
- [ ] **Step 7: Commit:** `feat(builds): build export/import codec; data-only extension and validator acceptance tests`

---

## Покрытие спеки этим планом

| Раздел спеки | Задача |
|---|---|
| §3.1 слои, §3.3 данные и валидатор | 0, 3, 6 |
| §4.1–4.3 статы и формула, §4.7 разбор | 1 |
| §4.2 `per`, граф статов | 2 |
| §4.4 теги, §4.5 условия | 1, 3, 13 |
| §5.3 и §8.2 таблица опыта, уровни скиллов | 7 |
| баффы скиллов (Боевой клич) | 14 |
| §4.6 эффекты и механики | 4, 16 |
| §5 атрибуты, здоровье, ресурс, защита | 5, 15 |
| §6.1–6.6 пайплайн удара | 9, 10 |
| §6.7 недуги | 12 |
| §6.8 случайность | 11 |
| §7 пассивки, keystones | 6, 16 |
| §8 скиллы, деревья, слоты, лут, триггеры, конфликты | 7, 8, 14 |
| §9 оружие | 7, 9 |
| §10.4 асценданты (механики) | 6, 16 |
| §11 манекены, покрытие, симуляция, выживание | 11, 13, 15 |
| §12 билд, экспорт и импорт | 5, 17 |
| §15.1, §15.2, §15.7 | 17 |
| §10 полный контент, §12 эталонные билды, §15.4–15.6, §15.9–15.10, калибровка §11.1 | **План 2** |
| §13 отладочная сцена, §15.3 | **План 3** |
