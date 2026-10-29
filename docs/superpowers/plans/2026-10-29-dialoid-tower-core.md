# Dialoid Tower Core Implementation Plan (план 5)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Логика Башни в ядре без мира: данные башни, ранги, кошелек валюты с уровнями, сборка и перемешивание, намазка, качество награды и выпадение валюты, правила забега (смерть, выход, вершина, открытие ранга) и тесты двух уравнений экономики.

**Architecture:** Новый модуль `Dialoid.Core.Tower` со своим загрузчиком `TowerDataLoader` для каталогов `data/tower/*.json`. `DataLoader` и `GameData` не меняются: их правят сессии живого боя и лута. Модуль переиспользует `DataLoader.ReadCatalog` и `DataLoader.Options` (internal, та же сборка), `StrictJson`, `DataError` и `IRandomSource`. Все операции детерминированы: состояние + случайность → новое состояние или отказ с русским текстом. Активностей этажей (таймер, волны, ИИ) нет: этаж здесь «пройден» или «проигран» по команде теста.

**Tech Stack:** .NET 8 (`net8.0`, RollForward Major), C# 12, xUnit. Godot не участвует.

**Spec:** `docs/superpowers/specs/2026-10-29-dialoid-endgame-tower-design.md` (утверждена 2026-10-29). Основа: `docs/superpowers/specs/2026-10-25-dialoid-core-systems-design.md` (§3.3, §4). Связи: `2026-10-29-dialoid-loot-design.md` (§7.2, §7.4, §9), `2026-10-29-dialoid-enemies-design.md`.

## Global Constraints

- `core/` собирается с `TreatWarningsAsErrors` и `Nullable enable`; `dotnet test core/Dialoid.sln` зеленый, 0 предупреждений. Существующие тесты остаются зелеными без изменения утверждений.
- `DataLoader`, `GameData`, `Validator`, `Simulation/*`, `Sandbox`, `game/` **не меняются**. Новый код — только `core/Dialoid.Core/Tower/`, тесты — `core/Dialoid.Core.Tests/Tower/`, данные — `data/tower/`. Из общих файлов меняются только `core/Dialoid.Core.Tests/Fixtures/FixturePaths.cs` (новый помощник) и, в последнем коммите, `README.md`, `data/BALANCE_NOTES.md`, `docs/data-authoring-reference.md`.
- Данные строгие: неизвестное поле — ошибка, сообщения — русские предложения в `DataError`.
- **Детерминизм.** Модуль работает только с `RandomSources.Create(SimMode.Roll, seed)`. Порядок обхода — порядок записей в файле (списки, не словари). Выбор из n вариантов — `TowerRandom.Pick(rng, n)`. Каналы `Chance`: `"tower.bias"`, `"tower.drop.<currencyId>"`.
- **Индексы этажей в API — с нуля (0–4), в текстах для игрока — с единицы.**
- Числа из спеки (копировать точно):
  - этажей **5**; свойств на этаж **до 5**, на башню **до 3**;
  - 15 рангов; полосы 1–12, 12–24, 24–36, 36–48, 48–60; ранги 6–15: уровень зоны `60 + (ранг − 5)`;
  - штраф опыта: −10% за каждый уровень сверх верхней границы полосы, не больше −50%; «ниже своего» — верхняя граница **строго меньше** уровня персонажа;
  - макс. тир мода: ранги 1–2 → 1, 3–4 → 2, 5–7 → 3, 8–11 → 4, 12–15 → 5;
  - виды модов: общие с ранга 1, под тип этажа с 3, на башню с 4;
  - ранги выпадения = ранги доступа (спека 8.3): `imbue`, `clear`, `shuffle`, `reroll`, `reroll_variant` — 1; `imbue_type`, `upgrade` — 3; `imbue_tower`, `biased_rare_*` — 4; `remove` — 5; `biased_epic_*` — 8; `bias_value` — 10;
  - **уровень есть только** у `imbue`, `imbue_type`, `imbue_tower`, `upgrade`, `biased_*`, `bias_value`;
  - цена перемешивания внутри одной сборки: 0, 1, 2, 4, 8, … (первое бесплатно);
  - обмен валюты вверх только на соседний уровень, 4:1; **валюта уровня 1 не обменивается** (спека: «валюта ранга 1 на ранг 2 не переезжает»);
  - уравнение 1: ранг 1 ≥ 100% полной намазки, ранги 2–15 — 60–80%, голая башня ≥ 40%;
  - уравнение 2: применимая к рангу N валюта в час максимальна при фарме ранга N, даже если ранги ниже проходятся в 3,5 раза быстрее;
  - намазка **не** увеличивает количество валюты; чем выше ранг, тем больше валюты (`drop_growth > 1`).
- Тексты для игрока — по-русски, идентификаторы — по-английски. Коммиты — conventional commits, в конце `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Ветка `feat/tower-core`, worktree `../dialoid-worktrees/tower-core`.

## Review Focus

1. **Намазка сверх лимита или не туда** (6-е свойство этажа, 4-е башни, `imbue_tower` на этаж, `imbue` на башню, мод под тип не своего типа, второй мод группы). Ожидание: отказ с причиной, кошелек и сборка не тронуты. Тесты в задаче 5.
2. **Валюта низкого уровня на высоком ранге.** Уровень L применим только к рангу ≤ L; тратится самый низкий подходящий; при нехватке — отказ без частичного списания; уровень 1 не обменивается. Тесты в задаче 3.
3. **«Перемешал, поправил слот, вернул как было».** Бонус потерян для этой сборки, цена следующего перемешивания продолжает расти. Тест в задаче 4.
4. **Смерть на вершине; выход до вершины; убер убит раньше, чем набран уровень.** Награды этажей сохранены; без вершины ранг не открывается; ранг открывается позже, когда уровень набран. Тесты в задаче 7.
5. **Детерминизм.** Один сид и одни действия → одинаковые моды, числа и выпадения. Тесты в задачах 5 и 6.

---

## Структура файлов

```
data/tower/rules.json            объект: этажи, слоты, ранги, качество, бонусы, обмен, цены перемешивания
data/tower/floor_types.json      типы этажей и разновидности Испытаний
data/tower/biomes.json           биомы
data/tower/floor_mods.json       свойства
data/tower/currencies.json       валюта
core/Dialoid.Core/Tower/TowerData.cs         записи определений + неизменяемые каталоги
core/Dialoid.Core/Tower/TowerDataLoader.cs   загрузка и проверка
core/Dialoid.Core/Tower/Json/*.cs            JSON-классы с CheckFields и чтец объекта rules.json
core/Dialoid.Core/Tower/TowerRandom.cs       Pick
core/Dialoid.Core/Tower/RankRules.cs
core/Dialoid.Core/Tower/Wallet.cs
core/Dialoid.Core/Tower/TowerSpec.cs         TowerSpec, FloorSpec, ModInstance, TowerCodec
core/Dialoid.Core/Tower/TowerRefusal.cs      причины отказа + TowerTexts
core/Dialoid.Core/Tower/TowerAssembly.cs
core/Dialoid.Core/Tower/Crafting.cs
core/Dialoid.Core/Tower/Rewards.cs
core/Dialoid.Core/Tower/TowerRun.cs          TowerRun, TowerProgress
core/Dialoid.Core.Tests/Tower/*Tests.cs
core/Dialoid.Core.Tests/Fixtures/tower/      маленький набор данных башни
```

---

### Task 1: Данные башни и загрузчик

**Files:**
- Create: `data/tower/*.json`, `core/Dialoid.Core/Tower/TowerData.cs`, `TowerDataLoader.cs`, `TowerRandom.cs`, `Tower/Json/*.cs`
- Create: `core/Dialoid.Core.Tests/Fixtures/tower/*.json`, `core/Dialoid.Core.Tests/Tower/TowerDataLoaderTests.cs`
- Modify: `core/Dialoid.Core.Tests/Fixtures/FixturePaths.cs` — `public static string TowerFixture` и `public static string CopyTowerFixture()` по образцу `CopyTiny`; `RealTowerData => Path.Combine(RealData, "tower")`.

**Interfaces:**
- Consumes: `DataLoader.ReadCatalog<T>` (массивы с `id`), `DataLoader.Options`, `StrictJson.RejectUnknown/RejectUnknownIn`, `DataError`.
- Produces:
  - `public enum ModKind { General, FloorType, Tower }`
  - `public enum HardCounter { None, NoLifeRegen, NoResourceRegen, NoHealing }` (JSON: `null`, `no_life_regen`, `no_resource_regen`, `no_healing`)
  - `public enum CurrencyOp { AddMod, AddModBiased, RerollOne, RerollValueHigh, UpgradeTier, Clear, RemoveOne, RerollVariant, Shuffle }`
  - `public sealed record FloorTypeDef(string Id, string Name, bool HasEndBoss, IReadOnlyList<string> Variants)` — `Variants` не пуст только у `trial`.
  - `public sealed record ModTier(int Tier, double Min, double Max, double Quality)` — `0 ≤ Min ≤ Max`, `Quality > 0`. Число всегда неотрицательно, направление («меньше времени») задано текстом мода.
  - `public sealed record FloorModDef(string Id, string Name, ModKind Kind, IReadOnlyList<string> FloorTypes, string? Variant, string? Group, IReadOnlyList<string> Directions, IReadOnlyList<string> StyleTags, HardCounter HardCounter, bool AffectsSummit, IReadOnlyList<ModTier> Tiers)`
    - `FloorTypes` пуст = совместим со всеми типами. Для `FloorType` — ровно один тип; если это `trial`, `Variant` обязателен.
    - `Group` — группа несовместимости (на этаже или на башне не больше одного мода группы).
    - `Directions` — направления для направленной намазки (`waves`, `elite`, `reward`, `density`…), отдельная ось от `Group`.
    - Для мода башни совместимость с ареной вершины = `FloorTypes` пуст или содержит `bosses`.
  - `public sealed record CurrencyDef(string Id, string Name, CurrencyOp Op, ModKind? ModKind, bool HasLevel, int DropsFromRank, double DropBase, double DropGrowth, double SummitMultiplier, string? BiasDirection, double BiasChance)`
  - `public sealed record RankDef(int Rank, int LevelMin, int LevelMax, int ZoneLevel, IReadOnlySet<ModKind> ModKinds, int MaxTier)` — для 1–5 `ZoneLevel = 0` (вычисляется), для 6–15 — `60 + (rank − 5)`.
  - `public sealed record TowerRules(int Floors, int FloorSlots, int TowerSlots, IReadOnlyList<double> HeightMultipliers, double SummitBonus, double DiminishingC, double DiversityBonusPerType, int DiversityBonusTypesCap, double ShuffleBonus, double NoDeathBonus, double QualityCap, IReadOnlyList<int> ShuffleCosts, int ExchangeRatio, int ExchangeMinLevel, double XpPenaltyPerLevel, double XpPenaltyCap, IReadOnlyList<RankDef> Ranks)`
  - `public sealed class TowerData` — `Rules`; списки в порядке файла `FloorTypes`, `Biomes` (`IReadOnlyList<string>`), `Mods`, `Currencies`; словари для поиска `FloorType(id)`, `Mod(id)`, `Currency(id)`.
  - `public static class TowerDataLoader { public static (TowerData? Data, IReadOnlyList<DataError> Errors) Load(string towerDirectory) }`
  - `internal static class TowerRandom { public static int Pick(IRandomSource rng, int n) => Math.Min(n - 1, (int)Math.Floor(rng.InRange(0, n))); }`
- `rules.json` — объект, а `ReadCatalog` читает только массивы, а `ReadObjectFile` в `DataLoader` закрыт. Поэтому в `Tower/Json/RulesJson.cs` свой чтец: `JsonDocument` → `StrictJson.RejectUnknown` для корня, `RejectUnknownIn` для `ranks` → `Deserialize<RulesJson>(DataLoader.Options)`.

Стартовое содержимое (**[Решение Claude]**, цифры к балансу):
- типы: `timed_run` (HasEndBoss), `trial` (разновидности `escort`, `hold`, `ritual`, `levers`), `clear`, `waves`, `bosses` (HasEndBoss); биомы `crypt`, `swamp`, `forge`, `ruins`.
- моды: 10 общих, из них три с жестким контром (`no_life_regen`, `no_resource_regen`, `no_healing`, у них `min = max = 0`); по 2 под тип для `timed_run`, `clear`, `waves`, `bosses` и по 2 под каждую разновидность `trial`; 4 мода башни (один `affects_summit` с группой `gate`). Группы: `timer`, `waves`, `gate`, `density`. Направления: `waves`, `elite`, `reward`, `density`. У каждого мода тиры 1–5.
- валюта: `imbue` (AddMod, General), `imbue_type` (AddMod, FloorType), `imbue_tower` (AddMod, Tower), `clear`, `shuffle`, `reroll`, `reroll_variant`, `upgrade`, `remove`, `biased_rare_elite` и `biased_rare_reward` (AddModBiased, General, 0,5), `biased_epic_elite` и `biased_epic_reward` (0,9), `bias_value` (RerollValueHigh). Ранги и `HasLevel` — по Global Constraints.
- `rules.json`: `floors 5`, `floor_slots 5`, `tower_slots 3`, `height_multipliers [1.0, 1.1, 1.2, 1.35, 1.5]`, `summit_bonus 3.0`, `diminishing_c 2.0`, `diversity_bonus_per_type 0.03`, `diversity_bonus_types_cap 2`, `shuffle_bonus 0.05`, `no_death_bonus 0.03`, `quality_cap 20`, `shuffle_costs [0, 1, 2, 4, 8, 16]`, `exchange_ratio 4`, `exchange_min_level 2`, `xp_penalty_per_level 0.10`, `xp_penalty_cap 0.5`, 15 рангов.

Проверки загрузчика (каждая — русское сообщение с id): неизвестное поле; неизвестный тип или разновидность в моде; мод `FloorType` не с ровно одним типом; мод под `trial` без `Variant`; `Variant` у типа без разновидностей; тиры не 1..5 по порядку, `Min > Max`, `Min < 0`, `Quality ≤ 0`; жесткий контр у мода `Tower`; `affects_summit` не у мода `Tower`; `AddMod*` без `ModKind`; `AddModBiased` без `BiasDirection` или с `BiasChance` вне (0, 1); `DropsFromRank` вне 1–15; `DropGrowth ≤ 1` у валюты с уровнем; `HasLevel` у валюты с операцией `Clear`, `RemoveOne`, `RerollOne`, `RerollVariant`, `Shuffle`; рангов не 15 или полосы не стыкуются; `HeightMultipliers` не длины `Floors`.

- [ ] **Step 1: Write failing tests** `TowerDataLoaderTests`:
  - `Real_tower_data_loads_without_errors` — `Load(FixturePaths.RealTowerData)`: ошибок нет, 5 типов, 15 рангов, `Floors 5`, `FloorSlots 5`, `TowerSlots 3`.
  - `Rank_table_matches_spec` — ранг 2: `LevelMin 12, LevelMax 24, MaxTier 1`, виды `{General}`; ранг 4: `{General, FloorType, Tower}`, `MaxTier 2`; ранг 8: `MaxTier 4`, `ZoneLevel 63`; ранг 15: `MaxTier 5`, `ZoneLevel 70`.
  - `Currency_levels_and_ranks_match_spec` — `HasLevel` ровно у списка из Global Constraints; `DropsFromRank` по тому же списку.
  - `Mods_keep_file_order` — `Mods[0].Id` равен первому id в `floor_mods.json`.
  - по одному тесту на сломанную копию `CopyTowerFixture()`: `Unknown_field_is_rejected`, `Hard_counter_on_tower_mod_is_rejected`, `Floor_type_mod_needs_exactly_one_type`, `Trial_mod_needs_variant`, `Tiers_must_be_1_to_5_in_order`, `Rank_bands_must_join`, `Levelled_currency_must_grow`.
- [ ] **Step 2:** `dotnet test core/Dialoid.sln --filter TowerDataLoaderTests` → FAIL.
- [ ] **Step 3:** реализовать.
- [ ] **Step 4:** PASS, весь `dotnet test` зеленый.
- [ ] **Step 5:** commit `feat(tower): tower data catalogs and loader`.

---

### Task 2: Ранги

**Files:** Create `Tower/RankRules.cs`; test `Tower/RankRulesTests.cs`

**Interfaces:**
- Produces: `public static class RankRules`:
  - `int ZoneLevel(TowerRules r, int rank, int characterLevel)` — ранги 1–5: `Math.Clamp(characterLevel, LevelMin, LevelMax)`; 6–15: `RankDef.ZoneLevel`.
  - `double XpMultiplier(TowerRules r, int rank, int characterLevel)` — `1 − min(cap, perLevel × max(0, characterLevel − LevelMax))`; на рангах 6–15 всегда 1.
  - `bool CanApply(TowerRules r, int rank, ModKind kind, int tier)`.
  - `bool LevelAllows(TowerRules r, int rank, int characterLevel)` — `characterLevel ≥ LevelMin(rank)`.

- [ ] **Step 1: tests:** `ZoneLevel(2, 5) == 12`, `(2, 18) == 18`, `(2, 30) == 24`, `(9, 60) == 64`; `XpMultiplier(2, 24) == 1.0`, `(2, 26) ≈ 0.8`, `(2, 40) == 0.5`, `(7, 60) == 1.0`; `CanApply(2, FloorType, 1) == false`, `(3, FloorType, 2) == true`, `(3, General, 3) == false`, `(4, Tower, 1) == true`; `LevelAllows(3, 23) == false`, `(3, 24) == true`.
- [ ] **Step 2–4.** **Step 5:** commit `feat(tower): rank rules`.

---

### Task 3: Кошелек с уровнями

**Files:** Create `Tower/Wallet.cs`; test `Tower/WalletTests.cs`

**Interfaces:**
- Produces: `public sealed class Wallet`:
  - `Wallet(TowerData data)`
  - `void Add(string currencyId, int level, int count)` — у валюты без уровня уровень хранится как 0.
  - `int CountUsable(string currencyId, int rank)` — сумма уровней ≥ `rank` (без уровня — всё).
  - `bool TrySpend(string currencyId, int rank, int count)` — с самого **низкого** подходящего уровня; иначе ничего не списывает.
  - `bool TryExchangeUp(string currencyId, int fromLevel)` — `ExchangeRatio` единиц уровня L → 1 единица L+1; только для валюты с уровнем, `ExchangeMinLevel ≤ L < 15`.
  - `IReadOnlyDictionary<int, int> Levels(string currencyId)`.

- [ ] **Step 1: tests:** `Spends_lowest_usable_level_first` (уровни 3×2 и 5×2, ранг 3, тратим 3 → остается 5×1); `Low_level_is_unusable_on_higher_rank` (уровень 5 на ранге 6 → 0); `Spend_is_all_or_nothing`; `Exchange_up_is_4_to_1_neighbor_only`; `Level_1_cannot_be_exchanged`; `Levelless_currency_works_on_any_rank` (`clear`).
- [ ] **Step 2–4.** **Step 5:** commit `feat(tower): wallet with currency levels`.

---

### Task 4: Описание башни, сборка и перемешивание

**Files:** Create `Tower/TowerSpec.cs`, `Tower/TowerRefusal.cs`, `Tower/TowerAssembly.cs`; tests `Tower/TowerAssemblyTests.cs`, `Tower/TowerCodecTests.cs`

**Interfaces:**
- Consumes: `Wallet` (задача 3), `TowerRandom.Pick`.
- Produces:
  - `public sealed record ModInstance(string ModId, int Tier, double Value)`
  - `public sealed record FloorSpec(string? TypeId, string? Variant, string? BiomeId, IReadOnlyList<ModInstance> Mods)` — `null` = слот пуст.
  - `public sealed record TowerSpec(int Rank, IReadOnlyList<FloorSpec> Floors, IReadOnlyList<ModInstance> TowerMods, string? UberBossId)` — чистые данные, без уровня персонажа и без бонуса перемешивания (пресет переносим). `UberBossId` — свободная строка: пула боссов в данных пока нет.
  - `public static class TowerCodec { string ToJson(TowerSpec); bool TryFromJson(string json, TowerData d, out TowerSpec? spec, out string? error) }` — snake_case, строгие поля, проверка id типов, разновидностей, биомов и модов; тир не выше 5.
  - `public enum TowerRefusal { None, UnknownCurrency, CurrencyLocked, WrongTarget, KindNotAllowed, SlotOutOfRange, UnknownType, EmptySlot, SlotFull, NoMods, PoolEmpty, NotATrial, NothingToUpgrade, ModNotFound, NotEnoughCurrency, RankLocked, ModAboveRank, NoUberBoss }`
  - `public static class TowerTexts { public static string Refusal(TowerRefusal r) }` — русские тексты, у каждого значения свой.
  - `public sealed class TowerAssembly`:
    - `TowerAssembly(TowerData data, int rank, IRandomSource rng)` — 5 пустых слотов, без модов. Единственный источник случайности для сборки и намазки (`internal IRandomSource Rng`).
    - `static TowerAssembly FromSpec(TowerData d, TowerSpec spec, IRandomSource rng)` — для «повторить прошлую башню» и тестов.
    - `TowerSpec Spec { get; }`, `bool ShuffleBonusActive { get; }`, `int ShufflesDone { get; }`.
    - `TowerRefusal SetFloorType(int slot, string typeId)` — биом, если пуст, выбирается случайно. Разновидность `trial` выпадает при первой постановке в слот и запоминается за слотом: замена на другой тип и возврат `trial` дают ту же разновидность. Смена типа снимает моды под тип и общие моды, несовместимые с новым типом. Если тип реально изменился, `ShuffleBonusActive = false`.
    - `void SetBiome(int slot, string biomeId)` — бонус не трогает.
    - `void SetUberBoss(string id)`.
    - `int NextShuffleCost` — `ShuffleCosts[ShufflesDone]`, после конца списка удвоение последнего.
    - `TowerRefusal Shuffle(Wallet wallet)` — списывает `NextShuffleCost` валюты `shuffle` (при 0 ничего); заполняет все 5 слотов, без двух одинаковых типов подряд; биомы и разновидности бросает заново и **перезаписывает** память слотов; моды этажей снимает, моды башни оставляет; `ShuffleBonusActive = true`, `ShufflesDone++`.
    - `internal` мутаторы для `Crafting`: `SetFloorMods(int slot, IReadOnlyList<ModInstance>)`, `SetTowerMods(...)`, `SetVariant(int slot, string)`. Намазка и смена разновидности бонус перемешивания **не** снимают.

- [ ] **Step 1: tests:**
  - `Shuffle_never_places_same_type_twice_in_a_row` — 200 сидов.
  - `First_shuffle_is_free_then_costs_grow` — цены 0, 1, 2, 4; при нехватке — `NotEnoughCurrency`, `Spec` не меняется.
  - `Manual_edit_after_shuffle_drops_bonus_for_good` — перемешал → `SetFloorType` другой тип → вернул прежний → `ShuffleBonusActive == false`; `SetBiome` бонус не снимает.
  - `Shuffle_keeps_tower_mods_and_clears_floor_mods` (через `FromSpec`).
  - `Trial_variant_is_remembered_by_slot` — `trial` → `clear` → `trial`: та же разновидность.
  - `Changing_type_removes_type_mods` (через `FromSpec` со спекой, где на этаже мод `waves`).
  - `Codec_round_trips_and_rejects_unknown_ids`.
  - `Every_refusal_has_russian_text` — для всех значений `TowerRefusal` текст непустой и разный.
- [ ] **Step 2–4.** **Step 5:** commit `feat(tower): tower spec, assembly and shuffle`.

---

### Task 5: Намазка

**Files:** Create `Tower/Crafting.cs`; test `Tower/CraftingTests.cs`

**Interfaces:**
- Consumes: `TowerAssembly` (+ internal мутаторы и `Rng`), `Wallet`, `RankRules.CanApply`, `TowerRandom.Pick`.
- Produces: `public static class Crafting { public static TowerRefusal Apply(TowerData data, TowerAssembly tower, Wallet wallet, string currencyId, int? floorSlot, string? targetModId = null) }` — `floorSlot == null` = башня. Списание — только при `None`.

Проверки по порядку (первая сработавшая — ответ):

| Операция | Проверки |
|---|---|
| все | `UnknownCurrency`; `CurrencyLocked` (ранг < `DropsFromRank`); `SlotOutOfRange`; `EmptySlot` (тип не задан) |
| `AddMod`, `AddModBiased` | `WrongTarget` (вид `Tower` ↔ цель башня); `KindNotAllowed` (ранг); `SlotFull` (5 / 3); `PoolEmpty`; `NotEnoughCurrency` |
| `RerollOne`, `RerollValueHigh`, `UpgradeTier`, `Clear` | `NoMods`; для `UpgradeTier` — `NothingToUpgrade`; `NotEnoughCurrency` |
| `RemoveOne` | `ModNotFound` (`targetModId` нет у цели); `NotEnoughCurrency` |
| `RerollVariant` | `WrongTarget` (цель башня); `NotATrial`; `NotEnoughCurrency` |

Правила броска:
- **Пул** для этажа: моды нужного вида, совместимые с типом этажа (и с разновидностью для `trial`), с группой, которой еще нет у других модов этого этажа. Для башни: моды `Tower`, группа которых еще не занята модами башни. Группы этажа и башни независимы. Жесткие контры в пул башни не попадают по данным (задача 1).
- Мод выбирается `TowerRandom.Pick` по списку пула в порядке файла; тир — `Pick` из 1..`MaxTier` ранга; число — `rng.InRange(Min, Max)` тира.
- `AddModBiased`: `rng.Chance("tower.bias", BiasChance)`; при успехе пул сужается до модов с `BiasDirection` в `Directions` (если такие есть), иначе обычный пул.
- `RerollOne`: снять случайный мод цели и добавить мод того же вида; группы считаются без снятого мода; новый мод отличается от снятого, если в пуле есть другой.
- `RerollValueHigh`: случайный мод цели получает `Value = rng.InRange((Min + Max) / 2, Max)` своего тира.
- `UpgradeTier`: случайный мод с тиром ниже максимума ранга → тир + 1, число бросается заново в диапазоне нового тира.
- `Clear`: снять все моды цели. `RemoveOne`: снять ровно `targetModId`.
- `RerollVariant`: новая разновидность ≠ прежней; моды под тип снимаются; память слота обновляется.

- [ ] **Step 1: tests:**
  - `Sixth_floor_mod_is_refused_and_wallet_untouched`, `Fourth_tower_mod_is_refused`.
  - `Tower_currency_on_floor_is_wrong_target`, `General_currency_on_tower_is_wrong_target`.
  - `Floor_type_mod_not_allowed_on_rank_2`, `Tower_mod_not_allowed_on_rank_3`, `Remove_locked_before_rank_5`.
  - `Tower_pool_never_contains_hard_counters` (1000 сидов `imbue_tower`).
  - `Group_allows_one_mod_per_floor` (1000 сидов, на этаже не бывает двух модов группы `timer`).
  - `Tier_never_exceeds_rank_max` (ранг 2 → тир 1).
  - `Upgrade_rerolls_value_in_new_tier_range`.
  - `Remove_one_removes_exactly_target`.
  - `Reroll_variant_changes_variant_and_drops_type_mods`.
  - `Biased_currency_hits_direction_about_half` — 2000 сидов `biased_rare_elite` на пустой этаж `clear`: доля модов с направлением `elite` ∈ [0,45; 0,65], при обычном `imbue` < 0,3.
  - `Same_seed_same_result` — одна последовательность сборки и намазки с одним сидом дважды → равный `TowerCodec.ToJson`.
- [ ] **Step 2–4.** **Step 5:** commit `feat(tower): currency operations`.

---

### Task 6: Награды: качество и выпадение валюты

**Files:** Create `Tower/Rewards.cs`; test `Tower/RewardsTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record QualityBreakdown(double Height, double FloorMods, double TowerMods, double Diversity, double Shuffle, double NoDeath, double Total)` — множители для разбора; `Total` — их произведение, не выше `QualityCap`.
  - `public static class Rewards`:
    - `double Diminish(TowerRules r, double x)` = `1 + c·x / (c + x)`, `c = DiminishingC`.
    - `double ModQuality(TowerData d, ModInstance m)` — при `Min == Max` равно `Quality` тира; иначе `Quality × Value / ((Min + Max) / 2)`.
    - `int DistinctTypes(TowerSpec t)` — разновидности Испытания считаются одним типом.
    - `QualityBreakdown FloorQuality(TowerData d, TowerSpec t, int floorIndex, bool noDeaths, bool shuffleBonus)` — `Height = HeightMultipliers[i]`, `FloorMods = Diminish(Σ модов этажа)`, `TowerMods = Diminish(Σ модов башни)`, `Diversity = 1 + DiversityBonusPerType × min(DiversityBonusTypesCap, DistinctTypes − 1)`, `Shuffle = 1 + ShuffleBonus` или 1, `NoDeath = 1 + NoDeathBonus` или 1.
    - `QualityBreakdown SummitQuality(TowerData d, TowerSpec t, bool noDeaths, bool shuffleBonus)` — `Height = SummitBonus`, `FloorMods = 1`, `TowerMods = Diminish(Σ модов башни, совместимых с ареной вершины)`.
    - `double NextModGain(TowerData d, TowerSpec t, int? floorIndex, double addedQuality)` — прирост `Total` от еще одного мода с данным качеством.
    - `IReadOnlyDictionary<string, double> ExpectedCurrencyDrop(TowerData d, int rank, int? floorIndex)` — `floorIndex == null` = вершина. По каждой валюте с `DropsFromRank ≤ rank`: `DropBase × DropGrowth^(rank − 1) × (вершина ? SummitMultiplier : HeightMultipliers[i])`. **Моды не участвуют.**
    - `IReadOnlyList<(string CurrencyId, int Level, int Count)> RollCurrencyDrop(TowerData d, int rank, int? floorIndex, IRandomSource rng)` — целая часть ожидания + `rng.Chance("tower.drop.<id>", дробная часть)`; уровень = `rank` у валюты с уровнем, иначе 0.

- [ ] **Step 1: tests:** `Diminish_is_increasing_and_concave` (x = 0..20 шагом 0,5); `Fifth_mod_still_adds_quality`; `Numberless_mod_quality_is_tier_quality` (`no_healing`); `Floor_mods_do_not_affect_summit`; `Only_arena_compatible_tower_mods_affect_summit`; `Diversity_bonus_caps_at_3_types` (3 и 5 разных типов дают одинаковый `Diversity`; два Испытания с разными разновидностями — один тип); `Breakdown_total_is_product_and_capped`; `Currency_drop_ignores_mods`; `Currency_does_not_drop_before_its_rank` (`imbue_tower` на ранге 3 → 0); `Summit_drops_more_than_any_floor`; `Same_seed_same_drop`.
- [ ] **Step 2–4.** **Step 5:** commit `feat(tower): reward quality and currency drops`.

---

### Task 7: Забег и прогресс

**Files:** Create `Tower/TowerRun.cs`; test `Tower/TowerRunTests.cs`

**Interfaces:**
- Consumes: `TowerAssembly`, `Rewards`, `RankRules`, `Wallet`.
- Produces:
  - `public enum RunStage { Floor, Summit, Won, Exited }`
  - `public sealed record FloorReward(int? FloorIndex, QualityBreakdown Quality, IReadOnlyList<(string CurrencyId, int Level, int Count)> Currency)` — `null` = вершина.
  - `public sealed record ExitLoss(bool LostSummit, IReadOnlyList<int> LostFloors)` — индексы с нуля.
  - `public sealed class TowerProgress`:
    - `int HighestUnlocked { get; }` (сначала 1); `bool UberKilled(int rank)`;
    - `internal void RecordUberKill(int rank)`;
    - `bool TryUnlock(TowerRules r, int characterLevel)` — открывает следующий ранг, пока `UberKilled(HighestUnlocked) && RankRules.LevelAllows(next, level)`; возвращает, открыл ли хоть один.
  - `public sealed class TowerRun`:
    - `static TowerRefusal TryEnter(TowerData d, TowerAssembly assembly, TowerProgress progress, int characterLevel, IRandomSource rng, out TowerRun? run)` — отказы: `RankLocked` (ранг > `HighestUnlocked`), `EmptySlot` (слот без типа), `ModAboveRank` (тир или вид мода не разрешен рангом), `NoUberBoss`. Запоминает `ZoneLevel` и `ShuffleBonusActive` сборки; спека дальше не меняется.
    - `int CurrentFloor` (0–4), `RunStage Stage`, `int ZoneLevel`, `IReadOnlyList<FloorReward> Rewards`, `int Deaths(int? floorIndex)`.
    - `FloorReward WinFloor(Wallet wallet)` — только в `Floor`; награда сразу, валюта сразу в кошелек; после этажа 4 → `Summit`.
    - `void Die()` — в `Floor` и `Summit`: этаж или вершина начинается заново, моды и награды сохраняются, `Deaths++`; бонус «без смертей» пропадает только для этого этажа.
    - `FloorReward WinSummit(Wallet wallet, TowerProgress progress)` — только в `Summit`: `Stage = Won`, `progress.RecordUberKill(rank)`.
    - `ExitLoss PreviewExit()`; `ExitLoss Exit()` — в `Floor` и `Summit`; `Stage = Exited`.
    - Вызов не в своей стадии → `InvalidOperationException`.

- [ ] **Step 1: tests:** `Enter_refuses_empty_slot_locked_rank_and_mod_above_rank`; `Floor_reward_is_granted_immediately`; `Death_restarts_floor_keeps_mods_and_rewards`; `Death_removes_no_death_bonus_only_for_that_floor`; `Exit_before_summit_loses_summit` (`PreviewExit` после 3 этажей: `LostFloors [3, 4]`, `LostSummit`); `Summit_death_keeps_floor_rewards`; `Shuffle_bonus_reaches_rewards`; `Rank_unlocks_after_uber_even_if_level_comes_later` (убер ранга 2 на 22-м уровне → не открыт; `TryUnlock(level 24)` → открыт ранг 3); `Zone_level_fixed_for_the_run`; `Wrong_stage_throws`.
- [ ] **Step 2–4.** **Step 5:** commit `feat(tower): tower run and progress`.

---

### Task 8: Приемка экономики

**Files:** Create `core/Dialoid.Core.Tests/Tower/EconomyAcceptanceTests.cs`; Modify `data/tower/currencies.json` (только нормы выпадения); последний коммит: `README.md` (строка статуса «Башня: логика в ядре»), `data/BALANCE_NOTES.md` (раздел «Башня: экономика»), `docs/data-authoring-reference.md` (раздел `data/tower/`).

**Interfaces:**
- Produces (в тестах): `internal static class EconomyModel` — два пула:
  - **этажи**: полная намазка = 25 единиц (`imbue` + `imbue_type`); доход = ожидаемое выпадение `imbue` + `imbue_type` за 5 этажей и вершину;
  - **башня**: полная намазка = 3 единицы `imbue_tower`, проверяется с ранга 4;
  - `double FullFill(TowerData d, int rank, Pool p)`, `double Income(TowerData d, int rank, Pool p)`;
  - `double UsablePerHour(TowerData d, int farmRank, int targetRank, Pool p, double speed)` — валюта уровня `farmRank` доводится до `targetRank` обменом `ExchangeRatio^(targetRank − farmRank)`; уровень 1 не обменивается (0).

Подсказка к подбору: полная намазка от ранга не зависит, поэтому `drop_growth` в пределах 1 < g ≤ ~1,022 (`g^13 ≤ 0,8 / 0,6`). Уравнение 2 выполняется с запасом: 3,5 / 4 = 0,875 < 1.

- [ ] **Step 1: tests:**
  - `Equation1_rank1_covers_full_fill` — этажи: `Income(1) / FullFill(1) ≥ 1.0`.
  - `Equation1_ranks_2_to_15_cover_60_to_80_percent` — для каждого пула, где он проверяется.
  - `Bare_tower_income_at_least_40_percent` — отдельно, потому что доход не зависит от модов.
  - `Income_grows_with_rank` — `Income(N + 1) > Income(N)` для этажей, N = 1..14.
  - `Equation2_own_rank_is_best_per_hour` — для N = 2..15 и каждого пула: `UsablePerHour(N, N, speed 1) > UsablePerHour(N − 1, N, speed 3.5)` и `> UsablePerHour(N − 2, N, speed 3.5)` (где ранг ≥ 1). Пара 2/1 проверяется: уровень 1 не обменивается.
  - `Mods_do_not_change_currency_income` — на реальных данных.
- [ ] **Step 2:** при FAIL подобрать `drop_base`, `drop_growth`, `summit_multiplier` до PASS.
- [ ] **Step 3:** весь `dotnet test core/Dialoid.sln` зеленый, 0 предупреждений.
- [ ] **Step 4:** commit `test(tower): economy acceptance`.
- [ ] **Step 5:** последний коммит `docs(tower): balance notes, data reference, readme` — в `BALANCE_NOTES.md` итоговые нормы и пометка: «это аналитическая модель; проверка симуляцией эталонного билда (спека 8.3) — после активностей этажей».

---

## Что не входит (следующие планы)

- Активности этажей (таймер, алтари, волны, Испытания, Сопровождение), монстры, аффиксы, боссы и пул убер-боссов — ждут живого боя, 3D-комнаты и плана врагов.
- Метка жесткого контра относительно билда и проверка «−50% у эталона» симуляцией — нужны моды монстров в симуляции.
- Лут предметов (качество → редкость, смещение по типам из документа лута 7.4).
- Опыт за забег, онбординг первых трех башен, экран башни в Godot, окна подтверждения, сохранение кошелька и прогресса.
