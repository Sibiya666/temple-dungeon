# Dialoid Tower Core Implementation Plan (план 5)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Логика Башни в ядре без мира: данные башни, ранги, сборка и перемешивание, кошелек валюты с уровнями, намазка, качество награды и выпадение валюты, правила забега (смерть, выход, вершина, открытие ранга) и тесты двух уравнений экономики.

**Architecture:** Новый модуль `Dialoid.Core.Tower` со своим загрузчиком `TowerDataLoader` для каталогов `data/tower/*.json`. `DataLoader` и `GameData` не меняются: их же правят сессии живого боя и лута. Модуль переиспользует `DataLoader.ReadCatalog` (internal, та же сборка), `DataError` и `IRandomSource` из `Simulation`. Все операции чистые: на вход состояние и случайность, на выход новое состояние или отказ с русским текстом. Активностей этажей (таймер, волны, ИИ) нет: этаж в этом плане — «пройден» или «проигран» по команде теста.

**Tech Stack:** .NET 8 (`net8.0`, RollForward Major), C# 12, xUnit. Godot не участвует.

**Spec:** `docs/superpowers/specs/2026-10-29-dialoid-endgame-tower-design.md` (утверждена 2026-10-29). Основа: `docs/superpowers/specs/2026-10-25-dialoid-core-systems-design.md` (§3.3 данные, §4 модификаторы). Связи: `2026-10-29-dialoid-loot-design.md` (§7.2, §7.4, §9), `2026-10-29-dialoid-enemies-design.md`.

## Global Constraints

- `core/` собирается с `TreatWarningsAsErrors` и `Nullable enable`; `dotnet test core/Dialoid.sln` зеленый, 0 предупреждений. Все существующие тесты остаются зелеными без изменения их утверждений.
- `DataLoader`, `GameData`, `Validator`, `Simulation/*`, `Sandbox`, `game/` **не меняются** (кроме чтения). Новый код — только `core/Dialoid.Core/Tower/`, тесты — `core/Dialoid.Core.Tests/Tower/`, данные — `data/tower/`.
- Данные строгие, как в ядре: неизвестное поле — ошибка (`StrictJson.RejectUnknown`), сообщения об ошибках — русские предложения в `DataError`.
- Числа из спеки (копировать точно):
  - этажей в башне — **5**; свойств на этаж — **до 5**, на башню — **до 3**;
  - 15 рангов; полосы 1–12, 12–24, 24–36, 36–48, 48–60; ранги 6–15 — уровень персонажа 60, уровень зоны `60 + (ранг − 5)`;
  - штраф опыта на ранге ниже своего: −10% за каждый уровень сверх верхней границы полосы, не больше −50%; «ниже своего» — верхняя граница **строго меньше** уровня персонажа;
  - макс. тир мода по рангам: 1–2 → 1, 3–4 → 2, 5–7 → 3, 8–11 → 4, 12–15 → 5;
  - виды модов по рангам: общие с 1, под тип этажа с 3, на башню с 4;
  - цена повторного перемешивания внутри одной сборки: 0, 1, 2, 4, 8, … (первое бесплатно);
  - обмен валюты вверх только на соседний уровень, 4:1;
  - уравнение 1: башня ранга N ≥ 2 приносит 60–80% полной намазки ранга N; ранг 1 — не меньше 100%; голая башня — не меньше 40%;
  - уравнение 2: применимая к рангу N валюта в час максимальна при фарме ранга N, даже если ранг N−1 и N−2 проходятся в 3,5 раза быстрее;
  - намазка **не** увеличивает количество валюты.
- Тексты для игрока — по-русски, идентификаторы — по-английски. Коммиты — conventional commits, в конце `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Ветка `feat/tower-core`, worktree `../dialoid-worktrees/tower-core`.

## Review Focus

1. **Намазка сверх лимита или несовместимого вида** (6-е свойство этажа, мод башни на этаж, мод под тип не своего типа, вторая единица группы несовместимости). Ожидание: отказ с причиной, кошелек не тронут. Тесты в задаче 5.
2. **Валюта низкого уровня на башне высокого ранга** и наоборот. Ожидание: валюта уровня L применима только к рангу ≤ L; тратится самый низкий подходящий уровень; при нехватке — отказ без частичного списания. Тесты в задаче 4.
3. **Сценарий «перемешал, поправил слот, вернул как было».** Ожидание: бонус перемешивания потерян навсегда для этой сборки, цена следующего перемешивания продолжает расти. Тест в задаче 3.
4. **Смерть на вершине и выход до вершины.** Ожидание: после смерти на вершине награды этажей сохранены, вершина доступна повторно; выход до победы на 5-м этаже — вершины и ее награды нет, ранг не открывается. Тесты в задаче 7.
5. **Детерминизм.** Один сид и одни действия → одинаковые моды, числа и выпадения на любом ПК. Тест в задаче 5 и 6.

---

## Структура файлов

```
data/tower/rules.json            общие правила: этажи, слоты, ранги, качество, бонусы, обмен, цены перемешивания
data/tower/floor_types.json      типы этажей и разновидности Испытаний
data/tower/biomes.json           биомы (только вид)
data/tower/floor_mods.json       свойства: вид, типы, группа, метки стиля, жесткий контр, тиры
data/tower/currencies.json       валюта: операция, уровень, ранг выпадения, нормы выпадения, смещение
core/Dialoid.Core/Tower/TowerData.cs         неизменяемые каталоги + записи определений
core/Dialoid.Core/Tower/TowerDataLoader.cs   загрузка и проверка data/tower
core/Dialoid.Core/Tower/Json/*.cs            JSON-классы с CheckFields (как Data/Json)
core/Dialoid.Core/Tower/RankRules.cs         полосы, уровень зоны, допуски модов, опыт, открытие
core/Dialoid.Core/Tower/TowerSpec.cs         описание башни (чистые данные) + TowerCodec (JSON)
core/Dialoid.Core/Tower/TowerAssembly.cs     сборка: слоты, разновидности, перемешивание, бонус
core/Dialoid.Core/Tower/Wallet.cs            кошелек с уровнями, обмен
core/Dialoid.Core/Tower/Crafting.cs          операции валюты над этажом/башней
core/Dialoid.Core/Tower/TowerRefusal.cs      причины отказа + русские тексты
core/Dialoid.Core/Tower/Rewards.cs           качество этажа и вершины, выпадение валюты
core/Dialoid.Core/Tower/TowerRun.cs          забег: этажи, смерть, выход, вершина, открытие ранга
core/Dialoid.Core.Tests/Tower/*Tests.cs
core/Dialoid.Core.Tests/Fixtures/tower/      маленький набор данных башни для тестов
```

---

### Task 1: Данные башни и загрузчик

**Files:**
- Create: `data/tower/{rules,floor_types,biomes,floor_mods,currencies}.json`
- Create: `core/Dialoid.Core/Tower/TowerData.cs`, `TowerDataLoader.cs`, `Tower/Json/*.cs`
- Create: `core/Dialoid.Core.Tests/Fixtures/tower/*.json` (копия-минимум), `core/Dialoid.Core.Tests/Tower/TowerDataLoaderTests.cs`

**Interfaces:**
- Consumes: `DataLoader.ReadCatalog<T>`, `DataLoader.Options`, `StrictJson`, `DataError`, `LoadResult`-образный результат.
- Produces:
  - `public enum ModKind { General, FloorType, Tower }`
  - `public enum CurrencyOp { AddMod, AddModBiased, RerollOne, UpgradeTier, Clear, RemoveOne, RerollVariant, Shuffle }`
  - `public sealed record FloorTypeDef(string Id, string Name, bool HasEndBoss, IReadOnlyList<string> Variants)` — `Variants` пуст у всех, кроме `trial`.
  - `public sealed record ModTier(int Tier, double Min, double Max, double Quality)` — `Quality` = вклад в качество при числе на середине диапазона.
  - `public sealed record FloorModDef(string Id, string Name, ModKind Kind, IReadOnlyList<string> FloorTypes, string? Variant, string? Group, IReadOnlyList<string> StyleTags, string? HardCounter, bool AffectsSummit, IReadOnlyList<ModTier> Tiers)` — `FloorTypes` пуст = совместим со всеми; для `FloorType` ровно один тип.
  - `public sealed record CurrencyDef(string Id, string Name, CurrencyOp Op, ModKind? ModKind, bool HasLevel, int DropsFromRank, double DropBase, double DropGrowth, double SummitMultiplier, string? BiasGroup, double BiasChance)`
  - `public sealed record RankDef(int Rank, int LevelMin, int LevelMax, int ZoneLevel, IReadOnlySet<ModKind> ModKinds, int MaxTier)` — для 1–5 `ZoneLevel = 0` (вычисляется), для 6–15 — `60 + (rank − 5)`.
  - `public sealed record TowerRules(int Floors, int FloorSlots, int TowerSlots, IReadOnlyList<double> HeightMultipliers, double SummitBonus, double DiminishingC, double DiversityBonusPerType, int DiversityBonusTypesCap, double ShuffleBonus, double NoDeathBonus, IReadOnlyList<int> ShuffleCosts, int ExchangeRatio, double XpPenaltyPerLevel, double XpPenaltyCap, IReadOnlyList<RankDef> Ranks)`
  - `public sealed class TowerData { Rules; FloorTypes; Biomes; Mods; Currencies }` (словари по id)
  - `public static class TowerDataLoader { public static (TowerData? Data, IReadOnlyList<DataError> Errors) Load(string towerDirectory) }`

Стартовое содержимое `data/tower` (**[Решение Claude]**, цифры к балансу):
- типы: `timed_run` (HasEndBoss), `trial` (варианты `escort`, `hold`, `ritual`, `levers`), `clear`, `waves`, `bosses` (HasEndBoss); биомы: `crypt`, `swamp`, `forge`, `ruins`.
- моды: 10 общих (из них `no_life_regen`, `no_resource_regen`, `no_healing` с `hard_counter`), по 2 под каждый тип и под каждую разновидность Испытания, 4 на башню (один с `affects_summit` и группой `gate`). Группы: `timer`, `waves`, `gate`, `density`. Тиры 1–5 у каждого.
- валюта по таблице спеки 8.3: `imbue`, `imbue_type`, `imbue_tower`, `clear`, `shuffle`, `reroll`, `reroll_variant`, `upgrade`, `remove`, `biased_rare` (50%), `biased_epic` (90%), `bias_value` (ранг 10; операция `UpgradeTier` с флагом к верхней границе — **[Решение Claude]**: в этом плане `bias_value` реализуется как `RerollOne`-по-числу, см. задачу 5).
- `rules.json`: `floors 5`, `floor_slots 5`, `tower_slots 3`, `height_multipliers [1.0,1.1,1.2,1.35,1.5]`, `summit_bonus 3.0`, `diminishing_c 2.0`, `diversity_bonus_per_type 0.03`, `diversity_bonus_types_cap 3`, `shuffle_bonus 0.05`, `no_death_bonus 0.03`, `shuffle_costs [0,1,2,4,8,16]` (дальше удваивается), `exchange_ratio 4`, `xp_penalty_per_level 0.10`, `xp_penalty_cap 0.5`, 15 рангов с числами из Global Constraints.

Проверки загрузчика (каждая — русское сообщение): неизвестное поле; ссылка мода на неизвестный тип/разновидность; мод `FloorType` без ровно одного типа; `Variant` у типа без разновидностей; тиры не 1..5 по порядку, `Min > Max`, `Quality ≤ 0`; мод с `hard_counter` вида `Tower`; `affects_summit` не у мода башни; валюта `AddMod*` без `ModKind`; `DropsFromRank` вне 1–15; `BiasChance` вне (0,1) у `AddModBiased`; рангов не 15 или полосы не стыкуются; `HeightMultipliers` не длины `Floors`.

- [ ] **Step 1: Write failing tests** `TowerDataLoaderTests`:
  - `Real_tower_data_loads_without_errors` — `Load(<repo>/data/tower)` → `Errors` пуст, 5 типов, 15 рангов, `Rules.Floors == 5`, `Rules.FloorSlots == 5`, `Rules.TowerSlots == 3`.
  - `Rank_table_matches_spec` — ранг 2: `LevelMin 12, LevelMax 24, MaxTier 1`, виды `{General}`; ранг 4: виды `{General, FloorType, Tower}`, `MaxTier 2`; ранг 8: `MaxTier 4`, `ZoneLevel 63`; ранг 15: `MaxTier 5`, `ZoneLevel 70`.
  - `Hard_counter_on_tower_mod_is_rejected` — фикстура с модом `kind: tower, hard_counter: "no_healing"` → ошибка с `Id` мода.
  - `Unknown_field_is_rejected`, `Floor_type_mod_needs_exactly_one_type`, `Tiers_must_be_1_to_5_in_order`, `Rank_bands_must_join` — по одной ошибке на сломанную фикстуру.
- [ ] **Step 2:** `dotnet test core/Dialoid.sln --filter TowerDataLoaderTests` → FAIL (нет типов).
- [ ] **Step 3:** реализовать записи, JSON-классы и `TowerDataLoader.Load`. Путь к реальным данным в тестах — `Path.Combine(FixturePaths.RealData, "tower")`.
- [ ] **Step 4:** тесты PASS, весь `dotnet test` зеленый.
- [ ] **Step 5:** commit `feat(tower): tower data catalogs and loader`.

---

### Task 2: Ранги

**Files:** Create `core/Dialoid.Core/Tower/RankRules.cs`, test `Tower/RankRulesTests.cs`

**Interfaces:**
- Consumes: `TowerRules`, `RankDef`.
- Produces: `public static class RankRules`:
  - `int ZoneLevel(TowerRules r, int rank, int characterLevel)` — ранги 1–5: `Math.Clamp(characterLevel, LevelMin, LevelMax)`; 6–15: `RankDef.ZoneLevel`.
  - `double XpMultiplier(TowerRules r, int rank, int characterLevel)` — `1 − min(cap, perLevel × max(0, characterLevel − LevelMax))`; для 6–15 всегда 1.
  - `bool CanApply(TowerRules r, int rank, ModKind kind, int tier)`.
  - `bool CanUnlock(TowerRules r, int nextRank, bool uberOfPreviousRankKilled, int characterLevel)` — `uberKilled && characterLevel >= LevelMin(nextRank)`; ранг 1 открыт всегда.

- [ ] **Step 1: tests:** `ZoneLevel(rank 2, lvl 5) == 12`, `(2, 18) == 18`, `(2, 30) == 24`, `(9, 60) == 64`; `XpMultiplier(2, 24) == 1.0`, `(2, 26) ≈ 0.8`, `(2, 40) == 0.5`; `CanApply(rank 2, FloorType, 1) == false`, `(3, FloorType, 2) == true`, `(3, General, 3) == false`; `CanUnlock(3, true, 23) == false`, `(3, true, 24) == true`, `(3, false, 30) == false`.
- [ ] **Step 2:** FAIL. **Step 3:** реализовать. **Step 4:** PASS. **Step 5:** commit `feat(tower): rank rules`.

---

### Task 3: Описание башни, сборка и перемешивание

**Files:** Create `Tower/TowerSpec.cs`, `Tower/TowerAssembly.cs`, `Tower/TowerRefusal.cs`; tests `Tower/TowerAssemblyTests.cs`, `Tower/TowerCodecTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record ModInstance(string ModId, int Tier, double Value)`
  - `public sealed record FloorSpec(string TypeId, string? Variant, string BiomeId, IReadOnlyList<ModInstance> Mods)`
  - `public sealed record TowerSpec(int Rank, IReadOnlyList<FloorSpec> Floors, IReadOnlyList<ModInstance> TowerMods, string? UberBossId)` — чистые данные, без уровня персонажа.
  - `public static class TowerCodec { string ToJson(TowerSpec); bool TryFromJson(string, TowerData, out TowerSpec?, out string? error) }` — snake_case, строгие поля, проверка id по `TowerData`.
  - `public enum TowerRefusal { None, WrongRank, SlotOutOfRange, UnknownType, SlotFull, KindNotAllowed, TierNotAllowed, TypeMismatch, GroupConflict, HardCounterOnTower, NotEnoughCurrency, NoMods, NotATrial, NothingToUpgrade }` и `public static string TowerTexts.Refusal(TowerRefusal)` — русские тексты.
  - `public sealed class TowerAssembly` (изменяемая сборка до входа):
    - `TowerAssembly(TowerData data, int rank, IRandomSource rng)`; `TowerSpec Spec { get; }`
    - `TowerRefusal SetFloorType(int slot, string typeId)` — для `trial` разновидность выпадает при первой постановке в слот и запоминается за слотом: замена на другой тип и возврат `trial` дают ту же разновидность. Смена типа снимает моды под тип этажа (общие остаются, если совместимы с новым типом, иначе снимаются). Если башня была перемешана — `ShuffleBonusActive = false`.
    - `void SetBiome(int slot, string biomeId)` — бонус не трогает.
    - `int NextShuffleCost { get; }` — `ShuffleCosts[shufflesDone]`, дальше удвоение последнего.
    - `TowerRefusal Shuffle(Wallet wallet)` — заполняет все 5 слотов, два одинаковых типа подряд не ставит, разновидности бросает заново, моды этажей снимает; списывает `NextShuffleCost` валюты `shuffle`; ставит `ShuffleBonusActive = true`.
    - `bool ShuffleBonusActive { get; }`; `string? UberBossId` — **[Решение Claude]** в этом плане задается извне (`SetUberBoss(string)`): пула боссов в данных ядра еще нет.

- [ ] **Step 1: tests:**
  - `Shuffle_never_places_same_type_twice_in_a_row` — 200 сидов.
  - `First_shuffle_is_free_then_costs_grow` — цены 0,1,2,4; при нехватке — `NotEnoughCurrency`, сборка не меняется.
  - `Manual_edit_after_shuffle_drops_bonus_for_good` — перемешал → `SetFloorType` → вернул прежний тип → `ShuffleBonusActive == false`; `SetBiome` бонус не снимает.
  - `Trial_variant_is_remembered_by_slot` — `trial` → `clear` → `trial`: та же разновидность.
  - `Changing_type_removes_type_mods` — мод `waves`-типа снят при смене на `clear`.
  - `Codec_round_trips_and_rejects_unknown_ids`.
- [ ] **Step 2:** FAIL. **Step 3:** реализовать. **Step 4:** PASS. **Step 5:** commit `feat(tower): tower spec, assembly and shuffle`.

---

### Task 4: Кошелек с уровнями

**Files:** Create `Tower/Wallet.cs`; test `Tower/WalletTests.cs`

**Interfaces:**
- Produces: `public sealed class Wallet`:
  - `void Add(string currencyId, int level, int count)` — для валюты без уровня `level` игнорируется (хранится как 0).
  - `int Count(string currencyId)`; `int CountUsable(string currencyId, int rank)` — сумма уровней ≥ `rank` (без уровня — все).
  - `bool TrySpend(TowerData data, string currencyId, int rank, int count)` — списывает с самого **низкого** подходящего уровня, иначе ничего не списывает.
  - `bool TryExchangeUp(TowerData data, string currencyId, int fromLevel)` — `ExchangeRatio` единиц уровня L → 1 единица уровня L+1; только для валюты с уровнем, L < 15.
  - `IReadOnlyDictionary<int,int> Levels(string currencyId)`.

- [ ] **Step 1: tests:** `Spends_lowest_usable_level_first` (уровни 3×2, 5×2, ранг 3, тратим 3 → остаются 5×1); `Low_level_is_unusable_on_higher_rank` (`CountUsable(imbue, rank 6)` при уровне 5 → 0); `Spend_is_all_or_nothing`; `Exchange_up_is_4_to_1_neighbor_only`; `Levelless_currency_works_on_any_rank` (`clear`).
- [ ] **Step 2–4.** **Step 5:** commit `feat(tower): wallet with currency levels`.

---

### Task 5: Намазка (операции валюты)

**Files:** Create `Tower/Crafting.cs`; test `Tower/CraftingTests.cs`

**Interfaces:**
- Consumes: `TowerAssembly`, `Wallet`, `RankRules.CanApply`, `IRandomSource`.
- Produces: `public static class Crafting`:
  - `TowerRefusal Apply(TowerData data, TowerAssembly tower, Wallet wallet, string currencyId, int? floorSlot, IRandomSource rng, string? targetModId = null)` — `floorSlot == null` = башня. Проверки по порядку: ранг выпадения/доступа валюты, вид мода разрешен рангом, слот свободен (5/3), пул не пуст (с учетом типа этажа, разновидности, групп и запрета `HardCounter` для башни), хватает валюты. Списание — только при успехе.
  - Пул и бросок: равные веса по подходящим модам; тир — равные веса от 1 до `MaxTier` ранга; `Value = rng.InRange(Min, Max)` тира.
  - `AddModBiased`: с вероятностью `BiasChance` пул сужается до группы `BiasGroup` (если в ней есть подходящие), иначе обычный пул.
  - `RerollOne`: заменить случайный мод цели модом того же вида.
  - `UpgradeTier`: случайный мод с тиром < макс. ранга → тир+1, число **бросается заново** в диапазоне нового тира; `NothingToUpgrade`, если таких нет.
  - `Clear`: снять все моды цели.
  - `RemoveOne`: снять мод `targetModId` (адресно).
  - `RerollVariant`: только для `trial`; новая разновидность ≠ прежней, моды под тип снимаются.
  - `bias_value` в этом плане: `RerollOne` числа случайного мода с `Value = rng.InRange((Min+Max)/2, Max)` (**[Решение Claude]**, к балансу).

- [ ] **Step 1: tests:**
  - `Sixth_floor_mod_is_refused_and_wallet_untouched`, `Fourth_tower_mod_is_refused`.
  - `Floor_type_mod_not_allowed_on_rank_2`; `Tower_mod_not_allowed_on_rank_3`.
  - `Tower_pool_never_contains_hard_counters` (1000 бросков `imbue_tower`).
  - `Group_allows_one_mod` — два мода группы `timer` на одном этаже не появляются (1000 сидов).
  - `Tier_never_exceeds_rank_max` (ранг 2 → всегда тир 1).
  - `Upgrade_rerolls_value_in_new_tier_range`.
  - `Remove_one_removes_exactly_target`.
  - `Biased_currency_hits_group_about_half` — 2000 бросков `biased_rare`, доля мода группы ∈ [0,45; 0,65] при базовой доле < 0,3.
  - `Same_seed_same_result` — два прогона одних действий дают равные `TowerSpec` (через `TowerCodec.ToJson`).
- [ ] **Step 2–4.** **Step 5:** commit `feat(tower): currency operations`.

---

### Task 6: Награды: качество и выпадение валюты

**Files:** Create `Tower/Rewards.cs`; test `Tower/RewardsTests.cs`

**Interfaces:**
- Produces: `public static class Rewards`:
  - `double Diminish(TowerRules r, double x)` = `1 + c·x/(c + x)`, `c = DiminishingC`. Строго растет, вогнута, `Diminish(0) = 1`.
  - `double ModQuality(TowerData d, ModInstance m)` — `Quality` тира × (`Value` / середина диапазона).
  - `double FloorQuality(TowerData d, TowerSpec t, int floorIndex, bool noDeaths, bool shuffleBonus)` = `HeightMultipliers[i] × Diminish(Σ модов этажа) × Diminish(Σ модов башни) × (1 + diversity) × (1 + shuffle?) × (1 + noDeath?)`; `diversity = DiversityBonusPerType × min(cap, distinctTypes − 1)`.
  - `double SummitQuality(TowerData d, TowerSpec t, bool noDeaths, bool shuffleBonus)` = `SummitBonus × Diminish(Σ модов башни) × (1 + diversity) × …` — моды этажей не участвуют.
  - `double NextModGain(TowerData d, TowerSpec t, int? floorIndex, double addedQuality)` — прибавка качества от следующего мода (для экрана).
  - `IReadOnlyDictionary<string,double> ExpectedCurrencyDrop(TowerData d, int rank, int floorIndex, bool summit)` — по каждой валюте с `DropsFromRank ≤ rank`: `DropBase × DropGrowth^(rank−1) × (summit ? SummitMultiplier : HeightMultipliers[i])`. **Моды не участвуют.** Уровень выпавшей валюты = `rank`.
  - `IReadOnlyList<(string CurrencyId, int Level, int Count)> RollCurrencyDrop(TowerData d, int rank, int floorIndex, bool summit, IRandomSource rng)` — целая часть ожидания + `rng.Chance` на дробную.

- [ ] **Step 1: tests:** `Diminish_is_increasing_and_concave` (x = 0..20 шагом 0,5); `Fifth_mod_still_adds_quality` (`NextModGain > 0` при 4 модах); `Floor_mods_do_not_affect_summit`; `Diversity_bonus_caps_at_3_types`; `Currency_drop_ignores_mods` (одна башня голая и полностью намазанная → равные `ExpectedCurrencyDrop`); `Currency_does_not_drop_before_its_rank` (`imbue_tower` на ранге 3 → 0); `Summit_drops_more_than_any_floor`.
- [ ] **Step 2–4.** **Step 5:** commit `feat(tower): reward quality and currency drops`.

---

### Task 7: Забег

**Files:** Create `Tower/TowerRun.cs`; test `Tower/TowerRunTests.cs`

**Interfaces:**
- Consumes: `TowerSpec`, `Rewards`, `RankRules`, `Wallet`.
- Produces:
  - `public enum RunStage { Floor, Summit, Won, Exited }`
  - `public sealed record FloorReward(int FloorIndex, double Quality, IReadOnlyList<(string CurrencyId, int Level, int Count)> Currency)`
  - `public sealed record ExitLoss(bool LostSummit, IReadOnlyList<int> LostFloors)` — для окна подтверждения.
  - `public sealed class TowerRun`:
    - `static TowerRun Enter(TowerData d, TowerSpec spec, int characterLevel, IRandomSource rng)` — фиксирует `ZoneLevel` на забег; спека дальше не меняется.
    - `int CurrentFloor`, `RunStage Stage`, `int ZoneLevel`, `IReadOnlyList<FloorReward> Rewards`, `int Deaths(int floorIndex)`.
    - `FloorReward WinFloor(Wallet wallet)` — выдает награду сразу и кладет валюту в кошелек; после 5-го этажа `Stage = Summit`.
    - `void Die()` — этаж (или вершина) начинается заново, моды и награды сохраняются, `Deaths++`; бонус «без смертей» для этого этажа пропадает.
    - `FloorReward WinSummit(Wallet wallet)` — `Stage = Won`, `UberKilled = true`.
    - `ExitLoss PreviewExit()`; `ExitLoss Exit()` — `Stage = Exited`.
    - `bool UberKilled`.
  - `public sealed class TowerProgress { int HighestUnlocked; bool TryUnlockNext(TowerData d, TowerRun finishedRun, int characterLevel) }` — открывает `rank+1` по `RankRules.CanUnlock`.

- [ ] **Step 1: tests:** `Floor_reward_is_granted_immediately`; `Death_restarts_floor_keeps_mods_and_rewards`; `Death_removes_no_death_bonus_only_for_that_floor`; `Exit_before_summit_loses_summit_and_unlock` (`PreviewExit` перечисляет этажи 4–5 и вершину); `Summit_death_keeps_floor_rewards`; `Rank_unlocks_only_after_uber_and_level`; `Zone_level_fixed_for_the_run`.
- [ ] **Step 2–4.** **Step 5:** commit `feat(tower): tower run rules`.

---

### Task 8: Приемка экономики

**Files:** Create `core/Dialoid.Core.Tests/Tower/EconomyAcceptanceTests.cs`; Modify `data/tower/currencies.json` (только нормы выпадения, если тест укажет); Modify `README.md` (строка статуса: «Башня: логика в ядре»); Modify `data/BALANCE_NOTES.md` (раздел «Башня: экономика» с принятыми нормами).

**Interfaces:**
- Consumes: всё выше, реальные `data/tower`.
- Produces: `internal static class EconomyModel` в тестах, два пула:
  - **пул этажей**: полная намазка = 25 единиц (`imbue` + `imbue_type` вместе, 5 этажей × 5 слотов); доход = ожидаемое выпадение `imbue` + `imbue_type` за 5 этажей и вершину;
  - **пул башни**: полная намазка = 3 единицы `imbue_tower` с ранга 4 (до ранга 4 пул не проверяется); доход = ожидаемое выпадение `imbue_tower`.
  - `double FullFill(TowerData d, int rank, Pool p)`, `double Income(TowerData d, int rank, Pool p)`.
- Подсказка к подбору норм: полная намазка от ранга почти не зависит, поэтому для уравнения 1 `drop_growth` должен быть близок к 1 (≈1,00–1,02). Уравнению 2 это не мешает: валюта ранга N−1 доводится до N обменом 4:1, и даже при скорости ×3,5 дает 3,5/4 < 1 от дохода своего ранга.
- **Известное исключение:** ранг 1 по спеке щедрый (≥100%), поэтому пара «ранг 2 против ранга 1» в уравнении 2 не проверяется. Валюта ранга 1 — только общие моды тира 1, утечка мала. Записать это в `BALANCE_NOTES.md`.

- [ ] **Step 1: tests:**
  - `Equation1_rank1_covers_full_fill` — пул этажей: `Income(1) / FullFill(1) ≥ 1.0`.
  - `Equation1_ranks_2_to_15_cover_60_to_80_percent` — для каждого пула и каждого N, где пул проверяется: `0.60 ≤ Income/FullFill ≤ 0.80`.
  - `Bare_tower_income_at_least_40_percent` — следует из предыдущего, но проверяется отдельно, потому что доход не зависит от модов.
  - `Equation2_own_rank_is_best_per_hour` — для N ∈ 3..15 и каждого вида Наложения: применимая к N валюта в час при фарме N (время 1) больше, чем при фарме N−1 и N−2 (время 1/3,5), где валюта уровня L < N доводится до N обменом `ExchangeRatio^(N−L)`.
  - `Mods_do_not_change_currency_income` — повтор теста из задачи 6 на реальных данных.
- [ ] **Step 2:** запустить; при FAIL подобрать `drop_base`, `drop_growth`, `summit_multiplier` в `currencies.json` до PASS, записать итоговые числа и рассуждение в `BALANCE_NOTES.md`.
- [ ] **Step 3:** весь `dotnet test core/Dialoid.sln` зеленый, 0 предупреждений.
- [ ] **Step 4:** commit `test(tower): economy acceptance, balance notes`.

---

## Что не входит (следующие планы)

- Активности этажей (таймер, алтари, волны, Испытания, Сопровождение), монстры, аффиксы, боссы и пул убер-боссов — ждут живого боя, 3D-комнаты и плана врагов.
- Метка жесткого контра относительно билда и автоматическая проверка «−50% у эталона» симуляцией — нужны моды монстров в симуляции.
- Лут предметов (качество → редкость, смещение по типам из документа лута 7.4).
- Опыт персонажа за забег, онбординг первых трех башен, экран башни (Godot), сохранение кошелька и прогресса.
