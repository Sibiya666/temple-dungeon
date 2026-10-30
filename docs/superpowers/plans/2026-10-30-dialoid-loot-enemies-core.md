# Dialoid Loot & Enemies Core Implementation Plan (план 6)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Лут этапа 1 и враги этапа 1 в ядре без мира: оружие заменяет заглушку G; предметы с аффиксами, звездочками, легендарными свойствами, перековкой, кубом и мастерворком входят в билд и в расчет; генератор лута (предметы и материалы) — чистая функция; монстры, элиты и боссы из данных сводятся к паку манекенов с силой `M × S`; приемочные тесты справедливости, силы лута и экономики.

**Architecture:** Два новых модуля ядра. `Dialoid.Core.Items` — данные предметов, экземпляр предмета, математика значения, строгая проверка экземпляра, генератор лута и `ItemCrafting` (вход + `IRandomSource` → новый экземпляр или отказ с русским текстом). `Dialoid.Core.Enemies` — данные монстров и сложности и сведение стаи или босса к `DummyDefinition` с силой врага. Общие файлы ядра правятся точечно и перечислены в задачах. Кодекс, кошелек материалов, пол, инвентарь и ИИ монстров — этап 2, не здесь.

**Tech Stack:** .NET 8 (`net8.0`, RollForward Major), C# 12, xUnit, Windows 10 + PowerShell 5.1. Godot только компилируется (`dotnet build game`, `tools/godot_check.ps1`).

> **Статус (2026-10-30).** Три круга ревью: dialoid-tech-reviewer, dialoid-loot-economy-designer, dialoid-encounter-designer — APPROVE. Код не начат: ждет предусловий ниже.

**Spec:** `docs/superpowers/specs/2026-10-29-dialoid-loot-design.md` и `docs/superpowers/specs/2026-10-29-dialoid-enemies-design.md` (утверждены 2026-10-29). Основа: `2026-10-25-dialoid-core-systems-design.md` (§4, §6, §8.5, §11). Связи: `2026-10-29-dialoid-endgame-tower-design.md` (§6, §8), план 5 `2026-10-29-dialoid-tower-core.md`.

## Предусловия (до первой задачи)

- **Ядро плана 4** (задачи 1–7, `CombatEngine`, живой бой) — влито в `main` (fa09599, 2026-10-30). Задачам 1–4 и 6–13 этого плана 3D-сцена не нужна.
- **Задача 5** правит `game/Ui/CharacterPanel.cs`, `SkillsPanel.cs`, `SelfTest.cs` (только адаптеры, чтобы `game/` компилировался). Задачи 8–12 плана 4 переписывают `game/`, поэтому задача 5 идет **после их вливания** (или по согласованию с сессией плана 4 правит уже новые окна).
- **`DamageType.Dark`** из реализации Отступника (план 7, сессия «Новый класс») — на 2026-10-30 не влит. Код плана начинается после того, как Dark и пересборка эталонов Отступника окажутся в `main` (порядок согласован: пересборки не идут одновременно).
- План 5 (`feat/tower-core`) может быть влит или нет: лут берет качество награды числом. Имена не пересекаются: здесь `ItemCrafting`, в плане 5 — `Tower.Crafting`; `Stats.ModKind` и `Tower.ModKind` в песочнице разводятся алиасом `using`.

## Global Constraints

- `core/` собирается с `TreatWarningsAsErrors` и `Nullable enable`; `dotnet test core/Dialoid.sln` зеленый, 0 предупреждений.
- **Утверждения существующих тестов не меняются**, кроме явно перечисленных в задачах 1 и 5 (там изменение — смена API или правило «без оружия P = 1», а не числа эталонов).
- **Эталоны не сдвигаются от замены G и миграции билдов.** `dotnet build tools/BuildOptimizer -c Release; dotnet tools/BuildOptimizer/bin/Release/net8.0/BuildOptimizer.dll suite --all` после задач 1, 5 и 11 совпадает с выводом на `main` до плана (сохранить в `tools/BuildOptimizer/out/baseline.txt`, вне git; сравнить `Compare-Object`). Пересборка эталонов — только в задаче 8, одна за раз (~1,5 ч).
- **Числа из спек (копировать точно):**
  - `P(L) = 1 + 0,06 × (L − 1)`, L — уровень предмета оружия в основной руке, 1–70; без оружия `P = 1`; заклинание берет от оружия только `P`; вторая рука в урон не входит;
  - 10 слотов `Weapon, Offhand, Helm, Chest, Gloves, Pants, Boots, Amulet, Ring1, Ring2`;
  - аффиксов с пола: белый 0, синий 2, желтый 3, легендарный 3; куб добавляет 4-й только желтому и легендарному;
  - звездочка: база = верхняя граница × 1,5, без броска; только у аффиксов с пола; не у флага `no_greater` (целочисленные статы и «+уровень скилла»); на рангах 1–3 шанс 0 при любом качестве;
  - мастерворк: 12 уровней, +2% от базы за уровень (сложение), крупный +10% на уровнях 4, 8, 12, каждый раз другому аффиксу; `итог = база × (1 + 0,02 × уровень + 0,10 × крупный)`; доступ 1–4 с ранга 1, 5–8 с 6, 9–12 с 10; округление один раз после звездочки и мастерворка (Flat — целое; крит и сопротивления — 0,5 п.п.; Increased — целый процент);
  - перековка: 2 варианта + текущий, первый перекованный аффикс закрепляется, цена (золото и осколки) ×1,5 за каждую следующую перековку предмета, звездочка снимается, аффикс куба не перековывается;
  - куб: призмы `offense, defense, resource, ailment, attribute`; добавление 4-го аффикса не тратит попытку, 5 попыток — на перебросы; обмен призм 3:1 в одну сторону;
  - разборка возвращает 60% материалов мастерворка (с рудой) и 50% осколков перековки; золото и призмы не возвращаются; сброс крупных бонусов МВ — за золото (`masterwork_reset_gold` в данных), возврат 60%;
  - легендарное свойство: одно на предмет; одинаковые не носятся вдвоем; не больше одного More и не больше +15%; свойств [×] надето не больше 3; цена здоровьем со всех свойств не ниже 50% базовой; механики `ascendancy_only` не выдаются; бросок числа хранится в экземпляре; диапазон свойства не зависит от уровня;
  - «+1 к уровню скиллов класса» — только амулет, только рост (+5%), не дает очков дерева, сверх 20 не больше +1; «+N к уровню скиллов из лута» — **сумма** со всех предметов (сейчас `SkillLevelCap` берет максимум — смена семантики), кап 14, дает и очки дерева;
  - требование: уровень предмета ≤ уровень персонажа + 5; на 60-м ограничения нет;
  - редкости и звездочки — таблица лута 7.3; смещение по типу этажа +30–50% к весу; `Champion` ×3, `RareLeader` ×6 + гарантированный желтый, `Boss` — гарантированная легендарка, `Summit` — 2;
  - враги: `S` — таблица врагов 8.2 (не калибруется); минимум телеграфа 1,5 / 1,0 / 0,7 с; моды «быстрее» ×0,7 не ниже; скорость атаки телеграф не укорачивает; удар и всплеск стаи за 1 с без телеграфа ≤ 35% eHP эталона ранга; скорость движения со всеми баффами ≤ 1,1; базовое сопротивление 0 / 10 / 20 / 30 и полосы смещения 0 / ±10 / ±15 / ±20 по рангам 1–2 / 3–5 / 6–9 / 10–15; боссы +10 п.п. без смещения; кап 75%; заморозка: рядовые 1,0, чемпион 0,75, редкий вожак 0,5, босс 0,5; элиты ×2,5/×1,3 и ×5/×1,5 (More); аффиксов на элиту 1 / 2 / 3 / 4 на рангах 1–3 / 4–7 / 8–11 / 12–15; доля уклоненного телеграфного урона 0,8 (данные);
  - «получаемый урон» пака — через `TargetState.SetDamageTaken("pack", %)` (та же Increased-корзина, что шок), заново после каждого `Reset()`.
- **Детерминизм.** Лут и крафт — только `RandomSources.Create(SimMode.Roll, seed)`; каналы `"loot.*"`, `"craft.*"`; бой их не трогает. Обход — в порядке записей файлов; `Build.Equipment` обходится в порядке `EquipSlot`.
- Данные строгие (`StrictJson`), ошибки — русские `DataError`, исключений нет. Новые каталоги необязательны для загрузчика (нет файла — пустой каталог), кроме проверок, которые на них ссылаются; реальные данные содержат все.
- Тексты для игрока — по-русски, идентификаторы — по-английски. Коммиты — conventional commits, в конце `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Ветка `feat/loot-enemies-core`, worktree `../dialoid-worktrees/loot-enemies-core`.

## Review Focus

1. **Старые билды** (`DLD1:`, JSON без префикса, `data/builds/*.json` с `weapon_id`/`item_ids`, `veteran_axe` с бонусом +2). Ожидание: импорт через отдельный DTO старого формата, оружие — экземпляр уровня билда, предмет-заглушка — легендарка «дает скилл» с аффиксом «+N к скиллам из лута», числа те же, что сняты с `main` до миграции. Тесты в задаче 5.
2. **Подделанный или невозможный предмет в коде билда** (бросок 1e9, лишний аффикс, звездочка у аффикса куба, МВ 13, два крупных бонуса на одном аффиксе, 4-е свойство [×]). Ожидание: `BuildRules.Validate` отвергает с кодом, урон не считается. Тесты в задаче 3.
3. **Крафт, который нельзя сделать** (куб на белом и синем, без попыток, пустая категория; перековка второго аффикса; МВ выше доступного ранга). Ожидание: отказ с причиной, экземпляр не тронут, призма не потрачена. Тесты в задаче 7.
4. **Пресеты песочницы и бессмертный манекен.** Ожидание: `dummies.json` ведут себя как до плана (сила = `P(уровень персонажа)` = G); монстры башни — `M × S`. Тесты в задачах 1 и 11.
5. **Детерминизм лута и крафта.** Один сид и одни действия → одинаковые предметы, материалы и результаты крафта; бой не меняет поток лута. Тесты в задачах 6 и 7.

---

## Структура файлов

```
data/item_bases.json        базы (заменяет weapons.json; оружие — базы слота weapon)
data/affixes.json           аффиксы
data/legendary_powers.json  легендарные свойства (заменяет items.json)
data/loot_tables.json       редкости, звездочки, смещения типов, количество, выход материалов по рангу
data/crafting.json          цены, выход разборки, возврат, куб, мастерворк
data/reference_gear.json    эталонная экипировка по билдам
data/monsters.json, monster_affixes.json, bosses.json, difficulty.json
core/Dialoid.Core/Items/    ItemData, ItemDataLoader, ItemInstance, ItemMath, ItemValidation,
                            LootGenerator, ItemCrafting, ItemRefusal, ReferenceGear, LegacyBuild
core/Dialoid.Core/Enemies/  EnemyData, EnemyDataLoader, EnemyPacks, EnemyRules
core/Dialoid.Core.Tests/Items/, Enemies/, Content/
core/Dialoid.Core.Tests/Fixtures/legacy_builds/   снимок старых билдов и их чисел до миграции
```

---

### Task 1: Сила оружия вместо G и сила врага

**Files:**
- Modify: `core/Dialoid.Core/Combat/Growth.cs`, `Combat/BaseDamage.cs:16-26`, `Simulation/CombatEngine.cs:83,413`, `Simulation/SimConfig.cs`, `Skills/SkillDefinition.cs:23`, `Simulation/FightSetup.cs:31`, `Explain/SkillSheet.cs:131`, `core/Dialoid.Sandbox/SandboxSession.cs:574-576`
- Create: `core/Dialoid.Core/Items/WeaponResolver.cs`
- Modify tests (только смена вызова и правило «без оружия»): `Combat/BaseDamageTests.cs` (`Spell_base_damage`, `Added_flat_damage_scaled_by_added_effectiveness`: заклинание без оружия на уровне 60 → множитель 1, а не 4,54; добавить оружие уровня 60, чтобы проверить прежнее число), `CryomancerTests.cs:87`, `DefenseTotalsTests.cs:45`
- Test: `core/Dialoid.Core.Tests/Combat/WeaponPowerTests.cs`

**Interfaces:**
- Produces:
  - `public static double Growth.WeaponPower(int itemLevel)`; `Growth.Gear` удаляется.
  - `WeaponStub` получает последнее поле `int ItemLevel`.
  - `public static WeaponStub? WeaponResolver.Resolve(Build build, GameData data)` — единственная точка получения оружия для `FightSetup`, `SkillSheet` и `SandboxSession` (до задачи 5 — из `Build.WeaponId` с `ItemLevel = build.Level`).
  - `BaseDamage.Compute` — `P = weapon is null ? 1 : Growth.WeaponPower(weapon.ItemLevel)` для заклинательной и оружейной частей.
  - `SimConfig.EnemyPower: double?` — `null` → `Growth.WeaponPower(build.Level)` (= прежний G) для жизни (`CombatEngine.cs:83`) и удара (`:413`) манекена.

- [ ] **Step 1 (на `main`, до любых правок):** снять `baseline.txt` (`suite --all`). Скопировать `data/builds/*.json`, `data/items.json` и по одному `DLD1:`-коду каждого эталона в `core/Dialoid.Core.Tests/Fixtures/legacy_builds/`; записать в `legacy_numbers.json` DPS по «Тренировочному» и «Рою» и выживание против «Атакующего босса» каждого билда в режиме «среднее». Проверить, что у всех `data/builds/*.json` есть `weapon_id`; если нет — остановиться и спросить «Управление проектом». Commit `test(items): snapshot legacy builds before migration`.
- [ ] **Правило для незапланированных падений:** если после правила «без оружия P = 1» падает тест вне списка выше (известные кандидаты: `MechanicTestData.Mage` ур. 20, `KeystoneTests` `test_mage` ур. 2/4/60, `SimulatorTests.Mage(4)`), фикстуре дается оружие уровня билда, утверждение не меняется; тест и причина перечисляются в сообщении коммита.
- [ ] **Step 2: Write failing tests** `WeaponPowerTests`: `Weapon_power_matches_old_gear` (L = 1, 20, 60 → `1 + 0.06*(L-1)`; 70 → 5,14); `Spell_takes_only_weapon_power` (огненный шар с мечом и с топором одного уровня — одинаковая база); `No_weapon_means_power_one`; `Enemy_power_defaults_to_character_level_power`; `Enemy_power_override_scales_life_and_hit`; `Sheet_and_sim_use_same_weapon_level` (база в `SkillSheet` = база в симуляции).
- [ ] **Step 3:** `dotnet test core/Dialoid.sln --filter WeaponPowerTests` → FAIL. **Step 4:** реализовать. **Step 5:** весь `dotnet test` зеленый; `suite --all` = `baseline.txt`.
- [ ] **Step 6:** commit `feat(core): weapon power replaces gear stub; enemy power hook`.

---

### Task 2: Данные предметов: базы и аффиксы

**Files:**
- Create: `data/item_bases.json`, `data/affixes.json`, `Items/ItemData.cs`, `Items/ItemDataLoader.cs`, `Data/Json/ItemBaseJson.cs`, `Data/Json/AffixJson.cs`
- Modify: `Data/DataLoader.cs`, `Data/GameData.cs` (+ `ItemBases`, `Affixes`; `Weapons` пока по-прежнему из `weapons.json`), `Data/Validator.cs`, `data/stats.json` (+ `skill_level_class`, `skill_level_loot`)
- `weapons.json` в этой задаче **не удаляется** (на него ссылаются `StrictJsonTests:22,64`, `SkillTreeTests:205`, `BerserkerTests:185` и фикстуры); удаление и переход `Weapons` на базы — в задаче 5. Тест `Old_weapons_keep_numbers` сверяет базы оружия с `weapons.json`.
- Test: `Items/ItemDataTests.cs`

**Interfaces:**
- Produces:
  - `public enum EquipSlot { Weapon, Offhand, Helm, Chest, Gloves, Pants, Boots, Amulet, Ring1, Ring2 }`; у баз `slot_kind` (`ring` подходит к `Ring1` и `Ring2`).
  - `public enum AffixCategory { Offense, Defense, Resource, Ailment, Attribute }`; `public enum RoundingRule { Integer, HalfPoint, Percent }`
  - `public sealed record WeaponStats(string Type, double PhysMin, double PhysMax, double AttacksPerSecond, double CritChance)`
  - `public sealed record ItemBaseDef(string Id, string Name, string SlotKind, IReadOnlyList<Modifier> Implicits, WeaponStats? Weapon, double DropWeight, string? ClassId)`
  - `public sealed record AffixRange(double MinAt1, double MaxAt1, double MinAt60, double MaxAt60, double MinAt70, double MaxAt70)`; `public (double Min, double Max) At(int itemLevel)` — кусочно-линейно 1→60→70.
  - `public sealed record AffixDef(string Id, string Name, string Stat, ModKind Kind, IReadOnlyList<string> Tags, AffixCategory Category, IReadOnlyList<string> SlotKinds, double Weight, AffixRange Range, string Group, bool NoGreater, bool NoMasterwork, RoundingRule Rounding)`
- Содержимое (**[Решение Claude]**, к балансу): 2–3 базы на слот (встроенные свойства — лут 4.4), 4 оружия с числами `weapons.json` на уровне 1; аффиксы пула лута 5.2 (≤ 12 на слот, без условных), диапазоны — таблица лута 5.5 (ур. 1/60/70); тьма — с `DamageType.Dark`.
- Проверки: неизвестный стат/тег; `More` у аффикса; > 12 аффиксов на слот-вид; у вида слота пустая категория; у вида слота нет базы; `NoGreater` обязателен у `skill_level_*`, `*_max_stacks`, `projectile_count`; `Min > Max`; диапазон убывает с уровнем.

- [ ] **Step 1: Write failing tests** `ItemDataTests`: `Real_item_data_loads`; `Old_weapons_keep_numbers` (меч 10–15, 1,4, крит 5); `Every_slot_kind_has_base_and_every_category`; `At_most_12_affixes_per_slot_kind`; `Integer_stats_are_no_greater`; `Affix_range_interpolates` (`+% урона` на 30: `Min = 3 + 9 × 29/59`, `Max = 5 + 13 × 29/59`, допуск 1e-9); сломанные копии: `More_affix_rejected`, `Unknown_stat_rejected`, `Empty_category_rejected`, `Slot_without_base_rejected`.
- [ ] **Step 2:** FAIL. **Step 3:** реализовать, перевести фикстуры. **Step 4:** весь `dotnet test` зеленый.
- [ ] **Step 5:** commit `feat(items): item bases and affix catalogs`.

---

### Task 3: Экземпляр предмета, значение и строгая проверка

**Files:**
- Create: `Items/ItemInstance.cs`, `Items/ItemMath.cs`, `Items/ItemValidation.cs`
- Modify: `Character/Build.cs` (+ `Equipment`, старые `WeaponId`/`ItemIds` **остаются** до задачи 5), `Character/BuildRules.cs` (`Validate` вызывает `ItemValidation`), `Builds/BuildCodec.cs` (сериализация `Equipment`, ключи-enum строками)
- Test: `Items/ItemMathTests.cs`, `Items/ItemValidationTests.cs`

**Interfaces:**
- Produces:
  - `public enum Rarity { White, Blue, Yellow, Legendary }`; `public enum AffixOrigin { Floor, Reforged, Cube }`
  - `public sealed record AffixRoll(string AffixId, double Roll, bool Greater, AffixOrigin Origin)`
  - `public sealed record MasterworkState(int Level, IReadOnlyList<int> BigBonusAffixIndices)`
  - `public sealed record ItemInstance(string BaseId, int ItemLevel, Rarity Rarity, IReadOnlyList<AffixRoll> Affixes, int? LockedAffixIndex, int ReforgeCount, int CubeAttemptsLeft, MasterworkState Masterwork, string? PowerId, double? PowerRoll)`
  - `public sealed record AffixValueBreakdown(double Roll, double AfterGreater, double MasterworkMultiplier, double Final)`; `public static AffixValueBreakdown ItemMath.AffixValue(ItemInstance item, int index, GameData data)`
  - `public static bool ItemMath.CanEquip(ItemInstance item, int characterLevel)`
  - `public static IReadOnlyList<string> ItemValidation.Check(EquipSlot slot, ItemInstance item, GameData data)` — коды: `base_wrong_slot`, `affix_wrong_slot`, `roll_out_of_range`, `affix_count`, `affix_group_repeat`, `greater_not_allowed`, `cube_affix_count`, `masterwork_range`, `big_bonus_repeat`, `cube_attempts_range`, `power_roll_out_of_range`, `item_level_range` (1–70).
  - `Build.Equipment: SortedDictionary<EquipSlot, ItemInstance>`.

- [ ] **Step 1: Write failing tests**: `Greater_ignores_roll`; `Masterwork_formula` (уровень 12, крупный у аффикса 0 → ×1,34; у аффикса 1 без крупного → ×1,24); `Rounding_half_point_for_resist`; `No_greater_flag_wins`; `Can_equip_rules` (ур. 20: 25 да, 26 нет; ур. 60: 70 да); `Equipment_roundtrips_in_codec`; в `ItemValidationTests` — по тесту на каждый код (в том числе `Roll = 1e9`, звездочка у аффикса куба, МВ 13, два крупных на одном индексе).
- [ ] **Step 2:** FAIL. **Step 3:** реализовать. **Step 4:** весь `dotnet test` зеленый.
- [ ] **Step 5:** commit `feat(items): item instance, affix value, strict item validation`.

---

### Task 4: Легендарные свойства и предметы в расчете

**Files:**
- Create: `data/legendary_powers.json`, `Data/Json/LegendaryPowerJson.cs`
- Modify: `Data/DataLoader.cs` (`items.json` → `legendary_powers.json`), `Data/GameData.cs`, `Data/Validator.cs`, данные механик ядра (+ поле `ascendancy_only`), `Effects/*` (бросаемый параметр), `Character/CharacterCompiler.cs:80-99`, `Skills/SkillCompiler.cs:21-39` (`CompilationContext` + `ItemEffects`), `Skills/CompiledSkill*.cs` (уровень дерева и уровень роста раздельно), `Combat/BaseDamage.cs` (`Growth.Skill(уровень роста)`), `Character/BuildRules.cs:13-31` (сумма бонусов), `Explain/*` (строка аффикса «бросок → звездочка → мастерворк → итог», бросок свойства)
- `data/items.json` и `GameData.Items` (`ItemStub`) в этой задаче **не трогаются**: `DataLoader` продолжает их грузить как есть, `Build.ItemIds` и все потребители (`BuildRules.cs:28-29,76`, `BuildCodec.cs:170`, `SandboxSession.cs:288`, `game/Ui/*`, тесты `SessionSkillTests`, `SkillContentTests`, `ReferenceBuildTests`, `BalanceAcceptanceTests`, `DataOnlyExtensionTests`, `StrictJsonTests`, `SkillTreeTests`, `AttributeTests`) работают как раньше. `legendary_powers.json` добавляется рядом; id свойств «дает скилл» — `throwing_axe`, `poison_cloud` (по скиллу), соответствие старым id — таблица `LegacyBuild` в задаче 5.
- Test: `Items/LegendaryPowerDataTests.cs`, `Items/ItemCompilationTests.cs`

**Interfaces:**
- Consumes: `ItemMath.AffixValue`, `Build.Equipment`.
- Produces:
  - `public sealed record RollParam(int EffectIndex, string Param, double Min, double Max)`
  - `public sealed record LegendaryPowerDef(string Id, string Name, IReadOnlyList<Effect> Effects, RollParam? Roll, string? ClassId, IReadOnlyList<string> SlotKinds, bool StartsUnlocked, string? GrantsSkill, bool HasMore)`
  - `SourceRef("item", slot, itemName)` для аффиксов и встроенных свойств; эффекты свойства — `SourceRef("item_power", powerId, name)`.
  - `SkillCompiler.Compile`: «дерево → пассивы → асцендант → **предметы** → keystones»; глобальные механики свойств — не-keystone в `MechanicCompiler.Order`.
  - `CharacterCompiler` подставляет `PowerRoll` в параметр `Roll`.
  - `BuildRules.SkillLevelCap`: скилл из лута — `min(14, 10 + сумма «+N к скиллам из лута» со всех предметов)`; `skill_level_class` поднимает только уровень роста, кап 21.
    До задачи 5 старые `ItemIds` считаются по прежнему правилу (максимум бонуса заглушки), а сумма — по `Equipment`; итог — `min(14, 10 + max(старый, сумма))`. Так `LootContentTests` и `SessionSkillTests` не меняются до задачи 5.
  - `BuildRules.Validate` коды: `item_level_too_high`, `duplicate_power`, `too_many_more_powers` (> 3), `power_wrong_slot`, `health_cost_floor` (не отвергает — зажимает до 50%).
- Проверки данных: эффекты и механики существуют; `Roll` — числовой параметр; More ≤ 1 и ≤ 15; механика `ascendancy_only` не выдается.
- Содержимое: свойства «дает скилл» из `items.json` (`poison_cloud`, `throwing_axe`); по 2 свойства каждого вида лута 6.1 (к балансу).

- [ ] **Step 1: Write failing tests**: `Item_affix_explain_chain` (строка «бросок → звездочка → мастерворк → итог»); `Item_effects_after_ascendancy_before_keystones` (Override keystone перекрывает предметный модификатор); `Power_roll_is_substituted`; `Fourth_more_power_rejected`; `Duplicate_power_rejected`; `Loot_skill_cap_sums_to_14` (билд с пустыми `ItemIds`, только `Equipment`: +2 амулет, +2 кольцо, +4 свойство → 14, не 18; раньше максимум); `Class_skill_level_only_growth` (рост ×1,05 сверх 20, очки дерева 20); `Ascendancy_mechanic_not_grantable` (сломанная копия); `Health_cost_floor_50` (если механики цены здоровьем нет в ядре — `Skip` с причиной).
- [ ] **Step 2:** FAIL. **Step 3:** реализовать. **Step 4:** весь `dotnet test` зеленый (старые тесты предметов не трогаются до задачи 5).
- [ ] **Step 5:** commit `feat(items): legendary powers and items in character compilation`.

---

### Task 5: Миграция билдов и оружие из экземпляра

**Files:**
- Create: `Items/LegacyBuild.cs` (DTO старого формата), `Fixtures/legacy_builds/*.json` и `legacy_numbers.json`
- Modify: `Character/Build.cs` (удалить `WeaponId`, `ItemIds`), `Builds/BuildCodec.cs` (`DLD2:`; `DLD1:` и JSON без префикса — через `LegacyBuild`, формат различается по наличию `weapon_id`/`item_ids`), `Items/WeaponResolver.cs` (из `Equipment[Weapon]`), `Character/BuildRules.cs:27-29,52-53,74-77,142-144`, `Character/CharacterCompiler.cs:80`, `core/Dialoid.Sandbox/SandboxSession.cs:277-356,575` (`SetWeapon(baseId)`/`ToggleItem(powerId)` — адаптеры к `Equipment`), `tools/BuildOptimizer/Parity.cs`, `game/Ui/CharacterPanel.cs:146-159`, `game/Ui/SkillsPanel.cs:190`, `game/SelfTest.cs:224`, `data/builds/*.json`
- Modify tests (смена API, не чисел): `TestBuilds`, `ReferenceBuildTests`, `BalanceAcceptanceTests`, `SessionSkillTests`, `BuildCodecTests`, `SkillSheetTests`, `LiveFightTests`, `DataOnlyExtensionTests` и остальные, что строят билд через `WeaponId`/`ItemIds` (найти `Select-String -Path core -Pattern 'WeaponId|ItemIds' -Recurse`)
- Test: `Builds/BuildMigrationTests.cs`

**Interfaces:**
- Delete: `data/items.json`, `data/weapons.json`, `GameData.Items`, `ItemStub`, загрузчики `LoadItems`/`LoadWeapons` и соответствующие файлы фикстур (`tiny`, `data_only`); `GameData.Weapons` строится из баз слота `weapon`; все потребители переходят на `Equipment` и `LegendaryPowerDef`. Тесты, которые ссылаются на эти файлы (`StrictJsonTests`, `SkillTreeTests`, `BerserkerTests`, `DataOnlyExtensionTests`, `AttributeTests`, `SessionSkillTests`, `LootContentTests`), переводятся на новые файлы без смены утверждений о числах и капе.
- Produces: `public static Build LegacyBuild.Upgrade(LegacyBuildJson old, GameData data)` (таблица старых id заглушек → свойство и бонус берется из `Fixtures/legacy_builds/items.json`-совместимой константы в `LegacyBuild`: `veteran_axe → throwing_axe, +2`, `ring_of_mist → poison_cloud, 0`) — `weapon_id` → белый экземпляр базы с `ItemLevel = level`; каждый `item_ids[i]` → легендарка свойства «дает скилл» в первый свободный слот из `Amulet, Ring1, Ring2`, с аффиксом «+N к уровню скиллов из лута», если у заглушки был `skill_level_bonus` (`veteran_axe` → +2), остальные аффиксы пустые. `ItemValidation` разрешает легендарке со свойством «дает скилл» 0–1 аффикс; диапазон аффикса `skill_level_loot` не зависит от уровня (1–2, `+2` допустим и на уровне 1); `SlotKinds` свойств «дает скилл» включают `amulet` и `ring`.

- [ ] **Step 1:** снимок уже сделан в задаче 1 (шаг 1, на `main`).
- [ ] **Step 2: Write failing tests** `BuildMigrationTests`: `Legacy_json_and_dld1_give_snapshot_numbers` (числа = `legacy_numbers.json` до бита); `Veteran_axe_keeps_plus_two`; `Dld2_roundtrip`; `Weapon_from_instance_level` (меч 30 и 60 → отношение `P(60)/P(30)`); `Offhand_does_not_hit`.
- [ ] **Step 3:** FAIL. **Step 4:** реализовать; мигрировать `data/builds/*.json`. **Step 5:** весь `dotnet test`, `dotnet build game`, `tools/godot_check.ps1` зеленые; `suite --all` = `baseline.txt`.
- [ ] **Step 6:** commit `feat(items): build equipment replaces weapon and item ids; DLD2 codec`.

---

### Task 6: Генератор лута (предметы и материалы)

**Files:**
- Create: `data/loot_tables.json`, `Data/Json/LootTablesJson.cs`, `Items/LootGenerator.cs`
- Test: `Items/LootGeneratorTests.cs`

**Interfaces:**
- Produces:
  - `public enum LootSource { Monster, Champion, RareLeader, Boss, Chest, Summit }`
  - `public sealed record LootRequest(int Rank, int ItemLevel, double Quality, string FloorType, string ClassId, LootSource Source)`
  - Источник полей в башне (main, ea918bc, `core/Dialoid.Core/Tower/`): `ItemLevel` = `TowerRun.ZoneLevel` (`RankRules.ZoneLevel`); `Quality` = `Rewards.FloorQuality(...).Total` для этажа и `Rewards.SummitQuality(...).Total` для `Summit`. Валюту модификации дает только `Rewards.RollCurrencyDrop`; `LootGenerator` ее не выдает (тест `Loot_never_drops_tower_currency`: ключи `Cost` только из списка выше). Связку забега с `LootGenerator` в этом плане не пишем: монстров в ядре башни нет, проводка — отдельная задача после плана 6.
  - `public sealed record Cost(IReadOnlyDictionary<string, long> Materials)` (long: рост 2,1^10 не помещается в int) — ключи `gold, scrap, shards, echo, prism_<category>, star_ore`.
  - `public sealed record LootDrop(IReadOnlyList<ItemInstance> Items, Cost Materials)`
  - `public static LootDrop LootGenerator.Roll(LootRequest request, GameData data, IRandomSource rng)`
  - `public static int LootGenerator.ChestCount(int rank, double quality, string floorType, LootSource source, GameData data)` — 2–4 × множитель качества (данные); `Summit` ×2.
- Алгоритм: число предметов — `1` у `Monster`/`Champion`/`RareLeader`/`Boss`, `ChestCount` у `Chest`/`Summit`; редкость по таблице ранга, веса `Yellow`/`Legendary` × `(1 + k × (Quality − 1))`, `White`/`Blue` ÷ тот же множитель; смещение типа этажа (+30–50%) и источника; база по весу, классовые базы и свойства ×1,5 для `ClassId`; аффиксы без повтора группы, число по редкости; звездочка — шанс ранга × качество (0 на рангах 1–3); свойство из пула слота, бросок в `Roll`; `CubeAttemptsLeft = 5`. Материалы: золото и призмы по источнику и типу этажа (Ритуал — больше призм), руда с ранга 10 у `Boss`/`Summit`; все количества × `material_rank_growth^max(0, rank − 5)` и × множитель качества для сундука.
- **`material_rank_growth`** (новый параметр, **[Решение плана]**): на рангах 6–15 уровень зоны растет на 1, и рост диапазона не перекрывает 3,5–4× разницы во времени. Спека 9 требует, чтобы выход рос круче времени, поэтому на рангах 6–15 выход, золото **и цены** растут по рангу геометрически с одним множителем (стартово 2,1 за ранг): выход разборки — по уровню предмета 61–70, цены крафта — по уровню предмета-приемника 61–70 (`cost_level_growth` = `material_rank_growth`, задача 7). На рангах 1–5 множителя нет: там растет сам уровень зоны. Так материалы низкого ранга покупают меньше, как валюта с уровнем в башне 8.3. Это трактовка спеки, а не ее изменение.

- [ ] **Step 1: Write failing tests**: `Same_seed_same_loot`; `Rarity_shares_match_table` (10 000 с ранга 1, качество 1,0: 40/40/18/2 ±2 п.п.); `Affix_count_by_rarity` (0/2/3/3); `No_greater_ranks_1_to_3_any_quality` (качество 10); `Greater_only_on_floor_affixes`; `Quality_raises_rarity_not_rolls`; `Quality_raises_chest_count_and_prisms`; `Rare_leader_guarantees_yellow`; `Boss_guarantees_legendary`; `Affix_groups_never_repeat`; `Star_ore_from_rank_10_only`; `Loot_never_drops_tower_currency`.
- [ ] **Step 2:** FAIL. **Step 3:** реализовать. **Step 4:** PASS.
- [ ] **Step 5:** commit `feat(items): seeded loot generator with materials`.

---

### Task 7: Крафт

**Files:**
- Create: `data/crafting.json`, `Data/Json/CraftingJson.cs`, `Items/ItemCrafting.cs`, `Items/ItemRefusal.cs`
- Test: `Items/CraftingTests.cs`

**Interfaces:**
- Consumes: `Cost`, `ItemInstance`, `ItemValidation`.
- Produces:
  - `public sealed record CraftResult(ItemInstance? Item, Cost Cost, ItemRefusal? Refusal)`
  - `public sealed record ReforgeOffer(ItemInstance PaidItem, int AffixIndex, IReadOnlyList<AffixRoll> Options)` — `Options[0]` текущий, `[1..2]` новые.
  - `ItemCrafting.Cube(ItemInstance, AffixCategory, GameData, IRandomSource) → CraftResult` — нет аффикса куба → добавить (попытка не тратится); есть → перебросить (−1 попытка).
  - `ItemCrafting.MasterworkStep(ItemInstance, int rank, GameData, IRandomSource) → CraftResult`; `ItemCrafting.MasterworkReset(ItemInstance, GameData) → (ItemInstance Item, Cost GoldCost, Cost Refund)`
  - `ItemCrafting.Salvage(ItemInstance, GameData) → Cost`
  - `ItemCrafting.ExchangePrisms(AffixCategory from, AffixCategory to, int count) → (Cost Spent, Cost Got)` — 3:1.
  - `public enum ItemRefusal { NotYellowOrLegendary, AffixLocked, CubeAffixNotReforgeable, NoCubeAttempts, CategoryEmpty, MasterworkMax, MasterworkRankLocked, ExchangeSameCategory, ExchangeTooFew }`; `ItemTexts.Describe(ItemRefusal) → string` по-русски.
- Цены и выход — таблицы лута 9 (`L` — уровень предмета-приемника) × `cost_level_growth^max(0, L − 60)` для цен и тот же множитель для выхода разборки; `masterwork_reset_gold = 100 × L × cost_level_growth^max(0, L − 60)` (**[Решение плана]**, числа в спеке нет). Звездная руда тратится на мастерворк 9–12 (`1 + (уровень МВ − 8)` за шаг, лут 9). `ExchangePrisms` с `count < 3` — отказ `ExchangeTooFew`, иначе обменивается `count / 3 × 3`, остаток не тратится.

- [ ] **Step 1: Write failing tests**: `Reforge_locks_first_affix`; `Reforge_keep_current_still_costs_and_locks`; `Reforge_new_offer_costs_again`; `Reforge_choice_only_from_offer`; `Reforge_removes_greater`; `Reforge_gold_and_shards_grow_1_5x`; `Prism_exchange_remainder_not_spent`; `Costs_and_salvage_scale_together_above_60`; `Cube_affix_not_reforgeable`; `Cube_refused_on_white_and_blue`; `Cube_add_is_free_then_five_rerolls`; `Cube_empty_category_does_not_spend_prism`; `Masterwork_big_bonus_never_twice_on_same_affix`; `Masterwork_rank_gates` (5 на ранге 5 — отказ); `Salvage_refunds_60_masterwork_50_reforge_shards`; `Reset_refunds_60_percent`; `Prism_exchange_is_one_way`; `Refusal_leaves_item_untouched`; `Same_seed_same_craft`; `Crafted_items_pass_validation` (каждый результат проходит `ItemValidation.Check`).
- [ ] **Step 2:** FAIL. **Step 3:** реализовать. **Step 4:** PASS.
- [ ] **Step 5:** commit `feat(items): reforge, cube, masterwork, salvage`.

---

### Task 8: Эталонная экипировка и сила лута

**Files:**
- Create: `data/reference_gear.json`, `Items/ReferenceGear.cs`, `tools/BuildOptimizer/rebuild_references.ps1` (порт `tools/BuildOptimizer/rebuild_references.sh` на PowerShell 5.1: те же шаги и аргументы)
- Test: `Items/ReferenceGearTests.cs`, `Content/LootBalanceTests.cs`

**Interfaces:**
- Решение: `data/builds/*.json` **не носят** эталонную экипировку (только мигрированное белое оружие). Приемка и `BuildOptimizer` надевают `ReferenceGear.For` во время прогона; `BuildOptimizer suite` получает флаг `--gear <rank>` (без флага — как раньше, это и сравнивается с `baseline.txt` в задачах 5 и 11). — 10 желтых уровня `min(level, 60)` (ранги 6–15: `60 + rank − 5`), 3 аффикса с пола со средним броском из списка «нужных» аффиксов билда в `reference_gear.json`, без звездочек, без куба, без «+уровня скилла»; мастерворк 4 / 8 / 12 на рангах 1–5 / 6–9 / 10–15, крупные бонусы — индексам 0, 1, 2; 3 свойства из списка билда в `reference_gear.json` на слотах `Weapon`, `Gloves`, `Amulet`.

- [ ] **Step 1: Write failing tests** (лут 12.3): `Gear_multiplies_damage_1_5_to_2_5` на ранге 5 (МВ 4) и ранге 15 (L 70, МВ 12) для каждого эталона; `Worst_case_more_powers_under_1_55` (10 слотов: 3 самых сильных [×] + 7 самых сильных без More); `Keystone_tests_hold_with_gear` (тесты ядра 15.6 с экипировкой); `Apostate_tests_hold_with_gear` (если эталона Отступника нет — `Skip` с причиной).
- [ ] **Step 2:** FAIL. **Step 3:** реализовать. **Step 4:** PASS. **Step 5:** пересборка эталонов через `tools/BuildOptimizer/rebuild_references.ps1` с `--gear` (~1,5 ч, одна за раз), итоги — в `data/BALANCE_NOTES.md`.
- [ ] **Step 6:** commit `feat(items): reference gear and loot power acceptance`.

---

### Task 9: Экономика лута

**Files:**
- Create: `core/Dialoid.Core.Tests/Content/LootEconomyModel.cs`, `Content/LootEconomyTests.cs`

**Interfaces:**
- Consumes: `LootGenerator.Roll`/`ChestCount`, `ItemCrafting.*`, `ItemMath.AffixValue`, `ReferenceGear.For`.
- Модель: башня = 5 этажей; профили «зачистка» и «босс» со своим временем этажа (данные теста); с монстров 8–12 предметов на этаж, сундук `ChestCount`; ранг N — 20 мин, N − 2 — 20/4 мин (худший случай ×4).
- **Апгрейд** (определение): оценка предмета = сумма значений «нужных» аффиксов эталона из `reference_gear.json`, нормированных на верхнюю границу уровня. Находка — кандидат, если у нее ≥ 2 нужных аффикса с пола; кандидат доводится: перековка третьего на нужный (ожидаемое число перековок до нужного аффикса по весам пула слота, цена каждой ×1,5 включается в стоимость), куб нужной категории (средний бросок), МВ 8. Апгрейд — доведенный кандидат с оценкой выше надетого; надетое обновляется по ходу цепочки башен (старт — эталонная экипировка ранга).

- [ ] **Step 1: Write failing tests** (лут 7.3, 12.3.4, 12.3.5, 12.3.7): `Ranks_1_to_5_upgrade_every_1_to_2_floors`; `Ranks_1_to_5_one_or_two_legendaries_per_tower`; `Upgrade_rate_ranks_6_to_14` (раз в 1–2 башни); `Upgrade_rate_rank_15` (раз в 3–5); `Masterwork_8_with_refund_costs_at_most_one_tower_income`; `Materials_per_hour_max_on_own_rank` (каждый материал отдельно, N против N − 2 при ×4 скорости); `Monoculture_at_most_15_percent` (легендарки и звездочки в час, «Боссы ×5» против «разные типы», оба профиля); `Income_to_masterwork_8_cost_stable_ranks_6_to_15` (отношение дохода башни к цене МВ 8 на своем ранге отличается не больше чем на 25% между рангами 6 и 15); `Stock_after_100_hours_bounded` (симуляция 100 часов с доводкой по модели: запас каждого материала не больше расхода 10 башен своего ранга).
- [ ] **Step 2:** FAIL. **Step 3:** при провале править только данные (`loot_tables.json`, `crafting.json`: первым шаг МВ, затем рост диапазона 61–70, затем `material_rank_growth`); изменение формул — вопрос «Управлению проектом». **Step 4:** PASS. Фактическое соотношение предметов пол/сундук записать в `BALANCE_NOTES.md` (спека: ≈2/3 и 1/3).
- [ ] **Step 5:** commit `test(items): loot economy acceptance`.

---

### Task 10: Данные врагов

**Files:**
- Create: `data/monsters.json`, `data/monster_affixes.json`, `data/bosses.json`, `data/difficulty.json`, `Enemies/EnemyData.cs`, `Enemies/EnemyDataLoader.cs`, `Enemies/EnemyRules.cs`, `Data/Json/Monster*.cs`
- Modify: `Data/DataLoader.cs`, `Data/GameData.cs`, `Data/Validator.cs`
- Test: `Enemies/EnemyDataTests.cs`, `Enemies/EnemyRulesTests.cs`

**Interfaces:**
- Produces:
  - `public enum MonsterRole { Brute, Shooter, Caster, Swarm, Bomber, Pusher, Hunter, Leader }`; `public enum AttackShape { Melee, Cone, Line, Circle, Projectile, Ring }`; `public enum Behavior { Chaser, Kiter, CasterSupport, Bomber, Pusher, Hunter, BossScript }`
  - `public sealed record AttackDef(string Id, AttackShape Shape, IReadOnlyDictionary<DamageType, double> Damage, double Power, double? TelegraphBase, double Cooldown, double Range, double Radius, double Knockback)`
  - `public sealed record MonsterDef(string Id, string Name, string Family, MonsterRole Role, IReadOnlyList<string> Tags, double Life, double Armor, IReadOnlyDictionary<DamageType, double> ResistOffsets, double MoveSpeed, IReadOnlyList<AttackDef> Attacks, Behavior Behavior, IReadOnlyDictionary<string, double> BehaviorParams, string? OnDeathRule, int IntroRank, IReadOnlyList<RankOverride> RankOverrides)`; `RankOverride(int FromRank, double? Armor, string? AddsRule)` (Рыцарь: броня ×1,5 и аура с ранга 2–3)
  - `public sealed record FamilyDef(string Id, IReadOnlyList<PackEntry> Pack, string? LeaderId)`; `PackEntry(string MonsterId, int Min, int Max)`
  - `public sealed record MonsterAffixDef(string Id, string Name, string Group, int IntroRank, bool BossAllowed, IReadOnlyDictionary<string, double> Params, IReadOnlyDictionary<string, double> BossParams)` (числа аффикса — каталог врагов 9.2; `BossParams` — например «Огненное зачарование» без взрыва)
  - `ForbiddenPairs: IReadOnlyList<(string A, string B)>`; id мода башни — с префиксом `tower:` (проверяется планом башни, здесь не ищется)
  - `public sealed record BossPhase(double FromLifePct, IReadOnlyList<AttackDef> Attacks, double WindowSec, double WindowEverySec, double WindowDamageTaken, bool OnlyUber, double AddsLifeShare)` (`AddsLifeShare` — опоры Баала 3 × 0,08)
  - `public sealed record BossDef(string Id, string Name, double Life, IReadOnlyList<BossPhase> Phases, bool Tutorial, int IntroRank, int? UberRank)`
  - `public sealed record DifficultyDef(IReadOnlyList<double> S, double MExponent, double BaseLife, double BaseDamage, IReadOnlyList<RankDefense> Defense, IReadOnlyList<int> ElitesAffixCount, IReadOnlyList<double> TelegraphFloor, double FasterModFloor, double SpeedCap, double TelegraphDodgedShare, IReadOnlyDictionary<string, double> RosterWeights, double MaxTierDamageMod, WallCriteria Wall)`; `RankDefense(int FromRank, double BaseResist, double OffsetBand, double ArmorRef)`; `WallCriteria(double WallTtkSec, double OkTtkSec, double OkSurvivalSec)`
  - `EnemyRules.Power(int rank, int zoneLevel, DifficultyDef d) → WeaponPower(zoneLevel)^MExponent × S[rank − 1]`
  - `EnemyRules.EffectiveTelegraph(AttackDef a, int rank, double modMultiplier, DifficultyDef d) → max(base × max(modMultiplier, 0,7), floor(rank))` (скорость атаки не параметр)
  - `EnemyRules.EffectiveMoveSpeed(MonsterDef m, IEnumerable<double> bonusesIncreased, DifficultyDef d) → min(base × (1 + Σ), 1,1)`
  - `EnemyRules.Resist(MonsterDef m, DamageType t, int rank, bool boss, DifficultyDef d)` — базовое ранга + смещение вида, зажатое в полосу ранга (на 1–2 — 0), босс +10 без смещения, кап 75.
- Проверки загрузчика (враги 13.2): ссылки семейство ↔ вид ↔ вожак, аффиксы, пары (кроме `tower:`), белый список боссов; перечисления; ключ `dark` только при `DamageType.Dark`; |смещение| ≤ 20, броня вида 0,75–1,5; `TelegraphBase ≥ 0,5`; `MoveSpeed ≤ 1,1`; убер-босс без `Tutorial`: ≥ 2 фазы, ≥ 1 `OnlyUber`, окно в каждой фазе; `S` длины 15.

- [ ] **Step 1: Write failing tests**: `Real_enemy_data_loads`; `S_table_matches_spec` (1 → 1,0; 5 → 2,4; 15 → 22,5); `Resist_offsets_clamped_by_rank` (на ранге 1 смещение 0 при любом смещении вида; на ранге 3 смещение = смещение вида из данных, зажатое в ±10; босс ранга 4 → базовое + 10, без смещения); `Telegraph_floor_and_mod` (база 1,0 на ранге 1 → 1,5; мод 0,5 → считается как 0,7); `Attack_speed_does_not_shorten_telegraph`; `Move_speed_capped_worst_case` (рубака + ярость ×3 + «Быстрый» + аура → 1,1); `Elite_affix_count_by_rank`; `Tutorial_boss_exempt`; сломанные копии: `Dark_key_without_dark_type_rejected`, `Unknown_affix_in_pair_rejected` (а `tower:x` принят), `Boss_with_summoner_rejected`, `Uber_without_only_uber_rejected`.
- [ ] **Step 2:** FAIL. **Step 3:** реализовать. **Step 4:** PASS.
- [ ] **Step 5:** commit `feat(enemies): monster, boss and difficulty data`.

---

### Task 11: Стая и босс в симуляции (этап 1)

**Files:**
- Create: `Enemies/EnemyPacks.cs`
- Modify: `Simulation/SimConfig.cs` (+ `DummyOverride: DummyDefinition?`, `PackDamageTaken: double`), `Simulation/FightSetup.cs` (готовый манекен вместо `data.Dummies[DummyId]`), `Simulation/CombatEngine.cs` (`SetDamageTaken("pack", ...)` при старте и после каждого `Reset()`), `Explain/*` (строки «уклонено телеграфов: X%» и «получаемый урон пака»)
- Test: `Enemies/EnemyPackTests.cs`

**Interfaces:**
- Consumes: `EnemyRules.*`, `SimConfig.EnemyPower`.
- Produces:
  - `public enum EliteKind { None, Champion, RareLeader }` — множители врагов 9.1 и заморозка 1,0 / 0,75 / 0,5. `Champion` — весь пак ×2,5/×1,3; `RareLeader` — ×5/×1,5 получает только вожак, и в среднее он входит со своим весом жизни.
  - `public sealed record PackSetup(DummyDefinition Dummy, double EnemyPower, double PackDamageTaken, double TelegraphDodgedShare)`
  - `EnemyPacks.Stack(string familyId, int rank, int zoneLevel, EliteKind elite, bool withLeader, GameData data) → PackSetup` — однородный «средний монстр» (враги 13.3): жизнь, броня и сопротивления — средние с весом жизни по составу (с вожаком, если `withLeader`); размер пака — средний размер стаи (+1 с вожаком); атака — средний урон в секунду ролей, где телеграфные атаки × (1 − `TelegraphDodgedShare`), сведенный к одному удару в 1 с.
  - `EnemyPacks.Boss(string bossId, int rank, int zoneLevel, bool uber, GameData data) → PackSetup` (согласовано с Эндгеймом: `TowerSpec.UberBossId` на вершине → `uber = true`, этаж «Боссы» с тем же id → `uber = false`) — жизнь × (1 + Σ `AddsLifeShare`) × (2 у убер); поток атак — средний по фазам × 1,25 у убер; окно — `PackDamageTaken` = `WindowDamageTaken × WindowSec / WindowEverySec`; заморозка 0,5; сопротивления — базовое ранга + 10.
  - Баффы стай (клич, ярость, аура) на этапе 1 не моделируются; задача 12 добавляет их худшим случаем в тестах.

- [ ] **Step 1: Write failing tests**: `Presets_unchanged_without_override` (все пресеты `dummies.json`: DPS и выживание эталонов как в `baseline.txt`); `Horned_pack_weighted_average` (с вожаком и без); `Rank_1_2_pack_has_no_offsets`; `Champion_and_leader_multipliers_and_freeze`; `Pack_damage_taken_sums_with_shock` (шок 10% + окно 25% → 35%); `Pack_damage_taken_survives_reset`; `Boss_life_includes_pillars_and_uber_double`; `Explain_shows_dodged_telegraph_row`.
- [ ] **Step 2:** FAIL. **Step 3:** реализовать. **Step 4:** PASS; `suite --all` без `--gear` совпадает с `baseline.txt`.
- [ ] **Step 5:** commit `feat(enemies): monster packs and bosses as dummies`.

---

### Task 12: Первый контент и приемка врагов

**Files:**
- Modify: `data/monsters.json`, `data/bosses.json`, `data/monster_affixes.json`, `data/difficulty.json`
- Test: `Content/EnemyBalanceTests.cs`

**Interfaces:**
- Consumes: `EnemyPacks.*`, `EnemyRules.*`, `ReferenceGear.For`, эталонные билды.
- Содержимое (числа — враги 11, точно): `horned_brute`, `horned_shaman`, семейство `horned` (4–6 рубак + 1 шаман, вожак `hell_knight`), `hell_knight`, босс `baal` (`IntroRank 4`, `UberRank 5`, опоры 3 × 8%); 13 аффиксов каталога 9.2 с числами и запрещенные пары (пара с `tower:icy_trail`).
- **Отложено (не в этом плане, записать в README раздела врагов):** второе семейство ранга 1 (условие «Волн»), особые монстры Забега, учебные боссы (контент), пул боссов ≥ 6, возврат боссов на высокие ранги (+1 аффикс за 3 ранга, редкий монстр ×0,4), свита вожака с половиной аффикса, правила стай в симуляции (этап 2), правило ростера на рангах 3–15 (ждет второе семейство).
- **Критерии стены** (`difficulty.json`, `WallCriteria`): «упирается» — средний эталон на нижней границе уровней ранга N (ранги 1–5: 1, 12, 24, 36, 48; 6–15: 60 с эталонной экипировкой ранга N − 1) убивает стаю дольше `WallTtkSec` или выживает меньше `OkSurvivalSec`; «проходит» — `≤ OkTtkSec` и `≥ OkSurvivalSec`.

- [ ] **Step 1: Write failing tests** (враги 13.4, режим «среднее»):
  - `Class_fairness_all_ranks` — ранги 1–15: эталоны Воителя, Чародея, Отступника убивают `Stack("horned", rank, …, withLeader: true)` за время в пределах ±25% от среднего и выживают ≥ `OkSurvivalSec`.
  - `Executioner_survives_at_40_percent` — худший всплеск без телеграфа за 1 с: 6 рубак + 1 шаман (клич +15% Increased), редкий вожак ×1,5 More со свитой, пачка из 3 чемпионов-рубак ×1,3 More, «Быстрый» +20% к частоте ударов, мод `MaxTierDamageMod`; удар скипетром Баала ×1,25 у убер-версии — отдельная строка худшего одиночного удара; урон после защиты эталона против своего типа < 40% жизни эталона Палача (нет эталона — `Skip`).
  - `No_untelegraphed_hit_over_35_percent_ehp` — все ранги, все атаки без телеграфа, со всеми множителями выше.
  - `Roster_resists_balanced` — средние по 6 типам с `RosterWeights` ±5 п.п., броня переведена в эквивалент по формуле ядра 5.4 для эталонного удара. Сейчас ростер на рангах 3–15 — одно семейство, и правило ростера (спека 6.2) на одном семействе невыполнимо без нулевых смещений. Поэтому тест проверяет ранги 1–2, а ранги 3–15 — `Skip = "ростер из одного семейства, ждет второе семейство"`; это записано в отложенном.
  - `Armor_equivalent_at_most_5_percent_ranks_1_2`.
  - `Rank_wall` — ранги 2–15 (ранг 1 — вход в игру, не стена): на входе в ранг N эталон «упирается», через 3–5 уровней (2–5) или 1–2 шага эталонной экипировки (6–15) «проходит».
  - `Boss_ttk` — этажный Баал 45–90 с эталоном середины полосы ранга 4 (уровень 42, `ReferenceGear.For(build, 42, 4)`), с опорами; убер-Баал: эталон нижней границы ранга 5 (уровень 48, `ReferenceGear.For(build, 48, 5)`) выживает дольше, чем убивает его.
- [ ] **Step 2:** зафиксировать `WallCriteria` и `OkSurvivalSec` в `difficulty.json` **до** калибровки (стартово: `WallTtkSec 30`, `OkTtkSec 15`, `OkSurvivalSec 20`) и закоммитить их отдельно. **Step 3:** FAIL. **Step 4:** калибровать только `MExponent`, `BaseLife`, `BaseDamage`, `TelegraphDodgedShare`, смещения видов (в полосе ранга) и `RosterWeights`; `S`, `WallCriteria` и формулы не трогать (иначе вопрос «Управлению проектом»). **Step 5:** PASS.
- [ ] **Step 6:** commit `feat(enemies): horned pack, hell knight, baal; enemy acceptance`.

---

### Task 13: Песочница и документация

**Files:**
- Modify: `core/Dialoid.Sandbox/SandboxSession.cs`, `README.md`, `data/BALANCE_NOTES.md`, `docs/data-authoring-reference.md`
- Test: `core/Dialoid.Sandbox.Tests/ItemCommandsTests.cs`

**Interfaces:**
- Produces: `SandboxSession.Equip(EquipSlot, ItemInstance) → CommandResult`, `Unequip(EquipSlot)`, `GenerateLoot(LootRequest, int seed) → LootDrop`, `Reforge(EquipSlot, int affixIndex, int optionIndex, int seed)`, `Cube(EquipSlot, AffixCategory, int seed)`, `Masterwork(EquipSlot, int rank, int seed)` (возвращают `CommandResult` со стоимостью; кошелька нет — этап 2), `UseReferenceGear(int rank)`.

- [ ] **Step 1: Write failing tests**: `Equip_refuses_too_high_level`; `Generate_then_equip_changes_dps`; `Craft_on_equipped_item_recompiles`; `Reference_gear_command`; `Explain_affix_chain_visible`.
- [ ] **Step 2:** FAIL. **Step 3:** реализовать; документация: новые файлы данных, проверки, этапы 1–2, отложенное из задачи 12. **Step 4:** весь `dotnet test`, `dotnet build game`, `tools/godot_check.ps1` зеленые.
- [ ] **Step 5:** commit `feat(sandbox): item commands; docs for loot and enemies data`.
