# Dialoid Debug Scene Implementation Plan (план 3 из 3)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Отладочная сцена Godot 4 .NET (спека §13), в которой владелец собирает любого из 6 эталонных персонажей, меняет узел и видит изменение DPS с разбором причины (§15.3, §15.4).

**Architecture:** Три слоя. (1) `Dialoid.Core` получает публичный API объяснения чисел: листы персонажа и скилла с разбором каждого числа и защитные итоги симуляции. (2) Новая библиотека на чистом C# `core/Dialoid.Sandbox`. Это вся логика песочницы: сессия редактирования билда, команды (взять узел, сменить уровень, поставить скилл...), импорт и экспорт, эталоны, запуск симуляции, русские тексты разбора. Она полностью покрыта `dotnet test` без Godot. (3) Godot-проект `game/` — тонкий слой отрисовки на Control-узлах, интерфейс строится кодом в `_Ready()` (§3.2). Логики правил в `game/` нет: каждое действие — вызов `SandboxSession`. Плюс режим самопроверки `--selftest`, который проверяет §15.4 headless.

**Tech Stack:** .NET 8 (`net8.0`, RollForward Major, SDK 10 установлен), C# 12, xUnit, Godot 4 .NET (Godot.NET.Sdk той же версии, что установленный редактор).

**Spec:** `docs/superpowers/specs/2026-10-25-dialoid-core-systems-design.md` (§3.1–3.2, §4.7, §11.3, §12, §13, §15.3, §15.4). Хвосты: `docs/superpowers/plans/2026-10-25-dialoid-core-engine-notes.md` (раздел «Для плана 3»). Данные: `docs/data-authoring-reference.md`.

## Global Constraints

- `core/` собирается с `TreatWarningsAsErrors`, `Nullable enable` (через `core/Directory.Build.props`). Новые проекты в `core/` наследуют его и добавляются в `core/Dialoid.sln`. `dotnet test core/Dialoid.sln` — зеленый, 0 предупреждений.
- Все 647 существующих тестов остаются зелеными. Существующие публичные сигнатуры `Dialoid.Core` не ломаются: только добавления (новые типы, новые свойства `init` с умолчаниями).
- Изменения поведения ядра запрещены: числа DPS, TTK и выживания всех эталонов не меняются (это проверяют существующие тесты).
- `Dialoid.Sandbox` не зависит от Godot. `game/` не содержит правил игры: легальность, очки, компиляция и симуляция идут только через `Dialoid.Sandbox` и `Dialoid.Core`.
- Тексты для владельца (подписи, ошибки, разбор) — на русском. Идентификаторы кода, сообщения коммитов — на английском (conventional commits).
- Godot (§3.2): любой вызов `godot` — под `timeout` (на macOS фатальная ошибка вешает процесс). Проверка сборки в порядке `dotnet build` → `godot --headless --import` → `godot --headless --quit`, затем лог проверяется на `ERROR`. Все это делает `tools/godot_check.sh`.
- Путь к бинарю Godot: переменная `GODOT`, иначе `/Applications/Godot_mono.app/Contents/MacOS/Godot`, иначе `godot` в PATH. Godot ставит владелец (`brew install --cask godot-mono`). Задачи 1–4 Godot не требуют. Задача 5 и дальше без Godot возвращают BLOCKED с этой инструкцией.
- Отладочная сцена — инструмент владельца, а не игровой UI (§13): понятность важнее красоты, стили по умолчанию.
- Коммит после каждой задачи.

## Review Focus

1. **Папка `data/` не найдена** (запуск из редактора, headless, из другой папки). Ожидание: сцена показывает понятную ошибку с проверенными путями и не падает. Поиск пути — в `DataLocator` (задача 2), тест там же. Самопроверка задачи 9 запускается из корня репозитория и из `game/`.
2. **Билд стал нелегальным после правки** (снят узел посреди пути, уровень понижен ниже потраченных очков, сменился класс). Ожидание: сессия не бросает исключений, показывает нарушения `BuildRules` по-русски, симуляция возвращает отказ с причинами. Снятие узла, которое разрывает связность, отклоняется с сообщением. Тесты в задачах 2 и 3.
3. **Мусор в поле импорта** (пустая строка, обрезанный код `DLD1:`, произвольный JSON, JSON другого класса с чужими id). Ожидание: русская ошибка, текущий билд не меняется. Тест в задаче 3.
4. **Долгая симуляция замораживает интерфейс или устаревший результат перетирает новый.** Ожидание: симуляция идет в фоне, кнопка недоступна до конца, результат применяется, только если билд не менялся с момента запуска (номер ревизии). Тест ревизий в задаче 4, проверка в самопроверке задачи 9.
5. **Число на экране не совпадает с числом в тестах** (сцена считает по-своему). Ожидание: каждое число сцены приходит из `CharacterSheet`/`SkillSheet`/`SimResult` без пересчета. Самопроверка задачи 9 сравнивает DPS эталона в сцене с `Simulator.Run` по тому же билду.

---

## Структура файлов

```
core/Dialoid.Core/Explain/NoConditions.cs        публичный контекст «условия не выполнены»
core/Dialoid.Core/Explain/NumberLine.cs          строка листа: ключ, подпись, значение, разбор
core/Dialoid.Core/Explain/CharacterSheet.cs      лист персонажа (§13.1)
core/Dialoid.Core/Explain/SkillSheet.cs          лист скилла (§13.4)
core/Dialoid.Core/Simulation/SimResult.cs        + DefenseTotals, ResourceSeries (только добавления)
core/Dialoid.Core.Tests/Explain/*.cs
core/Dialoid.Sandbox/Dialoid.Sandbox.csproj      чистый C#, ссылается на Dialoid.Core
core/Dialoid.Sandbox/DataLocator.cs              поиск папки data/
core/Dialoid.Sandbox/SandboxSession.cs           состояние и команды редактирования билда
core/Dialoid.Sandbox/CommandResult.cs            успех или русская причина отказа
core/Dialoid.Sandbox/ReferenceBuilds.cs          список и загрузка data/builds/
core/Dialoid.Sandbox/SimulationRunner.cs         фоновая симуляция с ревизией
core/Dialoid.Sandbox/Texts.cs                    русские подписи: статы, режимы, типы урона
core/Dialoid.Sandbox/BreakdownText.cs            разбор StatResult в строки
core/Dialoid.Sandbox.Tests/*.cs                  xUnit поверх реальной data/
game/project.godot, game/Dialoid.Game.csproj     Godot-проект, ссылается на Dialoid.Sandbox
game/Main.tscn, game/Main.cs                     корень: вкладки панелей, загрузка сессии
game/Ui/*.cs                                     панели и общие контролы
game/SelfTest.cs                                 headless-самопроверка (§15.3, §15.4)
tools/godot_check.sh                             сборка и headless-проверки по §3.2
```

---

### Task 1: Публичный API объяснения чисел в ядре

**Files:**
- Create: `core/Dialoid.Core/Explain/NoConditions.cs`, `NumberLine.cs`, `CharacterSheet.cs`, `SkillSheet.cs`
- Modify: `core/Dialoid.Core/Simulation/SimResult.cs`, `SimRun.cs` / `ResultBuilder.cs` (сбор защитных итогов и серии), `core/Dialoid.Core/Simulation/SkillRuntime.cs` (вынести расчет темпа и перезарядки в публичный статический помощник, которым пользуются и симулятор, и лист)
- Test: `core/Dialoid.Core.Tests/Explain/CharacterSheetTests.cs`, `SkillSheetTests.cs`, `core/Dialoid.Core.Tests/Simulation/DefenseTotalsTests.cs`

**Interfaces:**
- Consumes: `CharacterCompiler.Compile(Build)`, `CompiledCharacter`, `CompiledSkill`, `StatEvaluator.Evaluate`, `HitCalculator.Resolve(HitInput)`, `Simulator.Run`.
- Produces:
  - `public sealed class NoConditions : IConditionContext { public static NoConditions Instance { get; } }`. `IsMet` всегда `false`. Тестовые копии в `HitPipelineTests` заменить на него.
  - `public sealed record NumberLine(string Key, string Label, double Value, StatResult? Breakdown, string? Note = null)`. `Key` — стабильный id (`life`, `armor`, `resist_fire`, `hit_avg_fire`, `uses_per_sec`...). `Label` — русская подпись. `Breakdown` = null только у производных чисел, а производное число перечисляет свои составляющие в `Note`.
  - `public static class CharacterSheet { public static IReadOnlyList<NumberLine> Describe(CompiledCharacter ch, GameData data); }`. Строки: 4 атрибута; здоровье; регенерация здоровья; максимум ресурса; реген и затухание ресурса; броня; сопротивления по стихиям с капом (подпись «45 / кап 75»); уклонение с капом; глобальные шанс и множитель крита; бонус основного атрибута к урону. Условия — `NoConditions`.
  - `public static class SkillSheet { public static IReadOnlyList<NumberLine> Describe(CompiledCharacter ch, CompiledSkill skill, GameData data, WeaponStub? weapon); }`. Строки: урон попадания по каждому итоговому типу (мин, макс, среднее; без защиты цели, без крита, `NoConditions`); шанс и множитель крита; среднее попадание с учетом крита; применений в секунду; перезарядка; стоимость и прирост ресурса; шансы недугов; ключевые числа поведения из `Behavior` (снаряды, цепи, область, длительность зоны). Разбор каждого урона берется из `HitResult.Breakdown`.
  - `SimResult` (только `init`-свойства с умолчанием null или пусто):
    - `DefenseTotals? Defense` — заполняется при `DummyAttacks`. Это `public sealed record DefenseTotals(int AttacksReceived, int AttacksDodged, IReadOnlyDictionary<DamageType,double> IncomingBeforeMitigation, double MitigatedByArmor, double MitigatedByResist, double LifeRegenerated)`.
    - `IReadOnlyList<ResourceSample> ResourceSeries` — серия ресурса. Это `public sealed record ResourceSample(double Time, double Resource, double Life)`, снимок каждые 0.5 с времени симуляции и последний шаг.

- [ ] **Step 1: Тесты листа персонажа.** `Describe_lists_life_armor_resists_and_attributes`: эталон `data/builds/warrior_berserker_whirlwind.json` (через `ContentHarness`). Значение `life` совпадает с `Evaluator.Evaluate("life", ...)`, а `Breakdown.Lines` непуст. Есть строки `resist_fire`, `resist_cold`, `resist_lightning` с капом в подписи. `Every_line_with_breakdown_matches_its_value`: для каждой строки с разбором `Value == Breakdown.Value`.
- [ ] **Step 2: Тесты листа скилла.**
  - `Fireball_sheet_shows_fire_hit_range_and_uses_per_second`: пироман-эталон, скилл `fireball`; есть `hit_avg_fire > 0`, `hit_min_fire ≤ hit_avg_fire ≤ hit_max_fire`, `uses_per_sec > 0`.
  - `Taking_a_damage_node_changes_the_sheet_and_its_breakdown_names_the_node`: снять с билда один пассивный notable на +% огненного урона, взятый в эталоне. `hit_avg_fire` уменьшается, а в разборе исходного билда есть строка с `Source.Id` этого узла.
  - `Attack_sheet_uses_weapon`: эталон воина, `cleave` без оружия и с ним дают разные средние.
- [ ] **Step 3: Тесты защитных итогов.**
  - `Defensive_run_reports_dodges_mitigation_and_regen`: эталон с уклонением против `attacking_boss`, `DummyAttacks = true`. `AttacksReceived > 0`, `AttacksDodged ≤ AttacksReceived`, `MitigatedByArmor + MitigatedByResist > 0`, сумма полученного урона по типам ≤ суммы `IncomingBeforeMitigation`.
  - `Resource_series_is_sampled_every_half_second`: соседние `Time` различаются на 0.5 (кроме последнего), значения ресурса в пределах `[0, max]`.
  - `Offensive_run_has_no_defense_totals`: `Defense == null`.
- [ ] **Step 4: Запустить, убедиться, что тесты падают** (`dotnet test core/Dialoid.sln --filter "FullyQualifiedName~Explain|FullyQualifiedName~DefenseTotals"`).
- [ ] **Step 5: Реализовать.**
  - Темп и перезарядку скилла не дублировать: вынести существующую формулу из `SkillRuntime` в публичный статический помощник, который вызывают и симулятор, и `SkillSheet`.
  - Серию и итоги собирать в `SimRun` в местах, где уже считаются уклонение, смягчение и регенерация (`SimRun.cs` около строк 218–231 и 310–347).
- [ ] **Step 6: Полный прогон** `dotnet test core/Dialoid.sln` — все тесты зеленые, 0 предупреждений, числа эталонов не изменились.
- [ ] **Step 7: Commit** `feat(core): public explain API and defensive sim totals`.

### Task 2: Библиотека песочницы — сессия, пассивки, асцендант, атрибуты, уровень

**Files:**
- Create: `core/Dialoid.Sandbox/Dialoid.Sandbox.csproj`, `DataLocator.cs`, `SandboxSession.cs`, `CommandResult.cs`, `Texts.cs`; `core/Dialoid.Sandbox.Tests/Dialoid.Sandbox.Tests.csproj`, `SessionPassiveTests.cs`, `DataLocatorTests.cs`
- Modify: `core/Dialoid.sln` (оба проекта)

**Interfaces:**
- Consumes: `DataLoader.Load`, `GameData`, `Build`, `BuildRules.Validate/Normalized`, `PassiveTree.CanAllocate/CanDeallocate/StartForClass/PointsForLevel`, `Ascendancy.PointsForLevel`, `CharacterCompiler`, `CharacterSheet` (задача 1).
- Produces:
  - `public static class DataLocator { public static string? Find(string startDirectory); public static IReadOnlyList<string> Candidates(string startDirectory); }`. Ищет вверх от стартовой папки каталог `data/`, в котором есть `classes.json`. `Candidates` — проверенные пути для текста ошибки.
  - `public sealed record CommandResult(bool Ok, string? Error)` со статическими `Success` и `Fail(string)`.
  - `public sealed class SandboxSession`:
    - Поля и события:
      - `GameData Data`
      - `Build Build` — копия только для чтения наружу
      - `int Revision` — растет на каждое успешное изменение
      - `event Action? Changed`
    - Методы:
      - `static SandboxSession Create(GameData data, string classId)` — пустой билд класса, уровень 1, взят только стартовый узел.
      - `CommandResult SetClass(string classId)` — сброс билда.
      - `CommandResult SetLevel(int level)` — от 1 до `BuildRules.MaxLevel`. Взятые узлы не снимаются, лишние очки видны как нарушения.
      - `CommandResult TogglePassive(string nodeId)` — отказ, если узел недоступен или его снятие рвет связность.
      - `CommandResult SetAttributeChoice(string nodeId, Attribute a)`.
      - `CommandResult SetAscendancy(string? ascendancyId)` — смена сбрасывает узлы асценданта.
      - `CommandResult ToggleAscendancyNode(string nodeId)`.
      - `int PassivePointsTotal`, `int PassivePointsSpent`, `int AscendancyPointsTotal`, `int AscendancyPointsSpent`.
      - `IReadOnlySet<string> AllocatablePassives` — для подсветки.
      - `IReadOnlyList<RuleViolation> Violations`.
      - `CompiledCharacter? Compiled` — null, если компиляция невозможна.
      - `IReadOnlyList<NumberLine> CharacterLines`.
    - `Compiled`, `Violations` и листы пересчитываются лениво один раз на ревизию.
  - Сообщения `CommandResult` — по-русски, с именем узла из данных.

- [ ] **Step 1: Тесты локатора.**
  - `Finds_data_from_repo_root_game_and_core_bin`: от корня, от `game/`, от `core/Dialoid.Sandbox.Tests/bin/...` — путь найден.
  - `Returns_null_and_candidates_from_temp_dir`: из временной папки без `data/` возвращает null и непустой список кандидатов.
- [ ] **Step 2: Тесты сессии.**
  - `New_session_has_only_class_start_and_zero_spent`.
  - `Toggle_unreachable_node_fails_in_russian_and_keeps_revision`.
  - `Toggle_reachable_node_allocates_and_bumps_revision`.
  - `Removing_middle_of_path_is_refused`.
  - `Lowering_level_keeps_nodes_and_reports_points_violation`.
  - `SetClass_resets_build`.
  - `Ascendancy_needs_level_20`: на уровне 19 отказ или нарушение, на 20 два очка.
  - `Changing_ascendancy_clears_its_nodes`.
  - `Character_lines_follow_revision`: после взятия узла на здоровье строка `life` меняется.
- [ ] **Step 3: Запустить, убедиться, что падают.**
- [ ] **Step 4: Реализовать.** Правила легальности не писать заново: вызывать `PassiveTree` и `BuildRules`.
- [ ] **Step 5: Прогон** `dotnet test core/Dialoid.sln` — зеленый, 0 предупреждений.
- [ ] **Step 6: Commit** `feat(sandbox): session with passive, ascendancy and attribute editing`.

### Task 3: Песочница — скиллы, оружие, предметы, ротация, импорт/экспорт, эталоны

**Files:**
- Create: `core/Dialoid.Sandbox/ReferenceBuilds.cs`; тесты `SessionSkillTests.cs`, `ImportExportTests.cs`, `ReferenceBuildsTests.cs`
- Modify: `SandboxSession.cs`

**Interfaces:**
- Consumes: `BuildRules.SlotsUnlocked/SkillLevelCap`, `SkillTree.RequirementsMet/ViolatedGroups/PointsForLevel/PointsSpent`, `BuildCodec.Export/TryImport/ToJson/TryFromJson`, `SkillSheet` (задача 1).
- Produces (методы `SandboxSession`):
  - `CommandResult SetSlotSkill(int slot, string? skillId)`. Снятый скилл запоминается вместе с уровнем и узлами для `UndoSlot` (дефолт спеки §14.2: как в LE, в песочнице есть «вернуть»). Отказ, если слот закрыт уровнем или скилл чужого класса без предмета.
  - `CommandResult UndoSlot(int slot)`.
  - `CommandResult SetSkillLevel(int slot, int level)` — до `SkillLevelCap`.
  - `CommandResult AddSkillNodePoint(int slot, string nodeId)`, `CommandResult RemoveSkillNodePoint(int slot, string nodeId)` — с проверкой требований, `exclusive_group`, свободных очков; снятие, ломающее требования другого узла, отклоняется.
  - `CommandResult SetWeapon(string? weaponId)`, `CommandResult ToggleItem(string itemId)`.
  - `CommandResult SetRotation(IReadOnlyList<string> skillIds)` — только скиллы из слотов.
  - `IReadOnlyList<NumberLine> SkillLines(int slot)`.
  - `string ExportCode()` — `DLD1:`.
  - `CommandResult Import(string text)` — принимает код `DLD1:` или JSON билда. При ошибке билд не меняется.
  - `public sealed record ReferenceBuildInfo(string Path, string Title, string Group)`. `Group`: `main`, `l20`, `parity`, `keystones`, `attributes`. `Title` — из имени файла, человеческое.
  - `public static class ReferenceBuilds { public static IReadOnlyList<ReferenceBuildInfo> List(string dataDirectory); public static CommandResult Load(SandboxSession s, ReferenceBuildInfo info); }`.

- [ ] **Step 1: Тесты скиллов.**
  - `Slot_locked_by_level_is_refused`.
  - `Skill_node_point_respects_requirements_and_exclusive_group`.
  - `Removing_required_node_point_is_refused`.
  - `Skill_level_capped_for_loot_skill_without_item`: `poison_cloud` с `ring_of_mist` и без.
  - `Replacing_skill_then_undo_restores_level_and_nodes`.
  - `Rotation_rejects_unslotted_skill`.
- [ ] **Step 2: Тесты импорта.** Отказ с русской ошибкой и неизменной `Revision` для каждого случая: пустая строка, `DLD1:` с обрезанным хвостом, `{"foo":1}`, JSON со скиллом чужого класса без предмета, неизвестный id узла. Плюс `Export_then_import_round_trips` (код и JSON).
- [ ] **Step 3: Тесты эталонов.**
  - `Lists_all_reference_files_grouped`: 6 файлов `main` и каталоги `l20`, `parity`, `keystones`, `attributes` найдены.
  - `Every_main_reference_loads_with_no_violations`.
  - `Loaded_reference_compiles_same_as_file`: `Simulator.Run` по `session.Build` и по файлу дает равный `DpsSingleTarget`.
- [ ] **Step 4: Запустить, убедиться, что падают.**
- [ ] **Step 5: Реализовать** через `BuildRules` и `SkillTree`, без своих правил.
- [ ] **Step 6: Прогон**, зеленый, 0 предупреждений.
- [ ] **Step 7: Commit** `feat(sandbox): skills, items, rotation, import/export and reference builds`.

### Task 4: Песочница — симуляция и тексты разбора

**Files:**
- Create: `core/Dialoid.Sandbox/SimulationRunner.cs`, `BreakdownText.cs`; тесты `SimulationRunnerTests.cs`, `BreakdownTextTests.cs`
- Modify: `Texts.cs`

**Interfaces:**
- Consumes: `Simulator.Run(Build, SimConfig)`, `SimResult` (+ задача 1), `StatResult`, `BreakdownLine`, `ModKind`.
- Produces:
  - `public sealed record SimRequest(string DummyId, SimMode Mode, int Seed, int? PackSize, bool DummyAttacks)`.
  - `public sealed record SimOutcome(int Revision, SimRequest Request, SimResult Result)`.
  - `public sealed class SimulationRunner`:
    - `SimulationRunner(SandboxSession s)`.
    - `Task<SimOutcome> RunAsync(SimRequest r, CancellationToken ct = default)` — снимок билда и ревизии в момент вызова, расчет в `Task.Run`.
    - `bool IsCurrent(SimOutcome o)` — `o.Revision == session.Revision`.
  - `public static class BreakdownText`:
    - `IReadOnlyList<string> Lines(StatResult r)` — по-русски, порядок как в формуле §4.3. Сначала «База», затем «+ Flat» с источниками, «Увеличение: сумма%» и строки, «Больше/Меньше: ×» по одному, «Переопределение», в конце «Итог (до капа → после капа)».
    - `string Title(string statId)` — русская подпись стата из `Texts`.
  - `Texts`: русские имена типов урона, режимов (`Average` = «среднее», `Roll` = «бросок»), манекенов (из данных), ключей `NumberLine`. Для неизвестного ключа возвращается сам ключ, без исключения.
  - Форматирование чисел: целые для DPS и урона, одна десятичная для секунд, проценты с одной десятичной.

- [ ] **Step 1: Тесты.**
  - `Run_returns_same_dps_as_simulator`.
  - `Outcome_is_stale_after_session_change`: запустить, затем `TogglePassive` до чтения результата, `IsCurrent == false`.
  - `Illegal_build_returns_rejected_with_violations`.
  - `Breakdown_lines_follow_formula_order_and_name_sources`: стат с Flat, Increased и More из фикстуры. Строки идут в порядке База → Flat → Увеличение → Больше, имена источников присутствуют, последняя строка содержит итог.
  - `Override_and_cap_are_shown`.
  - `Unknown_text_key_falls_back_to_key`.
- [ ] **Step 2: Запустить, убедиться, что падают.** **Step 3: Реализовать.** **Step 4: Прогон**, зеленый.
- [ ] **Step 5: Commit** `feat(sandbox): background simulation and russian breakdown text`.

### Task 5: Godot-проект и headless-проверка

**Предусловие:** Godot 4 .NET установлен. Если `tools/godot_check.sh` не находит бинарь, вернуть BLOCKED с текстом «Установите Godot 4 .NET: `brew install --cask godot-mono` (или укажите путь в переменной GODOT)».

**Files:**
- Create: `game/project.godot`, `game/Dialoid.Game.csproj`, `game/Main.tscn`, `game/Main.cs`, `game/.gitignore` (`.godot/`, `bin/`, `obj/`), `tools/godot_check.sh`
- Modify: `README.md` (раздел «Отладочная сцена»: установка Godot, запуск, проверка)

**Interfaces:**
- Consumes: `DataLocator.Find`, `DataLoader.Load`, `SandboxSession.Create`.
- Produces:
  - `Dialoid.Game.csproj`: `Godot.NET.Sdk/<версия = godot --version>`, `net8.0`, `ProjectReference ../core/Dialoid.Sandbox/Dialoid.Sandbox.csproj`, `Nullable enable`.
  - `Main : Control`. В `_Ready()` берет путь к данным из переменной `DIALOID_DATA`, если она задана, иначе ищет через `DataLocator.Find(ProjectSettings.GlobalizePath("res://"))`; загружает их и создает сессию класса `warrior`. При ошибке показывает `Label` с текстом ошибки и проверенными путями. Ниже — пустой `TabContainer` с 7 вкладками по §13 («Персонаж», «Пассивки», «Асцендант», «Скиллы», «Симуляция», «Разбор», «Билды»). Каждую вкладку наполняет своя задача.
  - `tools/godot_check.sh [--selftest]` — шаги по §3.2:
    1. `dotnet build game/Dialoid.Game.csproj`.
    2. `timeout 120 $GODOT --headless --path game --import`.
    3. `timeout 60 $GODOT --headless --path game --quit`, либо с `--selftest`: `timeout 300 $GODOT --headless --path game -- --selftest`.
    4. Лог проверяется: `grep -E "ERROR|SCRIPT ERROR"` → код выхода 1.
    5. Код выхода самопроверки пробрасывается наружу.
    Если на macOS нет `timeout`, использовать `gtimeout` или встроенный в скрипт перл-таймаут.

- [ ] **Step 1:** Записать в отчет `godot --version`, выставить версию `Godot.NET.Sdk`.
- [ ] **Step 2:** Создать проект и `Main`.
- [ ] **Step 3:** `tools/godot_check.sh` — код выхода 0, в логе нет `ERROR`.
- [ ] **Step 4:** Временно переименовать путь поиска (подмена через переменную окружения `DIALOID_DATA` на несуществующий путь, которую `Main` читает первой). Сцена стартует, показывает ошибку, `godot_check.sh` все равно без `ERROR`.
- [ ] **Step 5:** `dotnet test core/Dialoid.sln` — зеленый.
- [ ] **Step 6: Commit** `feat(game): godot project skeleton and headless check`.

### Task 6: Панели «Персонаж», «Билды», «Разбор» и общий контрол числа

**Files:**
- Create: `game/Ui/NumberLabel.cs`, `game/Ui/BreakdownPanel.cs`, `game/Ui/CharacterPanel.cs`, `game/Ui/BuildsPanel.cs`, `game/SelfTest.cs`
- Modify: `game/Main.cs` (запуск `SelfTest`, если в `OS.GetCmdlineUserArgs()` есть `--selftest`; по окончании `GetTree().Quit(0|1)`)

**Interfaces:**
- Consumes: `SandboxSession` (задачи 2–3), `NumberLine`, `BreakdownText`, `ReferenceBuilds`, `Texts`.
- Produces:
  - `NumberLabel : Button` (плоский) — показывает `Label: value`. По нажатию вызывает `BreakdownPanel.Show(NumberLine)` и переключает на вкладку «Разбор». Если разбора нет, показывает `Note`. Этим контролом выводится **каждое** число сцены во всех панелях (§13.6).
  - `BreakdownPanel` — заголовок, строки `BreakdownText.Lines`, история последних 10 разборов (кнопки).
  - `CharacterPanel` (§13.1):
    - класс — `OptionButton`;
    - уровень — `HSlider` 1–60 и число;
    - асцендант — `OptionButton`, доступен с 20 уровня;
    - оружие — `OptionButton` по классу;
    - предметы — `CheckBox`;
    - все `CharacterLines` через `NumberLabel`;
    - список нарушений красным.
  - `BuildsPanel` (§13.7): дерево эталонов по группам, кнопка «Загрузить», `TextEdit` и кнопки «Экспорт» (заполняет поле кодом) и «Импорт» (ошибка в `Label`).
  - Все панели подписываются на `session.Changed` и перерисовываются целиком. Оптимизация не нужна.

- [ ] **Step 1:** Реализовать.
- [ ] **Step 2:** В `SelfTest.cs` (создать здесь, расширяется в задаче 9) шаги: загрузить каждый main-эталон через `BuildsPanel`, `CharacterPanel` показывает 0 нарушений, у `NumberLabel` здоровья есть разбор.
- [ ] **Step 3:** `tools/godot_check.sh --selftest` — 0.
- [ ] **Step 4: Commit** `feat(game): character, builds and breakdown panels`.

### Task 7: Панели «Пассивки» и «Асцендант»

**Files:**
- Create: `game/Ui/TreeView.cs` (общий рисовальщик графа), `game/Ui/PassivePanel.cs`, `game/Ui/AscendancyPanel.cs`

**Interfaces:**
- Consumes: `PassiveTree.Nodes/Edges`, `PassiveNode.X/Y/Kind`, `SandboxSession.TogglePassive/AllocatablePassives/PassivePoints*`, `Ascendancy`, `ToggleAscendancyNode`, `SetAttributeChoice`.
- Produces:
  - `TreeView : Control` с `_Draw`: линии ребер, кружки узлов. Радиус по `Kind`: старт и keystone крупнее, notable средний, малые мелкие. Цвета: взят, доступен, недоступен.
    - Колесо мыши — масштаб, перетаскивание — сдвиг.
    - Клик по узлу — событие `NodeClicked(id)`.
    - Наведение — подсказка с именем и описанием (`TooltipText` или своя `PanelContainer`).
    - Попадание клика: ближайший узел в пределах радиуса с учетом масштаба.
  - `PassivePanel`:
    - `TreeView` по `PassiveTree`;
    - счетчик «Очки: потрачено/всего»;
    - результат `TogglePassive` (ошибка — в строке статуса);
    - для узла `Attribute` — выбор атрибута (`PopupMenu` по правому клику).
  - `AscendancyPanel` — то же для асценданта. Координат у узлов асценданта нет, поэтому раскладка: старт в центре, notables по кругу в порядке данных.

- [ ] **Step 1:** Реализовать.
- [ ] **Step 2:** Самопроверка:
  - взять доступный узел кликом через `TreeView` (вызов обработчика по координатам узла), счетчик изменился;
  - клик по недоступному узлу дает ошибку в статусе;
  - на 20 уровне взять стартовый узел асценданта и один notable.
- [ ] **Step 3:** `tools/godot_check.sh --selftest` — 0.
- [ ] **Step 4: Commit** `feat(game): passive tree and ascendancy panels`.

### Task 8: Панели «Скиллы» и «Симуляция»

**Files:**
- Create: `game/Ui/SkillsPanel.cs`, `game/Ui/SimulationPanel.cs`

**Interfaces:**
- Consumes:
  - `SandboxSession.SetSlotSkill`, `UndoSlot`, `SetSkillLevel`, `AddSkillNodePoint`, `RemoveSkillNodePoint`, `SetRotation`, `SkillLines`;
  - `SimulationRunner`, `SimRequest`, `SimResult` (+ `Defense`, `ResourceSeries`), `Texts`.
- Produces:
  - `SkillsPanel` (§13.4):
    - 5 слотов. Закрытые уровнем показываются как «откроется на N уровне».
    - В каждом слоте: `OptionButton` скилла (родные скиллы класса и скиллы из выбранных предметов), уровень `SpinBox`, кнопка «Вернуть».
    - Дерево выбранного скилла — список узлов. У каждого: имя, `points/max`, кнопки «+» и «−», описание в подсказке. Заблокированные требованием или группой серые, с причиной в подсказке.
    - Счетчик свободных очков.
    - `SkillLines` через `NumberLabel`.
    - Редактор ротации: список скиллов слотов с кнопками «вверх» и «вниз».
  - `SimulationPanel` (§13.5):
    - Ввод: манекен (`OptionButton` из `Data.Dummies`), размер пака `SpinBox` (по умолчанию из манекена), режим (среднее или бросок), сид, флажок «манекен бьет персонажа», кнопка «Симулировать». На время расчета кнопка выключена, показан текст «считаю...».
    - Результаты (§11.3):
      - DPS по одной цели и по паку;
      - DPS по скиллам и типам урона;
      - урон попаданиями и по времени;
      - время убийства, время до смерти;
      - защитные итоги (`Defense`);
      - серия ресурса как простая линия на `Control._Draw`;
      - лог событий в `RichTextLabel` по кнопке «Показать лог».
    - Устаревший результат (`!IsCurrent`) не показывается, вместо него «билд изменился — пересчитайте».
    - Нарушения из `SimResult.Violations` показываются вместо результатов.

- [ ] **Step 1:** Реализовать.
- [ ] **Step 2:** Самопроверка:
  - вложить очко в узел дерева, `SkillLines` изменились;
  - запустить симуляцию эталона и дождаться результата, DPS > 0;
  - изменить узел до прихода второго результата — результат помечен устаревшим.
- [ ] **Step 3:** `tools/godot_check.sh --selftest` — 0.
- [ ] **Step 4: Commit** `feat(game): skills and simulation panels`.

### Task 9: Приемка §15.3–15.4, README

**Files:**
- Modify: `game/SelfTest.cs`, `README.md`, `docs/superpowers/plans/2026-10-25-dialoid-core-engine-notes.md` (отметить закрытые хвосты плана 3)

**Interfaces:**
- Consumes: всё выше.
- Produces: итоговая самопроверка с кодом выхода 0/1 и отчетом в stdout.

- [ ] **Step 1: Самопроверка §15.4.** Для каждого из 6 main-эталонов:
  1. Загрузить через `BuildsPanel` и запустить симуляцию «Тренировочного» в режиме среднего через `SimulationPanel`.
  2. Сравнить DPS панели с `Simulator.Run` по тому же файлу: должен совпасть точно (Review Focus 5).
  3. Снять один взятый notable пассивного дерева через `PassivePanel`: DPS изменился.
  4. Разбор урона главного скилла до и после отличается строкой с этим узлом.
  5. Вернуть узел, DPS вернулся.

  Отчет печатает таблицу «билд → DPS до/после, узел».
- [ ] **Step 2: Самопроверка §15.3:**
  - `tools/godot_check.sh` — без `--selftest`, без `ERROR`;
  - `tools/godot_check.sh --selftest` — из корня и с `--path game` из папки `game/`.
- [ ] **Step 3: Review Focus:**
  - мусор в импорте не меняет билд;
  - отсутствующие данные (`DIALOID_DATA` указывает на несуществующий путь) — сцена стартует с ошибкой;
  - смена класса после эталона сбрасывает билд без исключений.
- [ ] **Step 4: README:**
  - как установить Godot, открыть сцену и запустить самопроверку;
  - скриншот не нужен;
  - статус проекта: этап 1 завершен, что дальше (визуал 2D-пиксель-арт — отдельный этап, решения владельца в `docs/superpowers/content-owner-questions.md`).
- [ ] **Step 5:** `dotnet test core/Dialoid.sln` и `tools/godot_check.sh --selftest` — оба зеленые.
- [ ] **Step 6: Commit** `feat(game): acceptance self-test, readme`.

---

## Покрытие спеки

| Раздел спеки | Задача |
|---|---|
| §4.7 разбор числа | 1, 4, 6 |
| §11.3 выход симуляции (защита, серия ресурса, лог) | 1, 8 |
| §12 экспорт и импорт, эталоны, бесплатный респек | 3, 6 |
| §13.1 персонаж | 2, 6 |
| §13.2 пассивное дерево | 2, 7 |
| §13.3 асцендант | 2, 7 |
| §13.4 скиллы | 3, 8 |
| §13.5 манекен и симуляция | 4, 8 |
| §13.6 разбор по клику | 4, 6 |
| §13.7 билды | 3, 6 |
| §14.2 «вернуть» снятый скилл | 3, 8 |
| §15.3 Godot собирается без ошибок | 5, 9 |
| §15.4 собрать эталон, изменить узел, увидеть DPS и причину | 9 |
| §15.8 README | 5, 9 |
| Хвост плана 1: Explain API, NoConditions, защитные итоги, серия ресурса | 1 |
