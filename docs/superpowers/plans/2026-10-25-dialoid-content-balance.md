# Dialoid Content & Balance Implementation Plan (план 2 из 3)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Наполнить `data/` полным игровым контентом этапа 1 (2 класса, 12 скиллов с деревьями, 4 асценданта, пассивное дерево ~160 узлов, 2 скилла из лута, оружие, манекены, эталонные билды) и доказать баланс приемочными тестами спеки §15.5, §15.6, §15.9, §15.10 и калибровкой §11.1.

**Architecture:** Контент — только JSON в `data/`. Код ядра меняется лишь в задаче 1 (ужесточение загрузчика, публичный JSON билда). Все проверки контента и баланса — тесты в `core/Dialoid.Core.Tests/Content/` поверх реальной `data/` через общий помощник `ContentHarness`. Эталонные билды лежат в `data/builds/` как JSON (формат `BuildCodec`, без gzip).

**Tech Stack:** .NET 8 (`net8.0`, RollForward на SDK 10), C# 12, xUnit, System.Text.Json. Godot не нужен.

**Spec:** `docs/superpowers/specs/2026-10-25-dialoid-core-systems-design.md` (утверждена). Справочник форматов данных: `docs/data-authoring-reference.md` — **обязателен к чтению каждым исполнителем**. Хвосты плана 1: `docs/superpowers/plans/2026-10-25-dialoid-core-engine-notes.md`.

## Global Constraints

- Данные: все `id` snake_case, уникальны в своем каталоге; ссылки только по `id`; `name`/`description` — на русском.
- Существующие `data/stats.json`, `data/tags.json`, `data/ailments.json`, `data/progression.json` только дополнять; три теста реальных данных из `Data/DataLoaderTests.cs` должны оставаться зелеными.
- Числа спеки — стартовые (§1 «не цели: финальный баланс»). Тюнинг разрешен в пределах ориентиров спеки; каждое отклонение от числа, явно написанного в спеке, записывается в `data/BALANCE_NOTES.md` (что, было → стало, почему).
- id скиллов фиксированы (совпадают с умолчаниями механик): Воитель — `cleave`, `crush`, `battle_cry`, `whirlwind`, `charge`, `throwing_axe`; Чародей — `spark`, `fireball`, `ice_lance`, `ball_lightning`, `meteor`, `blink`; из лута — `poison_cloud` (без класса). Классы: `warrior`, `sorcerer`. Асценданты: `berserker`, `thunderer`, `pyromancer`, `cryomancer`. Бафф Боевого клича: `battle_cry`.
- Правила деревьев скиллов (§8.4), проверяются тестом: 15–22 узла; сумма `max_points` ≥ 30; ≥ 1 `exclusive_group`; узлов «только +% урона» ≤ 25%; ≥ 3 направления, хотя бы одно меняет механику. Полные деревья `fireball` и `cleave` — ровно по §10.3 (узлы, очки, требования, эффекты; отклонения — только через BALANCE_NOTES с причиной «не выражается движком»).
- Асценданты (§10.4): стартовый узел + 6 notables по 2 очка; голые «+X%» запрещены.
- Keystones (§7.4): 6 штук, id узлов `keystone_blood_pact`, `keystone_titan_will`, `keystone_inevitability`, `keystone_avatar_of_fire`, `keystone_blade_dance`, `keystone_stone_skin`, каждый — один эффект `mechanic` соответствующего id.
- Механики со scope «только global» в данных указываются только в global-scope (валидатор проверяет).
- Приемочные тесты баланса работают в режиме `SimMode.Average` (§6.8).
- Отличия движка от спеки, которые контент должен учитывать: задержки (Метеор 1 с), перемещение (Рывок, Мерцание), притягивание не моделируются; ключ поведения `duration` движком не читается — длительность наземных эффектов задается `ground_duration`.
- Контентные задачи (3–8) после ревью кода проходят дополнительное ревью агентом `arpg-design-reviewer` (контроллер вызывает его на итоговом JSON).
- Коммит после каждой задачи; сообщения коммитов на английском (conventional commits).

## Review Focus

1. **Опечатка в имени поля JSON** (`max_point`, `requiers`) сейчас молча игнорируется → узел работает не так, как задумано. Ожидание: ошибка загрузки с файлом и `id`. Тест в задаче 1.
2. **Эталонный билд стал нелегальным после правки контента** (переименован узел, сдвинут unlock_level). Ожидание: тест падает с понятным нарушением. Тест в задаче 9 (`Every_reference_build_is_legal`).
3. **Скилл в ротации эталона ни разу не применяется** (не хватает ресурса, нет оружия). Ожидание: тест падает, называя скилл. Тест в задаче 9 (`Every_rotation_skill_is_used`).
4. **Узел дерева скилла, который ничего не меняет** (анти-цель §1). Ожидание: взятие каждого узла меняет либо скомпилированный скилл, либо DPS/выживание; исключения перечислены явно с причиной. Тест в задаче 9 (`Every_skill_tree_node_has_an_effect`).
5. **Режим «бросок» сильно расходится со «средним»** (накопитель/матожидание реализованы криво для реального контента). Ожидание: среднее 200 прогонов «броска» в пределах ±10% от «среднего» для одного эталона каждого класса. Тест в задаче 10.

---

## Структура файлов

```
data/
  attributes.json  classes.json  passives.json  ascendancies.json  skills.json
  weapons.json  items.json  dummies.json        (новые)
  builds/                                        (эталоны, варианты для тестов)
    warrior_berserker_whirlwind.json  warrior_thunderer_crush.json  warrior_fire_cleave_crit.json
    sorcerer_pyromancer_fireball.json  sorcerer_cryomancer_ice_lance.json  sorcerer_spark_hybrid.json
    parity/   warrior_l1.json warrior_l20.json sorcerer_l1.json sorcerer_l20.json
    keystones/ <keystone>_fit.json, <keystone>_unfit.json  (12)
    attributes/ warrior_no_attribute_nodes.json  sorcerer_no_attribute_nodes.json
  BALANCE_NOTES.md
core/Dialoid.Core/
  Data/Json/StrictJson.cs                         (задача 1)
  Builds/BuildCodec.cs                            (задача 1: ToJson/FromJson)
core/Dialoid.Core.Tests/Content/
  ContentHarness.cs  CommonDataTests.cs  PassiveTreeContentTests.cs  SkillContentTests.cs
  LootContentTests.cs  AscendancyContentTests.cs  ReferenceBuildTests.cs  BalanceAcceptanceTests.cs
```

Контент разбит по каталогам, тесты — по каталогу контента. Перекрестная валидация требует согласованности классов/скиллов/дерева, поэтому `classes.json` появляется в задаче 3 с пустыми списками скиллов и асцендантов и дополняется в задачах 4–8.

---

### Task 1: Строгий загрузчик, публичный JSON билда, помощник контента

**Files:**
- Create: `core/Dialoid.Core/Data/Json/StrictJson.cs`
- Modify: `core/Dialoid.Core/Data/DataLoader.cs` и все `Data/Json/*Json.cs` (подключить строгую проверку), `core/Dialoid.Core/Builds/BuildCodec.cs`
- Create: `core/Dialoid.Core.Tests/Content/ContentHarness.cs`, `core/Dialoid.Core.Tests/Data/StrictJsonTests.cs`, `core/Dialoid.Core.Tests/Builds/BuildJsonTests.cs`

**Interfaces:**
- Produces:
```csharp
namespace Dialoid.Core.Data.Json;
internal static class StrictJson
{ // DataError на каждое свойство объекта, не входящее в allowed; сообщение: «Неизвестное поле «x»» с file и id
  public static void RejectUnknown(JsonElement obj, IReadOnlySet<string> allowed, string file, string? id, List<DataError> errors); }
namespace Dialoid.Core.Builds;
public static partial class BuildCodec
{ public static string ToJson(Build build);                                           // тот же snake_case, что внутри Export, с отступами
  public static bool TryFromJson(string json, GameData data, out Build? build, out string? error); } // те же проверки id, что TryImport
namespace Dialoid.Core.Tests.Content;
public static class ContentHarness
{ public static GameData Data { get; }                       // DataLoader.Load(FixturePaths.RealData) один раз, Assert на пустые ошибки
  public static Build LoadBuild(string relativePath);        // data/builds/<path>.json → TryFromJson, Assert без ошибок
  public static SimResult Run(Build b, string dummyId, int? pack = null); // Average, 60 с
  public static SimResult Survival(Build b);                 // Average, dummy "attacking_boss", DummyAttacks=true
  public static double TrainingDps(Build b);                 // Run(b, "training").DpsSingleTarget
  public static Build Clone(Build b); }                      // глубокая копия для вариантов в тестах
```
Все объекты каталогов (включая вложенные: resource, max_per, attack, узлы деревьев, requires, эффекты, триггеры, действия, модификаторы, per, conditions-объекты, start/notables асцендантов) проверяются на неизвестные поля.

- [ ] **Step 1: Тесты**
```csharp
[Theory] // для каждого каталога и каждого вложенного объекта по одному случаю на копии tiny
[InlineData("skills.json", "max_point")] [InlineData("passives.json", "linkz")] [InlineData("classes.json", "base_attribute")]
[InlineData("dummies.json", "life_per_targt")] /* + effects: "valeu", trigger, action, requires[].pointz */
void Unknown_field_is_a_load_error(string file, string badField)   // DataError: File == file, Id == id записи, Message содержит badField
[Fact] void Tiny_and_data_only_fixtures_still_load_cleanly()
[Fact] void Build_json_roundtrip()                                  // ToJson → TryFromJson == исходный билд
[Fact] void Build_json_unknown_id_fails_with_reason()
[Fact] void Build_json_garbage_fails_without_throwing()
```
- [ ] **Step 2: FAIL.** `dotnet test core/Dialoid.sln --filter "FullyQualifiedName~StrictJson|FullyQualifiedName~BuildJson"`
- [ ] **Step 3: Реализация.** Если фикстуры содержат поля, ставшие неизвестными, — исправить фикстуры, не ослабляя проверку. `ContentHarness` пока не используется тестами контента (данных нет) — компилируется.
- [ ] **Step 4: PASS** весь набор.
- [ ] **Step 5: Commit:** `feat(data): reject unknown JSON fields; public build JSON; content test harness`

---

### Task 2: Общие данные — атрибуты, оружие, манекены

**Files:**
- Create: `data/attributes.json`, `data/weapons.json`, `data/dummies.json`, `data/BALANCE_NOTES.md`
- Test: `core/Dialoid.Core.Tests/Content/CommonDataTests.cs`

**Interfaces:**
- Produces: оружие `sword` (one_hand), `greataxe` (two_hand), `staff` (staff), `wand` (wand); манекены `training`, `armored`, `elemental`, `swarm`, `boss`, `attacking_boss`.

Значения (§5.1, §9, §11.1): `attributes.json` — как фикстура tiny (str→armor 2, int→четыре сопротивления 0.1, will→resource_gen и life_regen_pct increased 0.2, dex→dodge_chance 0.05; `primary_damage_more_per_point` 0.25). Оружие — стартовые числа билдера, ориентир: меч 10–15 / 1.4 aps / крит 5; двуручный топор 22–33 / 0.9 / крит 6; посох 8–12 / 1.1 / крит 4 с имплицитом `cast_speed` +10 и `damage` +15 с тегом `spell`; жезл 5–8 / 1.5 / крит 7 с имплицитом `damage` +20 с тегом `spell`. Манекены — ровно §11.1: training (бессмертный, 0 защиты, 1 цель), armored (броня 2000), elemental (сопр. 40 всем), swarm (5 целей, броня 200, сопр. 10, `life_per_target` 150, respawn), boss (броня 1000, сопр. 30, 8000), attacking_boss (как boss, бессмертный, атака physical 14 + fire 9 раз в 3.5 с, `freeze_duration_multiplier` 0.5). У boss тоже `freeze_duration_multiplier` 0.5.

- [ ] **Step 1: Тесты**
```csharp
[Fact] void Real_data_has_attribute_rules_matching_spec()      // 0.25 и восемь бонусов из §5.1
[Fact] void Four_weapons_one_per_type()
[Fact] void Six_dummy_presets_match_spec_11_1()                 // поля как выше
[Fact] void Bosses_halve_freeze_duration()
```
- [ ] **Step 2: FAIL. Step 3: Данные. Step 4: PASS.**
- [ ] **Step 5: Commit:** `feat(content): attributes, weapons and dummy presets`

---

### Task 3: Классы (каркас) и пассивное дерево

**Files:**
- Create: `data/classes.json` (оба класса; `skills: []`, `ascendancies: []`), `data/passives.json`
- Test: `core/Dialoid.Core.Tests/Content/PassiveTreeContentTests.cs`

**Interfaces:**
- Produces: классы `warrior` и `sorcerer` по §10.1/§10.2 (база атрибутов, основной атрибут, ресурс Ярость `fury` 100/0/5/3 и Мана `mana` 100 + `max_per {int, 2}` / реген 5; `life_multiplier` 1.2 у Воителя; классовые бонусы §10.1/§10.2). Старты `start_warrior`, `start_sorcerer`. Keystone-узлы с id из Global Constraints.

Требования к дереву (§7): 140–200 узлов; регионы `str`, `int`, `will`, `dex`; доли: малые ~60%, атрибутные ~15%, notables ~20% (допуск ±5 п.п.), keystones ровно 6; notables окружены 2–4 малыми узлами своей темы; Воитель стартует между str и will, Чародей — между int и will; dex достижим обоими; координаты `x,y` заданы (для отрисовки в плане 3, узлы не накладываются: расстояние между любыми двумя ≥ 1). Эффекты узлов — тематические (урон по типам/тегам, защита, ресурс, недуги, крит, скорость), значения стартовые: малый ≈ +8–10% increased или эквивалент, notable 2–3 модификатора. Каждый keystone расположен глубже первого кольца (кратчайший путь от ближайшего старта ≥ 8 узлов).

- [ ] **Step 1: Тесты**
```csharp
[Fact] void Node_count_between_140_and_200()
[Fact] void Kind_shares_match_spec()                    // small 55–65%, attribute 10–20%, notable 15–25%, keystone == 6
[Fact] void Six_keystones_each_one_mechanic_effect()    // id узла ↔ id механики по таблице
[Fact] void Starts_sit_between_their_regions()           // соседи старта Воителя только str/will, Чародея — int/will
[Fact] void Dex_region_reachable_from_both_starts()
[Fact] void Keystones_are_at_least_8_steps_from_any_start()
[Fact] void Every_notable_has_two_to_four_small_neighbours_of_its_region()
[Fact] void No_two_nodes_share_coordinates()
[Fact] void Classes_match_spec_10()                      // базы атрибутов, ресурс, life_multiplier, классовые бонусы
```
- [ ] **Step 2: FAIL. Step 3: Данные. Step 4: PASS.**
- [ ] **Step 5: Commit:** `feat(content): classes and passive tree`

---

### Task 4: Скиллы Воителя

**Files:**
- Modify: `data/skills.json` (6 скиллов), `data/classes.json` (`warrior.skills`), `data/tags.json` (добавить недостающие теги, если нужны)
- Test: `core/Dialoid.Core.Tests/Content/SkillContentTests.cs` (общие проверки деревьев + проверки Воителя)

**Interfaces:**
- Consumes: оружие из задачи 2, класс из задачи 3.
- Produces: `cleave` (полное дерево §10.3), `crush`, `battle_cry` (тег `buff`, `on_use` → `apply_buff` id `battle_cry` +20% `damage` 8 с, cooldown 20), `whirlwind` (channel, `resource_cost` 10/с, `channel_interval` 0.4), `charge`, `throwing_axe` (`weapon_types` one_hand/two_hand). Таблица §10.1 — роли, теги, unlock_level, стоимость/генерация. Деревья остальных пяти — по направлениям §10.3 и правилам §8.4.

Решения по невыразимым местам §10.3 (Рассекающий удар): «Эхо» — `max_points` 1, `every_n_uses(4)` → `cast_skill crush 30%`, плюс узел «Раскатистое эхо» (требует Эхо 1, 1 очко) — модификатор `damage more 100` с тегами `skill:crush`, `triggered` (итого 60%, как в спеке). «Раскол» (второй удар силой 50%) — `grant_proc` с триггером `on_use` и действием `cast_skill cleave` с `damage_percent` 50 (триггерная копия самого скилла: движок разрешает, копии не запускают проков); записать в BALANCE_NOTES как способ выражения. «Раскаленный клинок» и «Удар грома» — одна `exclusive_group` (§10.3).

- [ ] **Step 1: Тесты**
```csharp
[Theory] /* по всем скиллам data с class_id != null или из лута */ void Skill_tree_follows_design_rules(string skillId)
// 15–22 узла; Σmax_points ≥ 30; ≥1 exclusive_group; доля узлов, чьи эффекты — только modifier stat=damage kind=increased/more без условий, ≤ 25%
[Fact] void Warrior_has_six_skills_from_spec_table()     // id, unlock_level 1/1/4/8/12/16, теги и роль генератор/спендер
[Fact] void Cleave_tree_matches_spec_10_3()               // 16 узлов §10.3 + «Раскатистое эхо»: id-набор, max_points, requires, exclusive_group
[Fact] void Battle_cry_buff_is_twenty_percent_for_eight_seconds()
```
- [ ] **Step 2: FAIL. Step 3: Данные. Step 4: PASS.**
- [ ] **Step 5: Commit:** `feat(content): warrior skills`

---

### Task 5: Скиллы Чародея

**Files:**
- Modify: `data/skills.json` (6 скиллов), `data/classes.json` (`sorcerer.skills`)
- Test: `core/Dialoid.Core.Tests/Content/SkillContentTests.cs` (проверки Чародея)

**Interfaces:**
- Produces: `spark` (generator, 0 маны, +3 маны, `chain_count` 2), `fireball` (полное дерево §10.3: база огонь 14–20, каст 0.75, 12 маны, взрыв 1.5, поджог 20%, крит 6, added eff 120; «Пламенное кольцо» через `ground_duration` 2 / `ground_fraction` 0.25), `ice_lance` (10 маны, `pierce_count` 3), `ball_lightning` (20 маны, `hits_per_use` 10, area), `meteor` (35 маны, задержка не моделируется), `blink` (movement, перезарядка 6, без урона). Узел развилки B `fireball` «Морозный шар» имеет id `frost_orb` (на него ссылается `grant_skill_node` Криоманта в задаче 8).

- [ ] **Step 1: Тесты**
```csharp
[Fact] void Sorcerer_has_six_skills_from_spec_table()     // unlock 1/1/4/8/12/16, стоимости, теги
[Fact] void Fireball_tree_matches_spec_10_3()              // 17 узлов: id-набор, очки, requires, развилки A и B
[Fact] void Fireball_ring_of_fire_creates_ground_zone()    // узел даёт ground_duration 2 и ground_fraction 0.25
```
(общий `Skill_tree_follows_design_rules` из задачи 4 автоматически покрывает новые скиллы)
- [ ] **Step 2: FAIL. Step 3: Данные. Step 4: PASS.**
- [ ] **Step 5: Commit:** `feat(content): sorcerer skills`

---

### Task 6: Скиллы из лута и предметы

**Files:**
- Modify: `data/skills.json` (`poison_cloud`), Create: `data/items.json`
- Test: `core/Dialoid.Core.Tests/Content/LootContentTests.cs`

**Interfaces:**
- Produces: `poison_cloud` (`class_id` null; spell, area, poison, dot; облако через `ground_duration` 3, отравление 100%, дерево 15 узлов: размер, длительность, стакание, смена на огонь); предметы `ring_of_mist` (`grants_skill` poison_cloud, бонус 0) и `veteran_axe` (`grants_skill` throwing_axe, бонус 2).

- [ ] **Step 1: Тесты**
```csharp
[Fact] void Poison_cloud_is_a_classless_loot_skill()
[Fact] void Sorcerer_can_slot_throwing_axe_with_veteran_axe_and_sword() // BuildRules без нарушений, кап 12
[Fact] void Loot_skill_cap_is_ten_plus_item_bonus()
[Fact] void Sorcerer_with_throwing_axe_deals_damage()                   // DPS > 0 на training
```
- [ ] **Step 2: FAIL. Step 3: Данные. Step 4: PASS.**
- [ ] **Step 5: Commit:** `feat(content): loot skills and items`

---

### Task 7: Асценданты Воителя

**Files:**
- Create/Modify: `data/ascendancies.json` (`berserker`, `thunderer`), `data/classes.json` (`warrior.ascendancies`)
- Test: `core/Dialoid.Core.Tests/Content/AscendancyContentTests.cs`

**Interfaces:**
- Produces: узлы строго по §10.4. Берсерк: старт `berserk_frenzy`; notables `insatiable`, «Кровавый пир» (`blood_feast` + `bleed_max_stacks` flat +7), `reckless`, «Вихрь ярости» (`whirl_rage`, `skill` whirlwind), `flesh_rend`, `battle_trance`. Громовержец: старт — конверсия 25% физ→молния для атак (`add_conversion` scope tags `attack`); notables «Разряд» (`ground_type` + наземная зона на `crush` через `set_behavior` ground_duration 3 / ground_fraction 0.2 scope `skill:crush` + `shock_chance` +100 на crush), «Цепной удар», `static_charge`, «Громовой топор» (`set_damage_type lightning` + `chain_count` +1 на throwing_axe), `grounding`, «Буря в жилах».

- [ ] **Step 1: Тесты**
```csharp
[Theory][InlineData("berserker")][InlineData("thunderer")]
void Ascendancy_has_start_and_six_two_point_notables(string id)
[Fact] void No_notable_is_a_bare_percentage()   // каждый notable содержит mechanic, conversion, behavior, damage_type, proc или условный модификатор
[Fact] void Berserker_build_compiles_and_simulates()   // временный билд L50 со всеми 4 notables, DPS > 0, нарушений нет
[Fact] void Thunderer_build_compiles_and_simulates()
```
- [ ] **Step 2: FAIL. Step 3: Данные. Step 4: PASS.**
- [ ] **Step 5: Commit:** `feat(content): warrior ascendancies`

---

### Task 8: Асценданты Чародея

**Files:**
- Modify: `data/ascendancies.json` (`pyromancer`, `cryomancer`), `data/classes.json` (`sorcerer.ascendancies`)
- Test: `core/Dialoid.Core.Tests/Content/AscendancyContentTests.cs`

**Interfaces:**
- Produces: по §10.4. Пироман: старт — `ignite_max_stacks` flat +2 для тега `spell`; notables `spread_ignite`, `ashes`, `eternal_fire`, «Метеоритный дождь» («3 падения по 50%»: модификатор `damage more −50` со scope `skill:meteor` на сам скилл плюс два эффекта `grant_proc` `on_use` → `cast_skill meteor` с `damage_percent` 50; записать в BALANCE_NOTES), `fire_armor`, `soul_heat`. Криомант: старт `chill_slow` (25) + `damage increased 10` с тегом `cold` и условием `target_chilled`; notables `shatter`, `ice_armor`, «Морозный шар» (`grant_skill_node skill=fireball node=frost_orb` + `ailment_swap skill=fireball`), `brittle`, «Северное сияние» (`freeze_chance` +10 для `spell`, `freeze_duration` increased 50), «Ледник» (наземная зона холода на `ice_lance`: `ground_duration` 3, `ground_fraction` 0.15).

- [ ] **Step 1: Тесты** — те же три вида, что в задаче 7, для `pyromancer` и `cryomancer` (`[InlineData]` расширяются), плюс
```csharp
[Fact] void Cryomancer_frost_orb_takes_fork_b()   // при выбранном «Грозовом шаре» узел отключается, Fireball становится холодным
```
- [ ] **Step 2: FAIL. Step 3: Данные. Step 4: PASS.**
- [ ] **Step 5: Commit:** `feat(content): sorcerer ascendancies`

---

### Task 9: Эталонные билды и их санитарные проверки

**Files:**
- Create: `data/builds/*.json` (6 эталонов L60 по §12), `data/builds/parity/*.json` (4), `data/builds/attributes/*.json` (2)
- Test: `core/Dialoid.Core.Tests/Content/ReferenceBuildTests.cs`

**Interfaces:**
- Consumes: `ContentHarness` (задача 1).
- Produces: эталоны — `warrior_berserker_whirlwind`, `warrior_thunderer_crush`, `warrior_fire_cleave_crit`, `sorcerer_pyromancer_fireball`, `sorcerer_cryomancer_ice_lance`, `sorcerer_spark_hybrid`. Каждый: L60, 59 пассивок, асцендант с 4 notables, 5 слотов, уровни скиллов 20, ротация; разная доминирующая стихия или доминирующий скилл внутри класса. `parity/<class>_l1.json`, `_l20.json` — лучшая сборка того же класса на 1 и 20 уровне. `attributes/<class>_no_attribute_nodes.json` — копия одного эталона класса, где каждый атрибутный узел заменен соседним малым узлом (тот же бюджет очков, связность сохранена).

- [ ] **Step 1: Тесты**
```csharp
[Theory][MemberData(nameof(AllBuildFiles))] void Every_reference_build_is_legal(string path)        // BuildRules пусто (Review Focus 2)
[Theory][MemberData(nameof(MainBuilds))]   void Every_rotation_skill_is_used(string path)           // в логе "use" каждого скилла ротации ≥ 1 (Review Focus 3)
[Theory][MemberData(nameof(MainBuilds))]   void Reference_build_deals_damage_and_survives_somewhat(string path) // DPS>0; TimeToDeath ≥ 5 с
[Fact] void Builds_within_a_class_differ_by_dominant_type_or_skill()
[Fact] void Every_skill_tree_node_has_an_effect()   // Review Focus 4: для каждого скилла каждого класса — базовый билд класса с этим скиллом;
// взятие узла (с минимальными требованиями) меняет CompiledSkill или DPS/выживание; разрешенные исключения — список в тесте с причиной
```
- [ ] **Step 2: FAIL. Step 3: Данные. Step 4: PASS.**
- [ ] **Step 5: Commit:** `feat(content): reference builds with sanity tests`

---

### Task 10: Приемка баланса и калибровка

**Files:**
- Create: `data/builds/keystones/*.json` (12)
- Modify: любые числа контента и `data/dummies.json` в пределах спеки; `data/BALANCE_NOTES.md`
- Test: `core/Dialoid.Core.Tests/Content/BalanceAcceptanceTests.cs`

**Interfaces:**
- Consumes: эталоны задачи 9, `ContentHarness`.
- Produces: `keystones/<keystone>_fit.json` (подходящий билд с keystone) и `<keystone>_unfit.json` (неподходящий с keystone); для сравнения тест берет тот же билд и убирает из `passive_nodes` только узел keystone (keystone в этих билдах обязан быть листом пути, поэтому билд остается легальным; освободившееся очко не тратится).

- [ ] **Step 1: Тесты** (§15.5, §15.6, §15.9, §15.10, §11.1; Average)
```csharp
[Theory][InlineData("warrior")][InlineData("sorcerer")]
void Three_builds_per_class_within_2x_dps_on_training(string cls)          // max/min ≤ 2.0
[Theory] void No_build_dominated_on_both_dps_and_survival(string cls)        // Парето §15.5
[Theory][MemberData(nameof(Keystones))]
void Keystone_helps_fit_build_10_to_50_percent_and_hurts_unfit(string ks)    // fit: метрика ×(1.10..1.50); unfit: метрика хуже
// метрика — DPS на training, кроме blade_dance и stone_skin (TimeToDeath)
[Theory] void Attribute_nodes_neither_trap_nor_mandatory(string cls)         // |Δ| DPS и TimeToDeath ≤ 15% (§15.9)
[Theory][InlineData(1)][InlineData(20)][InlineData(60)]
void Class_parity_within_1_5x(int level)                                     // лучший эталон класса на уровне (§15.10)
[Theory][InlineData(20)][InlineData(60)]
void Boss_ttk_20_to_60_seconds_for_average_build(int level)                  // среднее арифметическое TimeToKill по эталонам уровня
[Theory][InlineData(20)][InlineData(60)]
void Survival_20_to_40_seconds_average_and_none_capped(int level)            // ни один эталон не упирается в 180
[Theory][InlineData("warrior_berserker_whirlwind")][InlineData("sorcerer_pyromancer_fireball")]
void Roll_mode_mean_of_200_runs_within_10_percent_of_average(string b)       // Review Focus 5
```
- [ ] **Step 2: Запуск.** Ожидание: часть тестов FAIL (баланс не настроен).
- [ ] **Step 3: Калибровка.** Итеративно править числа (сначала манекены §11.1, затем узлы и скиллы), каждый шаг — запись в BALANCE_NOTES. Нельзя ослаблять тесты и нельзя менять код ядра. Если тест недостижим без нарушения спеки — остановиться и сообщить (это вопрос владельцу).
- [ ] **Step 4: PASS** весь набор.
- [ ] **Step 5: Commit:** `feat(content): balance acceptance and calibration`

---

### Task 11: Документация контента и вопросы владельцу

**Files:**
- Modify: `README.md` (раздел «Контент»: что есть, как добавлять, как запускать тесты баланса), `docs/data-authoring-reference.md` (снять пометку «неизвестные поля игнорируются», добавить `ToJson/TryFromJson`, формат `data/builds/`), `docs/superpowers/plans/2026-10-25-dialoid-core-engine-notes.md` (отметить закрытые хвосты плана 2)
- Create: `docs/superpowers/content-owner-questions.md` — вопросы владельцу, выявленные в ходе плана (минимум: ailment_swap и глобальные модификаторы поджога; список отклонений от чисел спеки из BALANCE_NOTES)

- [ ] **Step 1:** Обновить документы. **Step 2:** `dotnet test core/Dialoid.sln` — PASS, 0 предупреждений. **Step 3: Commit:** `docs: content guide, authoring reference update, owner questions`

---

## Покрытие спеки

| Раздел спеки | Задача |
|---|---|
| §3.3 данные, понятные ошибки | 1 |
| §5.1 атрибуты, §9 оружие, §11.1 манекены | 2 |
| §7 пассивное дерево, keystones; §10.1–10.2 классы | 3 |
| §8.4 правила деревьев; §10.3 Воитель | 4 |
| §10.3 Чародей | 5 |
| §8.5, §10.5 лут | 6 |
| §10.4 асценданты | 7, 8 |
| §12 эталонные билды | 9 |
| §15.5, §15.6, §15.9, §15.10, калибровка §11.1 | 10 |
| §14 вопросы владельцу, README | 11 |
| §13 отладочная сцена, §15.3, §15.4 | **План 3** |
