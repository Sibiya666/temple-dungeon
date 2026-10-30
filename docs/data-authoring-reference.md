# Dialoid: справочник автора данных

Состояние на 2026-10-28, после плана 2 (контент и баланс; ядро `Dialoid.Core` плана 1); раздел 11 (башня) — на 2026-10-30, после плана 5. Справочник собран чтением кода. Если код расходится со справочником, прав код; расхождение нужно исправить здесь.

Код: `core/Dialoid.Core/`. Фикстуры тестов: `core/Dialoid.Core.Tests/Fixtures/{tiny,data_only}/`. Реальные данные: `data/`. Спека: `docs/superpowers/specs/2026-10-25-dialoid-core-systems-design.md`.

## 0. Общие правила загрузчика (`Data/DataLoader.cs`)

- Каталог — это JSON-массив объектов. Исключения: `attributes.json` и `progression.json` — одиночные объекты. Комментарии и висячие запятые допустимы.
- Имена полей пишутся в snake_case. Перечисления — строки в нижнем регистре; числа вместо них отклоняются.
- **Неизвестные поля — ошибка загрузки** («Неизвестное поле «x»», с файлом и id записи): проверяются все объекты, включая вложенные. Исключение: объекты, ключи которых — данные (`spell_damage`, `behavior`, `ailment_chances`, `resists`, `base_attributes`, `damage_per_hit`, `params` механики), у них своя проверка ключей. `per_point` допустим только у эффектов `modifier`, `add_behavior`, `add_conversion`.
- У каждой записи уникальный непустой `id` в пределах файла.
- Плохая запись пропускается, ошибка попадает в отчет. Результат — `LoadResult(Data, Errors)`; если ошибка хоть одна, `Data == null`. Сообщения на русском: `DataError(File, Id, Message)`.
- Перекрестная валидация (`Validator`) идет, только если все файлы загрузились чисто.
- **Обязательные файлы:** `stats.json`, `tags.json`, `ailments.json`. Остальные необязательны: `attributes`, `classes`, `passives`, `ascendancies`, `skills`, `weapons`, `items`, `progression`, `dummies`.
- Бесконечность в JSON не записывается; в фикстурах «без капа» — это `1000000000.0`.

## 1. Каталоги

### stats.json (обязательный)
- **Поля:** `id`, `name`, `default` (по умолчанию 0), `min` (по умолчанию −∞), `max` (по умолчанию +∞), `display` (`"number"` | `"percent"`).
- **Правила:** `min ≤ default ≤ max`. Все статы из `RequiredStats` (раздел 5) обязаны быть в файле.
- **Статы-основы:** статы с `id`, совпадающим с ключом поведения (`projectile_count`, `pierce_count`, `chain_count`, `area`, `duration`), служат основой для модификаторов.
- **Пример:** `{"id":"crit_chance","name":"Шанс крита","default":0,"min":0,"max":100,"display":"percent"}`

### tags.json (обязательный)
- **Формат:** `[{"id":"fire","name":"Огонь"}]`.
- **Правило:** любой тег, упомянутый в данных, должен быть здесь. Исключение — динамический тег `skill:<id>`.

### ailments.json (обязательный)
- **Поля:** `id`, `source_type` (`physical|lightning|cold|fire|poison`), `chance_stat`, `damage_fraction` (≥ 0), `duration_sec` (> 0), `max_stacks_stat`, `stack_rule` (`replace_weakest|strongest_only|unlimited`), `tag`.
- **Особые id:** движок по-особому обрабатывает `bleed`, `ignite`, `poison`, `shock`, `chill`, `freeze`.
- **Тест:** значения всех шести недугов закреплены тестом `Ailments_table_matches_spec_defaults`.

### attributes.json (одиночный объект)
- **Формат:** `{"primary_damage_more_per_point":0.25, "bonuses":[{"attribute":"str","stat":"armor","kind":"flat","per_point":2}, …]}`
- **Поля бонуса:** `attribute` — `str|int|will|dex`; `kind` — `flat|increased|more` (не `override`).
- **В фикстуре tiny:**
  - `str` → `armor` flat 2;
  - `int` → `resist_fire`/`resist_cold`/`resist_lightning`/`resist_poison` flat 0.1;
  - `will` → `resource_gen` increased 0.2 и `life_regen_pct` increased 0.2;
  - `dex` → `dodge_chance` flat 0.05.

### classes.json
- **Поля:**
  - `id`, `name`;
  - `primary` — `str|int|will|dex`;
  - `base_attributes` — ровно четыре ключа `str`, `int`, `will`, `dex`;
  - `life_multiplier` — по умолчанию 1;
  - `resource` — объект `{id, name, base_max > 0, regen_per_sec, decay_per_sec, decay_delay_sec, max_per?: {stat, every, cap?}}`;
  - `class_bonuses` — модификаторы в краткой форме;
  - `passive_start_node`, `skills`, `ascendancies`.
- **Перекрестные проверки:**
  - стартовый узел имеет `kind:"start"` и `start_for_class` этого класса, ровно один на класс;
  - у каждого скилла из списка `class_id` равен этому классу;
  - у каждого асценданта `class_id` равен этому классу, и класс указывает асценданта.
- **Старт ресурса:** ресурс с `regen_per_sec` 0 и `decay_per_sec` > 0 (как Ярость) стартует с нуля, остальные — полными.

### passives.json
- **Поля:**
  - `id`, `name`, `description`;
  - `kind` — `start|small|attribute|notable|keystone`;
  - `region`, `x`, `y`;
  - `links` — ненаправленные связи, достаточно указать с одной стороны;
  - `effects` — scope по умолчанию `global`;
  - `start_for_class` — обязателен, если `kind=start`, иначе запрещен.
- **Связность:** каждый узел достижим хотя бы от одного старта.
- **Атрибутные узлы:** эффектов не несут, дают +5 к атрибуту, выбранному в билде (`attribute_choices`).
- **Keystones:** компилируются последними; их «фиксированные» значения идут с приоритетом 1 000 000.

### ascendancies.json
- **Поля:** `id`, `name`, `class_id`, `start` `{id, name, description?, effects?}` (стоит 0), `notables` (непустой массив, `cost` по умолчанию 2).

### skills.json
- **Поля скилла:**
  - `id`, `name`, `description`;
  - `class_id` — `null` или отсутствует у скилла из лута;
  - `unlock_level` — 1..60;
  - `tags`, `is_triggered_only`.
- **Урон и время:**
  - `spell_damage` — `{"fire":[min,max]}`;
  - `weapon_effectiveness` (%) — **если задано, скилл становится атакой и требует оружие**;
  - `added_damage_effectiveness` — по умолчанию 100;
  - `cast_time`; у канала это запасной интервал тика;
  - `attack_time_multiplier`, `cooldown`.
- **Ресурс и попадания:**
  - `resource_cost`; у канала — в секунду;
  - `resource_gain`;
  - `crit_chance` — процентные пункты, используется для заклинаний;
  - `hits_per_use`.
- **Прочее:**
  - `behavior` — ключи из раздела 4;
  - `ailment_chances` — `{ailment: %}`;
  - `weapon_types` — подмножество `one_hand|two_hand|staff|wand`; пусто означает любое;
  - `tree`.
- **Узел дерева:**
  - `id`, `name`, `description`;
  - `max_points` — 1..5;
  - `requires` — `[{"node":"x","points":n}]`, без циклов;
  - `exclusive_group`;
  - `effects` — scope по умолчанию `skill:<этот скилл>`.
  - Модификатор `more` с `value × max_points < −100` — ошибка.
- **Теги со смыслом для движка:**
  - `channel` — канал;
  - `generator` — используется последним в ротации;
  - `spender` — читает механика `static_charge`;
  - `buff` — скилл не перекастуется, пока висит его собственный бафф;
  - `triggered` — не попадает в ротацию;
  - `area`, `projectile` — модель покрытия.
  - Теги типа урона **не добавляются** из `spell_damage` автоматически.

### weapons.json
- **Поля:** `id`, `name`, `type`, `phys_min`, `phys_max`, `attacks_per_second` (> 0), `crit_chance` (%), `implicits` (модификаторы в краткой форме).

### items.json
- **Поля:** `id`, `name`, `grants_skill`, `skill_level_bonus` (≥ 0).

### progression.json
- **Формат:** `{"character_levels":[60 порогов], "skill_levels":[20 порогов]}`; первый порог 0, пороги строго возрастают.

### dummies.json
- **Поля:**
  - `id`, `name`;
  - `pack_size` — 1..10;
  - `pack_radius` — по умолчанию 4;
  - `life_per_target` — `null` означает бессмертного; иначе умножается на `Gear(L)`;
  - `respawn`, `armor`;
  - `resists` — `{type: −100..100}`;
  - `freeze_duration_multiplier`;
  - `attack` — `{"damage_per_hit":{"physical":14,"fire":9},"interval_sec":3.5}`, урон умножается на `Gear(L)`.

## 2. Эффекты (`Data/Json/EffectJsonConverter.cs`, `ModifierJson.cs`)

**Scope:** `"global"` | `"skill:<id>"` | `{"tags":[...]}` (скиллы, у которых есть все эти теги после их собственного дерева).

| type | поля |
|---|---|
| `modifier` | краткая форма модификатора: в самом объекте или вложенно в `"modifier"`; `per_point` по умолчанию true |
| `add_tag` / `remove_tag` | `tag`; снятие `area` удаляет и `area_radius` |
| `set_behavior` | `param`, `value` |
| `add_behavior` | `param`, `delta`, `per_point` по умолчанию true |
| `add_conversion` | `from`, `to`, `percent`; только вправо: physical → lightning → cold → fire → poison |
| `set_damage_type` | `to` |
| `set_resource` | `field` (`cost`\|`gain`), `value` |
| `grant_proc` | `trigger`, `action` |
| `mechanic` | `id`, `params` |

**Краткая форма модификатора:**
- `stat`;
- `kind` — `flat|increased|more|override`;
- `value` — для `more` не меньше −100;
- `tags`;
- `per` — `{stat, every, cap?}`;
- `conditions` — строки или `{id, param, ref}`;
- `priority`.

**Формула:** `(base + ΣFlat) × (1 + ΣInc/100) × Π(1 + More/100)`, затем ограничение min/max. Статы-множители (по умолчанию 1): `cast_speed`, `attack_speed`, `cooldown_recovery`, `area`, `freeze_duration`, `damage`; для них используйте `increased`.

**Триггеры:** `on_use|on_hit|on_crit|on_kill`, `{"every_n_uses":N}`, `{"every_n_hits":N}`.

**Действия:**
- `{"apply_ailment":"id"}` — без броска шанса;
- `{"cast_skill":{"id":"..","damage_percent":N}}`;
- `{"apply_buff":{"id":"..","duration":s,"modifiers":[…]}}` — бафф не стакается, повторное применение обновляет его.

## 3. Условия

- **Список:** `target_ignited`, `target_chilled`, `target_shocked`, `target_bleeding`, `target_poisoned`, `target_frozen`, `resource_above_pct` (`param`, строго больше), `life_below_pct` (`param`, строго меньше), `recently_crit` (`param` — окно, по умолчанию 4 с), `recently_used_skill` (`ref` обязателен), `channelling_for` (`param`, `ref?`), `buff_active` (`ref` — id баффа из какого-нибудь `apply_buff` или `mechanic:<id>`).
- **Условия `target_*`** относятся к той цели, по которой идет удар.

## 4. Ключи поведения (`Skills/BehaviorKeys.cs`)

- **Список:** `area_radius`, `projectile_count`, `pierce_count`, `chain_count`, `duration`, `channel_interval`, `explosion_radius`, `ground_duration`, `ground_fraction`.
- **Радиусы:** растут как √`area`.
- **Количества:** `projectile_count`, `pierce_count`, `chain_count` задают основу одноименного стата.
- **Канал:** `channel_interval` — секунды на тик.
- **Наземная зона:** `ground_duration` (с) и `ground_fraction` (доля базы недуга в секунду) создают одну зону на скилл.
- **`duration`:** движок этот ключ **не читает**.

## 5. Обязательные статы (`Data/RequiredStats.cs`)

- **Основные:** `life`, `life_regen_pct`, `armor`, `dodge_chance`, `dodge_chance_max`, `damage`, `damage_taken`, `crit_chance`, `crit_damage`, `resource_max`, `resource_regen`, `resource_gen`, `resource_cost`, `cast_speed`, `attack_speed`, `cooldown_recovery`, `area`, `shock_effect`, `freeze_duration`, `str`, `int`, `will`, `dex`.
- **Сопротивления:** `resist_<e>` и `resist_<e>_max` для огня, холода, молнии и яда.
- **По типам урона:** `added_<t>_damage_min/max` и `penetration_<t>` для всех пяти типов.
- **По недугам:** `<a>_chance` и `<a>_max_stacks` для всех шести.
- **В `data/stats.json`** все они есть, плюс `duration`, `projectile_count`, `pierce_count`, `chain_count`.

## 6. Механики (`Mechanics/*.cs`, `MechanicCatalog.cs`)

- **Запись:** `{"type":"mechanic","id":"…","params":{…}}`.
- **Валидация:** неизвестные параметры, неверные типы и ссылки на несуществующие данные — ошибки загрузки. «Только global» означает, что scope `skill:`/`tags` — ошибка.
- **Умолчания с конкретными id.** У некоторых механик по умолчанию стоят id скиллов: `whirl_rage.skill`=`whirlwind`, `battle_trance.buff`=`battle_cry`, `soul_heat.skills`=[`fireball`,`meteor`], `ailment_swap.skill` — скилл из scope или `fireball`. Если в контенте другие id, задайте параметры явно.

**Keystones (только global):**
- `blood_pact`: `life_more` −30, `channel_max_sec` 2, `channel_cooldown_sec` 3, `cooldown_per_10_cost` 1.5;
- `titan_will`: `more_per_point` 0.5, `attribute` `will`;
- `inevitability`: `hit_more` 18, `ailment_chance_more` −30;
- `avatar_of_fire`: `fire_penetration` 15, `ignite_max_stacks` 1, `fire_resist_max` −15, `physical_to_fire` 50, `lightning_to_fire` 100, `cold_to_fire` 100;
- `blade_dance`: `dodge_per_dex` 0.15, `dodge_max_bonus` 10;
- `stone_skin`: `elemental_armor_fraction` 0.5.

**Берсерк:**
- `berserk_frenzy` (global): `fury_pct` 100, `attack_more` 20, `damage_taken` 10;
- `insatiable` (любой scope): `fury_pct`, `life_pct_per_10` 2;
- `reckless` (global): `cap` 40, `more_per_pct` 1;
- `whirl_rage` (любой): `skill`, `free_above_pct` 50, `speed_per_sec` 10, `speed_cap` 50;
- `flesh_rend` (любой): `ailment` `bleed`, `extra_stacks` 1;
- `battle_trance` (global): `buff` `battle_cry`, `fury_per_attack` 5, `suppress_stat` `damage`;
- `blood_feast` (global): `ailment` `bleed`, `stat` `attack_speed`, `cap` 15, `per_stack` 1.

**Громовержец:**
- `static_charge` (global): `max_charges` 10, `more` 100;
- `grounding` (любой): `ailment` `shock`, `min_effect` 20, `duration` 5;
- `ground_type` (любой): `type` `lightning`.

**Пироман:**
- `spread_ignite` (любой): `ailment` `ignite`, `delay` 1;
- `ashes` (любой): `ailment`, `cap` 25, `more_per_stack` 1, `condition` `target_ignited`;
- `eternal_fire` (любой): `ailment`, `duration_mult` 1.5;
- `soul_heat` (любой): `skills`, `ailment`, `per_target` 1, `max` 5;
- `fire_armor` (global): `armor_increased` 30, `resist_cap` 10.

**Криомант:**
- `shatter` (global): `more` 100;
- `ice_armor` (любой): `ailment` `freeze`, `duration` 4, `armor` 200;
- `grant_skill_node` (любой): `skill` и `node` обязательны;
- `ailment_swap` (любой): `skill`, `from` `ignite`, `to` `freeze`, `duration_stat` `freeze_duration`, `ratio` 0.5;
- `brittle` (global): `more` 15;
- `chill_slow` (любой): `slow` 25.

## 7. Правила билда (`Character/BuildRules.cs`)

- **Уровень и пассивки:**
  - уровень 1..60;
  - пассивных очков `L−1`, все узлы связаны со стартом класса, без дублей и без стартовых узлов;
  - для атрибутного узла обязателен выбор атрибута.
- **Асцендант:** с 20 уровня; очков 2/4/6/8 на 20/30/40/50+; в билде перечисляются только notables.
- **Слоты и уровни скиллов:**
  - слоты открываются на 1/4/8/14/20 уровнях;
  - родной скилл стоит в списке класса, кап 20, и закрыт ниже `unlock_level`;
  - скилл из лута требует надетый предмет, кап 10 + бонус предмета, максимум 14;
  - уровень слота не выше капа и не выше уровня персонажа;
  - очков в дереве не больше уровня слота.
- **Остальное:**
  - атака требует оружие подходящего типа;
  - в ротации только скиллы из слотов;
  - скиллы в слотах без повторов.

## 8. Публичный API

- **Загрузка:** `DataLoader.Load(string dir, MechanicRegistry? mechanics = null) → LoadResult`.
- **Симуляция:**
  - `new Simulator(GameData, MechanicRegistry.Default).Run(Build, SimConfig) → SimResult`;
  - при нелегальном билде симулятор не бросает исключение, а возвращает `Violations`.
- **Конфигурация:** `SimConfig(DummyId, SimMode Mode, Seed = 1, DurationSec = 60, StepSec = 0.05, PackSizeOverride = null, DummyAttacks = false, SurvivalCapSec = 180)`.
- **Результат:** `SimResult(DpsSingleTarget, DpsPack, DamageBySkill, DamageByType, HitDamage, DotDamage, TimeToKill?, TimeToDeath?, Log, Violations, DamageTaken?)`.
- **Проверка и кодек:**
  - `BuildRules.Validate(Build, GameData) → IReadOnlyList<RuleViolation>`;
  - `BuildCodec.Export(Build) → "DLD1:…"` (код: `DLD1:` + base64url(gzip(JSON)));
  - `BuildCodec.TryImport(code, data, out build, out error)`;
  - `BuildCodec.ToJson(Build) → string` — тот же JSON без gzip, с отступами, для ручной правки;
  - `BuildCodec.TryFromJson(json, data, out build, out error)` — разбор этого JSON с теми же проверками id, что у `TryImport`; не бросает исключений, ошибки на русском.
- **Сборка персонажа:** `new CharacterCompiler(GameData, MechanicRegistry?).Compile(Build) → CompiledCharacter`; перед вызовом нужен `BuildRules.Validate`.
- **JSON билда** (snake_case, как внутри кода билда):
  ```
  {"class_id":"…","level":20,"ascendancy_id":"…","ascendancy_nodes":["…"],"passive_nodes":["…"],
   "attribute_choices":{"node":"dex"},"slots":[{"skill_id":"…","level":3,"node_points":{"node":2}}],
   "weapon_id":"…","item_ids":["…"],"rotation":["…"]}
  ```
  Ротация: сначала не-генераторы по порядку, затем генераторы; если ротация пустая, берется порядок слотов.

## 9. Тесты и реальные данные

- **Тесты на `data/`.** Реальные данные читают тесты из `core/Dialoid.Core.Tests/Data/DataLoaderTests.cs` (три теста: данные загружаются без ошибок, множители по умолчанию 1 и не уходят ниже 0, таблица недугов совпадает со спекой) и тесты контента в `core/Dialoid.Core.Tests/Content/`:
  - `CommonDataTests`, `PassiveTreeContentTests`, `SkillContentTests`, `LootContentTests`, `AscendancyContentTests` — правила спеки для каждого каталога (деревья скиллов §8.4, keystones §7.4, асценданты §10.4, лут), без голых «+X%», без мертвых узлов;
  - `ReferenceBuildTests` — эталонные билды из `data/builds/` легальны и держат свою идею;
  - `BalanceAcceptanceTests` — приемка баланса §15.5, §15.6, §15.9, §15.10 и коридоры §11.1 (режим `SimMode.Average`);
  - `ContentHarness` — общие помощники.
  Остальные тесты работают на фикстурах tiny и data_only. Файлы `stats`/`tags`/`ailments`/`progression` только дополнять.
- **Помощники:**
  - `FixturePaths.RealData`, `FixturePaths.Tiny`, `FixturePaths.CopyTiny()`;
  - `Mechanics/MechanicTestData.cs`;
  - `Character/TestBuilds.cs`.

### Формат `data/builds/`

Каждый файл — один билд в виде JSON от `BuildCodec.ToJson` (тот же формат читает `BuildCodec.TryFromJson`; он описан в разделе 8). Имя файла без `.json` — идентификатор билда в тестах.

- `data/builds/*.json` — шесть основных эталонов 60 уровня (тематические: идея билда, а не потолок класса);
- `data/builds/l20/` — те же эталоны на 20 уровне (калибровка «Босса» и проверка лута);
- `data/builds/parity/<класс>_l1|l20|l60.json` — потолок класса без темы и keystones (паритет §15.10);
- `data/builds/keystones/<keystone>_fit|unfit.json` — пары «подходящий / неподходящий билд» для проверки §15.6;
- `data/builds/attributes/` — билды без атрибутных узлов (§15.9);
- `data/builds/README.md` — метрики и описание билдов; таблицы берутся из вывода `suite --all`, после пересборки эталонов их надо обновить.

Билд, который не проходит `BuildRules.Validate`, тест эталонов не принимает: после правки данных (переименование узла, смена прав скилла) файлы надо пересобрать.

### Как пересобрать эталоны

Инструмент `tools/BuildOptimizer` лежит вне решения (`core/Dialoid.sln` его не собирает). Рецепты — `tools/BuildOptimizer/rebuild_references.sh`. Запускать только последовательно (шаги читают файлы друг друга); полная пересборка идет ≈1,5–3 часа в зависимости от машины. После нее: проверить diff, прогнать тесты контента и `dotnet tools/BuildOptimizer/bin/Release/net8.0/BuildOptimizer.dll suite --all` (все метрики приемки одной таблицей).

## 10. Факты для баланса

- **Рост:** `Growth.Skill(L) = 1 + 0.05(L−1)`, `Growth.Gear(L) = 1 + 0.06(L−1)`.
- **База урона:**
  - заклинание: `spell_damage × Skill × Gear`;
  - атака: `урон оружия × эффективность/100 × Skill × Gear`;
  - добавленный урон умножается на `added_damage_effectiveness/100` и не растет с уровнем.
- **Персонаж:**
  - здоровье: `100 + 11 × L`, затем `life_multiplier` как More;
  - атрибуты: база класса + (L−1) ко всем + (L−1) к основному.
- **Крит:** шанс у атак берется из оружия, у заклинаний — из скилла; множитель `1 + crit_damage/100`.
- **Защита:**
  - броня: `armor/(armor + 10 × удар)`, кап 0.85;
  - сопротивления: `1 − (resist − pen)/100`, нижний предел −100.
- **Время:**
  - заклинание: `cast_time / cast_speed`;
  - атака: `attack_time_multiplier / (aps оружия × attack_speed)`;
  - канал: `channel_interval`, стоимость `cost × interval` за тик.
- **Манекен:**
  - здоровье: `life_per_target × Gear(L)`;
  - защитный режим (`DummyAttacks`): бой идет до смерти персонажа или до `SurvivalCapSec`.
- **Ориентиры спеки (§11.1):**
  - «Босс» должен умирать от среднего эталона за 20–60 с на 20 и 60 уровне;
  - выживание против «Атакующего босса» — 20–40 с;
  - ни один эталон не должен упираться в кап 180 с.

## 11. Башня: `data/tower/` (`Tower/TowerDataLoader.cs`, план 5)

Каталог башни грузится отдельно от остальных данных: `TowerDataLoader.Load(путь к data/tower)` → `(TowerData? Data, Errors)`. Правила загрузчика те же, что в разделе 0: snake_case, строгие поля (неизвестное поле — ошибка, у `rules.json` проверяются и объекты рангов), уникальные `id`, ошибки на русском с файлом и id, при любой ошибке `Data == null`. Все пять файлов обязательны. Порядок записей в файле сохраняется: от него зависит детерминированный порядок выпадения валют. Спека: `docs/superpowers/specs/2026-10-29-dialoid-endgame-tower-design.md`. Фикстура тестов: `core/Dialoid.Core.Tests/Fixtures/tower/` (`FixturePaths.TowerFixture`, копия для сломанных вариантов — `FixturePaths.CopyTowerFixture()`); реальные данные — `FixturePaths.RealTowerData`.

### rules.json (одиночный объект)
- **Устройство башни:** `floors` (этажей, > 0), `floor_slots` (модов на этаж, > 0), `tower_slots` (модов на башню, > 0), `height_multipliers` (по числу этажей, каждый > 0: множитель высоты этажа к награде и к выпадению валюты).
- **Качество награды:** `summit_bonus` (> 0, высота вершины), `diminishing_c` (> 0, убывающая отдача модов `1 + c·x / (c + x)`), `diversity_bonus_per_type` и `diversity_bonus_types_cap` (≥ 0: бонус за каждый тип сверх первого, не больше cap типов), `shuffle_bonus`, `no_death_bonus` (≥ 0), `quality_cap` (> 0, потолок итогового качества).
- **Перемешивание:** `shuffle_costs` — цены по порядку, первая обязана быть 0 (первое бесплатно), за концом списка цена удваивается.
- **Валюта:** `exchange_ratio` (≥ 2, обмен вверх на соседний уровень N:1), `exchange_min_level` (≥ 2: валюта уровня 1 не обменивается), `rank1_drop_multiplier` (≥ 1, обязательное: на ранге 1 выпадение валют с уровнем умножается на него, валют без уровня — нет; см. «Башня: экономика» в `data/BALANCE_NOTES.md`).
- **Жесткие контры:** `hard_counter_from_rank` (1–15, обязательное и равное `drops_from_rank` валюты `remove`): моды с заданным `hard_counter` (не null) попадают в пул и допускаются при входе только с этого ранга (на более низком вход отказывает с «ModAboveRank»).
- **Опыт:** `xp_penalty_per_level` (≥ 0, штраф за уровень выше полосы ранга), `xp_penalty_cap` (0..1).
- **`ranks`** — ровно 15 объектов по порядку: `rank` (1..15), `level_min` / `level_max` (полосы стыкуются, ранг 1 с 1-го уровня, ранги 6–15 — `60`–`60`), `mod_kinds` (`general` обязателен, плюс `floor_type`, `tower`), `max_tier` (1..5, не убывает). Уровень зоны рангов 6–15 вычисляется: 60 + (ранг − 5).
- **Пример ранга:** `{ "rank": 4, "level_min": 36, "level_max": 48, "mod_kinds": ["general", "floor_type", "tower"], "max_tier": 2 }`

### floor_types.json
- **Поля:** `id`, `name`, `has_end_boss`, `variants` (id разновидностей; непустой только у семейства Испытаний, без пустых и повторов).
- Тип `bosses` — арена вершины: моды башни, подходящие ему, действуют и на вершину.

### biomes.json
- **Поля:** `id`, `name`. Биом — только оформление, на числа не влияет. В коде — `TowerData.Biomes` (`BiomeDef(Id, Name)`, порядок файла).

### floor_mods.json
- **Поля:**
  - `id`, `name` (текст с `{v}` — выпавшее число);
  - `kind` — `general` | `floor_type` | `tower`;
  - `floor_types` — пусто = подходит всем типам; у `floor_type` ровно один тип, и для типа с разновидностями нужен `variant`, для остальных его быть не должно (у других видов `variant` всегда `null`);
  - `group` — группа несовместимости (пусто — `null`), `directions` — оси для направленных намазок, `style_tags` — метки стиля;
  - `hard_counter` — `null` | `no_life_regen` | `no_resource_regen` | `no_healing`; у мода башни запрещен;
  - действие на вершину не задается отдельным полем: мод башни действует на вершину (и прибавляет качество ее награды), если его `floor_types` пуст или содержит `bosses` (`FloorModDef.ActsOnSummit`); каждый такой мод обязан быть в группе `gate` — на вершине действует не больше одного (спека 5.2). На этажи мод башни действует и прибавляет качество только тем, чей тип подходит его `floor_types`. Поле `affects_summit` убрано и теперь считается неизвестным;
  - `tiers` — ровно 5 тиров по порядку: `tier`, `min`, `max` (`0 ≤ min ≤ max`, знак задает текст мода), `quality` (> 0, вклад в качество награды; у мода с диапазоном масштабируется выпавшим числом относительно середины).

### currencies.json
- **Поля:**
  - `id`, `name`;
  - `op` — `add_mod` | `add_mod_biased` | `reroll_one` | `reroll_value_high` | `upgrade_tier` | `clear` | `remove_one` | `reroll_variant` | `shuffle`;
  - `mod_kind` — только у наложений (`add_mod`, `add_mod_biased`);
  - `bias_direction` (направление, которое есть хотя бы у одного мода) и `bias_chance` (строго между 0 и 1) — только у `add_mod_biased`;
  - `has_level` — запрещен у `clear`, `remove_one`, `reroll_one`, `reroll_variant`, `shuffle` (они не прибавляют награды);
  - `drops_from_rank` (1..15) — с какого ранга валюта падает и разрешена;
  - `drop_base` (≥ 0), `drop_growth` (у валюты с уровнем > 1, у остальных ≥ 1), `summit_multiplier` (> 0) — нормы выпадения.
- **Обязательные валюты:** `shuffle` (`shuffle`), `imbue` (`add_mod`, `general`), `imbue_type` (`add_mod`, `floor_type`), `imbue_tower` (`add_mod`, `tower`).
- **Выпадение** (`Rewards.ExpectedCurrencyDrop`): `drop_base · drop_growth^(ранг − 1) · m`, где `m` — множитель высоты этажа или `summit_multiplier` на вершине; на ранге 1 у валюты с уровнем еще `· rank1_drop_multiplier`. Моды на количество не влияют. Бросок — целая часть плюс еще одна единица с вероятностью дробной. Уровень выпавшей валюты = ранг.
- **Пример:** `{ "id": "imbue", "name": "Наложение", "op": "add_mod", "mod_kind": "general", "has_level": true, "drops_from_rank": 1, "drop_base": 1.64, "drop_growth": 1.01, "summit_multiplier": 3.0 }`
- Нормы проверяет приемка экономики `core/Dialoid.Core.Tests/Tower/EconomyAcceptanceTests.cs`; после правки `drop_*` у Наложений прогнать ее.
