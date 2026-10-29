# Dialoid Training Room Implementation Plan (план 4)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Отладочная сцена становится тренировочной 3D-комнатой в духе манекенов WoW и подземелий Diablo 2. Герой (модель Mixamo) бьет манекен в реальном времени, журнал боя идет в чат, измеритель как Details! показывает DPS и потерю урона на защите манекена. Окна билда открываются поверх комнаты.

> **Статус (2026-10-30).** Задачи 1–4 (ядро и песочница) выполнены на ветке `feat/training-room`. Задачи 5–12 переписаны под 3D-спецификацию (визуал сменился с 2D на 3D 2026-10-29) и под Windows как основную платформу (решение владельца 2026-10-30). Прежние задачи 5–9 (пак Anokolisa, 2D) отменены.
>
> **Где исполнять задачи 5–12.** На новой ветке от `main` после вливания `feat/training-room`. Спека — версия из `main` (коммиты 822cd31 и b710278: 3D, Forward+, «Экран боя»); старая 2D-версия на ветке задач 1–4 для задач 5–12 недействительна.

**Architecture:**
- **Ядро.** Цикл `SimRun` выносится в общий двигатель шага `CombatEngine`. Мгновенная симуляция — это тот же двигатель, где команды выдает ротация, а живой бой (`LiveFight`) получает команды игрока. Поэтому числа совпадают по построению.
- **События урона.** Каждое попадание и тик пишутся структурным событием `DamageEvent` с разложением защиты цели.
- **Песочница.** `Dialoid.Sandbox` получает `TrainingRoom`: живой бой текущего билда, сегменты, измеритель, журнал по-русски и правило досягаемости по тегам. Все это тестируется без Godot.
- **Слой Godot.** Только рисует 3D-комнату, героя, манекен, экран боя и окна, меряет расстояние и видимость манекена и передает нажатия.

**Tech Stack:** .NET 8 (`net8.0`, RollForward Major), C# 12, xUnit, Godot 4.7.2 .NET (`Godot.NET.Sdk/4.7.2`, рендерер Forward+), Windows 10 + PowerShell 5.1, модели и анимации Mixamo (FBX), CC0-текстуры Poly Haven (ассеты вне git).

**Spec:** `docs/superpowers/specs/2026-10-29-dialoid-training-room-design.md` (утверждена). Основа: `docs/superpowers/specs/2026-10-25-dialoid-core-systems-design.md` (§6, §11), план 3 `docs/superpowers/plans/2026-10-28-dialoid-debug-scene.md`.

## Global Constraints

- **Сборка и тесты.**
  - `core/` собирается с `TreatWarningsAsErrors` и `Nullable enable`.
  - `dotnet test core/Dialoid.sln` зеленый, 0 предупреждений.
  - Все существующие тесты (Core 717, Sandbox 85) остаются зелеными без изменения их утверждений.
- **Поведение `Simulator.Run` не меняется.** DPS, TTK, выживание, журнал событий и `SimResult` всех эталонов те же до бита. Публичный API ядра только дополняется.
- **Правила только в ядре и песочнице.** Перезарядка, ресурс, занятость, баффы, урон — в ядре. Досягаемость, сегменты и измеритель — в песочнице. `game/` только рисует и передает ввод.
- **Новых атрибутов и механик нет.** Блока нет, уклонения монстров нет, модификаторов манекена нет, летального режима нет. Манекен не бьет в ответ.
- **Здоровье в комнате бесконечное у всех пресетов**: ядро получает переопределение здоровья.
- **Ассеты не кладутся в git.**
  - Где искать: переменная `DIALOID_ASSETS`, иначе папка `assets/` в корне репозитория (в `.gitignore`). Модели — `assets/mixamo/`, текстуры — `assets/polyhaven/`.
  - FBX импортируются Godot только внутри `res://`: скрипт копирует нужные файлы в `game/vendor/` (в `.gitignore`) перед импортом.
  - Без ассетов комната рисуется заглушками (капсулы, однотонные материалы), а самопроверка проходит.
  - Учет лицензий — `assets/LICENSES.md` (вне git) и раздел README; без него сборки не публикуются.
- **Платформа — Windows (решение владельца 2026-10-30).** Команды — PowerShell 5.1, bash нет в PATH. Godot: переменная `GODOT`, иначе `C:\Users\sibiya666\Tools\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe`. `tools/godot_check.sh` остается для Mac без изменений поведения.
- **Godot (§3.2 спеки ядра).** Каждый вызов `godot` под таймаутом. `tools/godot_check.ps1` и `tools/godot_check.ps1 -SelfTest` дают код 0, в логе нет строк `^(SCRIPT )?ERROR:`.
- **Ядро не знает координат.** Расстояние и «манекен в кадре» считает слой игры и передает в `TrainingRoom` числами. Единица — метры.
- **Бюджет кадра (спека §4, версия b710278).** Целевая машина — ПК владельца: Windows 10, Ryzen 5 5600, RX 6750 XT. Рендерер Forward+. Полная тень у света героя; тени у источников ближе ~10 м к герою, не больше 8 теневых источников в кадре, у факелов `shadow_opacity` 0,5–0,7; остальные факелы без теней. Заполняющий свет героя и свет манекена — без теней, в лимит не входят. SSAO включен, объемный туман выключен (включается настройкой). Не больше 8 снарядов со своим светом. Пол кусками, текстуры 2K со сжатием VRAM. Приемка: худший 1% кадров ≥ 60 при 1440p в кадре с «Роем» и всеми эффектами.
- **Тексты и коммиты.** Тексты для владельца — по-русски, идентификаторы — по-английски. Коммиты в стиле conventional commits, в конце строка `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- **Паттерны плана 3 сохраняются.**
  - Панели наследуют `SessionPanel`: отложенная, склеенная перерисовка, подписка в `_EnterTree`.
  - Контрол не освобождается внутри собственного сигнала.
  - `SandboxSession` однопоточная (UI-поток).
  - Каждое число на экране выводится через `NumberLabel` из `NumberLine` или из итогов ядра.

## Review Focus

1. **Низкий FPS или долгий кадр** (вкладка свернута, отладчик). Ожидание: слой игры делает не больше `MaxCatchUpSteps` шагов ядра за кадр. Лишнее время отбрасывается, а не накапливается в лавину. Бой не «телепортируется» на минуты вперед. Тест в задаче 4 (`TrainingRoom` получает огромный `dt`).
2. **Правка билда во время канала или с нажатием в очереди.** Ожидание: бой перезапускается чисто, канал и очередь сброшены, исключений нет, сегмент закрыт. Тест в задаче 4.
3. **Клавиши боя, пока фокус в поле ввода окна** (сид, импорт, уровень скилла), **и мышь над окном.** Ожидание: цифры уходят в поле, а не в бой; клик и колесо над окном не двигают героя и не зумят камеру. Проверка в самопроверке задачи 10.
4. **Нажатие скилла, которого нет в слоте, или пустой слот (в том числе дыра между слотами).** Ожидание: отказ «пустой слот», без исключения; клавиша N всегда про слот N. Тесты в задачах 2 и 4.
5. **Частичный набор ассетов** (нет одной модели, клипа или текстуры). Ожидание: недостающее заменяется заглушкой, одна строка `ASSET_MISSING:` без `ERROR:`, сцена работает. Тест в задаче 8.
6. **Поток событий на «Рое» с недугами** (тысячи тиков в секунду). Ожидание: кадр не проседает из-за журнала и всплывающих чисел; тики схлопываются, всплывающих чисел ограниченное число. Тест в задаче 7, замер в задаче 12.

---

## Структура файлов

```
core/Dialoid.Core/Simulation/CombatEngine.cs   общий двигатель шага (из SimRun), internal
core/Dialoid.Core/Simulation/ICommandSource.cs откуда приходят команды: ротация или игрок, internal
core/Dialoid.Core/Simulation/SimRun.cs         остается: тонкая обертка «двигатель + ротация + итог»
core/Dialoid.Core/Simulation/LiveFight.cs      публичный живой бой: шаг, нажатия, события, состояние
core/Dialoid.Core/Simulation/LiveTypes.cs      LiveFightConfig, PressResult, PressRefusal, LiveState
core/Dialoid.Core/Combat/DamageEvent.cs        структурное событие урона с разложением защиты
core/Dialoid.Core.Tests/Simulation/LiveFightTests.cs, DamageEventTests.cs
core/Dialoid.Sandbox/TrainingRoom.cs           живой бой текущего билда, сегменты, ввод
core/Dialoid.Sandbox/FightSegment.cs           сегмент и его итоги (измеритель)
core/Dialoid.Sandbox/ReachRules.cs             досягаемость по тегам
core/Dialoid.Sandbox/CombatLogText.cs          строки журнала по-русски
core/Dialoid.Sandbox.Tests/TrainingRoomTests.cs, MeterTests.cs, ReachRulesTests.cs
core/Dialoid.Sandbox/CombatLogEntry.cs          структурная строка журнала, фильтры, схлопывание (задача 7)
core/Dialoid.Sandbox/RoomFeed.cs                события для визуала и экрана боя (задача 7)
tools/godot_check.ps1                           проверка сборки и самопроверки на Windows (задача 5)
tools/sync_assets.ps1                           копия FBX и текстур из assets/ в game/vendor/ (задача 8)
game/Room/AssetLibrary.cs                       поиск ассетов, модели, клипы, материалы, заглушки
game/Room/RoomView.cs                           3D-комната из примитивов, свет, факелы, загораживание
game/Room/CameraRig.cs                          ортокамера под углом, зум, «манекен в кадре»
game/Room/HeroView.cs                           герой: движение, поворот, анимации, контур
game/Room/DummyView.cs                          манекен по пресету, реакция, вспышки
game/Room/SkillAnimations.cs, game/data/skill_animations.json   скилл → клип (данные игрового слоя)
game/Room/ProjectileView.cs, FloatingNumbers.cs снаряды, всплывающие числа
game/Room/CombatDriver.cs                       fixed timestep, ввод, досягаемость, связка с визуалом
game/Ui/WindowHost.cs, TopBar.cs                окна поверх комнаты, клавиши, верхняя панель
game/Ui/CombatHud.cs                            панель 1–5, ресурс, баффы, плашка манекена, отказы
game/Ui/CombatLogWindow.cs, MeterWindow.cs, ChartsWindow.cs
game/Main.cs, game/SelfTest.cs                  переделка корня; самопроверка
```

---

### Task 1: Общий двигатель шага в ядре

**Files:**
- Create: `core/Dialoid.Core/Simulation/CombatEngine.cs`, `ICommandSource.cs`
- Modify: `core/Dialoid.Core/Simulation/SimRun.cs` (цикл и `Act` переезжают в двигатель), `Rotation.cs` (реализует `ICommandSource`)
- Test: `core/Dialoid.Core.Tests/Simulation/CombatEngineTests.cs`

**Interfaces:**
- Consumes: всё, что сейчас использует `SimRun` (`SimState`, `UseResolver`, `Procs`, `MechanicRuntime`, `DamageLedger`, `SimLog`, `SimConditionContext`).
- Produces (internal):
  - `interface ICommandSource { SkillRuntime? Next(SimState s, IConditionContext c, WeaponStub? weapon); }` — источник команд. `Rotation` реализует его через существующий `Pick`.
  - `sealed class CombatEngine`:
    - `CombatEngine(GameData data, MechanicRegistry mechanics, CompiledCharacter character, WeaponStub? weapon, IReadOnlyList<SkillRuntime> skills, DummyDefinition dummy, SimConfig config, bool infiniteLife)`;
    - `bool Step(ICommandSource commands)` — один шаг `config.StepSec`. Порядок действий тот же, что сейчас в `Execute`: Act → наблюдатель → смерти → тики целей, ресурса, баффов, механик, жизни → атака манекена → смерти → выборка. Возвращает `false`, когда бой окончен (смерть цели без возрождения, смерть персонажа);
    - `double Time`;
    - `SimResult Result()` — итог, который сейчас собирается в конце `Execute`.
  - `SimRun.Execute()` = цикл `while (i < steps && engine.Step(rotation))` + `engine.Result()`.
  - Флаг `infiniteLife`: цели не умирают (здоровье не убывает ниже максимума или смерть игнорируется — **[Билдеру]**). Урон в учет идет как обычно.

- [ ] **Step 1: Тест неизменности.** `Engine_extraction_keeps_every_reference_result_identical`. Для каждого файла `data/builds/**/*.json`, каждого пресета манекена и обоих режимов (`Average`, `Roll` с сидом 1 и 7) результат `Simulator.Run` после переделки совпадает с результатом до нее:
  - DPS, `DamageBySkill`, `DamageByType`, TTK, TTD;
  - `Defense`, `ResourceSeries`;
  - журнал: количество событий и первые 200 событий.

  Эталон снять до переделки в JSON-файл `core/Dialoid.Core.Tests/Simulation/engine-baseline.json`, генератором-тестом, который включается флагом окружения `DIALOID_WRITE_BASELINE=1`.
- [ ] **Step 2:** снять эталон на текущем коде, закоммитить файл эталона отдельно (`test(core): engine baseline`).
- [ ] **Step 3:** вынести цикл в `CombatEngine`, `SimRun` оставить оберткой.
- [ ] **Step 4:** тест неизменности и полный прогон зеленые.
- [ ] **Step 5: Тест бесконечного здоровья.** `Infinite_life_only_removes_target_death`: на пресете «Босс» с `infiniteLife` бой идет всю длительность, TTK = null, урон в первые N секунд до смерти без флага совпадает с уроном с флагом.
- [ ] **Step 6: Commit** `refactor(core): shared combat step engine`.

### Task 2: Живой бой в ядре

**Files:**
- Create: `core/Dialoid.Core/Simulation/LiveFight.cs`, `LiveTypes.cs`; `core/Dialoid.Core/Simulation/PlayerCommands.cs` (internal `ICommandSource` с очередью)
- Test: `core/Dialoid.Core.Tests/Simulation/LiveFightTests.cs`

**Interfaces:**
- Consumes: `CombatEngine`, `ICommandSource` (задача 1), `BuildRules`, `CharacterCompiler`.
- Produces (public):
  - `public sealed record LiveFightConfig(string DummyId, SimMode Mode, int Seed = 1, int? PackSizeOverride = null, bool InfiniteLife = true)`. Шаг всегда 0.05 с (`LiveFight.StepSec`).
  - `public enum PressRefusal { None, EmptySlot, Cooldown, NotEnoughResource, Busy, BuffActive, WeaponMismatch, TriggeredOnly, OutOfReach, FightOver }`. `OutOfReach` ставит песочница, ядро его не выдает.
  - `public sealed record PressResult(bool Accepted, bool Queued, PressRefusal Refusal, double? CooldownLeft)`.
  - `public sealed record LiveState(double Time, double Resource, double ResourceMax, double Life, IReadOnlyDictionary<int,double> CooldownLeftBySlot, IReadOnlyDictionary<string,double> Buffs, bool Busy, string? ChannelSkillId, IReadOnlyList<string> TargetAilments)`.
  - `public sealed class LiveFight`:
    - `public const double StepSec = 0.05`, `public const double QueueWindowSec = 0.4`;
    - `static LiveFight? Start(GameData data, MechanicRegistry mechanics, Build build, LiveFightConfig config, out IReadOnlyList<RuleViolation> violations)` — те же проверки, что в `Simulator.Run`;
    - `PressResult Press(int slot)`;
    - `void StartChannel(int slot)`, `void StopChannel()`;
    - `void Step()` — один шаг двигателя;
    - `IReadOnlyList<SimEvent> DrainEvents()`, `IReadOnlyList<DamageEvent> DrainDamage()` (задача 3; до нее — пустой список);
    - `LiveState State`, `SimResult Snapshot()` — итоги на текущий момент.
  - **Правила нажатия** (спецификация §6.2в). Проверки идут через те же функции, что `Rotation.Pick` (перезарядка, ресурс, оружие, бафф). Если персонаж занят и до освобождения ≤ `QueueWindowSec`, нажатие ставится в очередь: новое заменяет старое, `Queued = true`. Иначе отказ `Busy`. Пустой слот дает `EmptySlot`, скилл, который только срабатывает от других, — `TriggeredOnly`.

- [ ] **Step 1: Тесты.**
  - `Live_fight_driven_by_rotation_matches_simulator`: `LiveFight`, куда каждый шаг подается `Rotation.Pick` (через внутренний вход для тестов), дает тот же `SimResult`, что `Simulator.Run`, на 6 основных эталонах. Режимы `Average` и `Roll`, сид 1, без бесконечного здоровья.
  - `Press_on_cooldown_is_refused_with_time_left`.
  - `Press_while_busy_within_window_is_queued_and_fires_on_release`.
  - `Press_while_busy_beyond_window_is_refused_busy`.
  - `Newer_queued_press_replaces_older`.
  - `Buff_skill_with_active_buff_is_refused`: Боевой клич.
  - `Empty_slot_and_triggered_only_are_refused`.
  - `Not_enough_resource_is_refused`.
  - `Channel_runs_while_held_and_stops_on_release`: Вихрь.
  - `Infinite_life_fight_never_ends`: 120 с на «Босс».
- [ ] **Step 2:** убедиться, что падают. **Step 3:** реализовать. **Step 4:** полный прогон зеленый.
- [ ] **Step 5: Commit** `feat(core): live fight with player commands`.

### Task 3: Разложение защиты цели в событиях урона

**Files:**
- Create: `core/Dialoid.Core/Combat/DamageEvent.cs`
- Modify:
  - `core/Dialoid.Core/Combat/HitCalculator.cs`, `Ailments.cs` — возвращают разложение, порядок умножений не трогается;
  - `UseResolver.cs`, `CombatEngine.cs` — собирают события;
  - `SimResult.cs` — добавляется `MitigationTotals`.
- Test: `core/Dialoid.Core.Tests/Combat/DamageEventTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record DamagePart(DamageType Type, double BeforeDefense, double ByArmor, double ByResist, double ByTakenMultiplier, double Nullified, double Final)`.
    - `ByTakenMultiplier` отрицателен, когда множитель > 1 (дебафф добавил урон).
    - Тождество: `BeforeDefense − ByArmor − ByResist − ByTakenMultiplier − Nullified = Final` с точностью 1e-9 относительно.
  - `public sealed record DamageEvent(double Time, string SkillId, int Target, bool IsDot, bool IsCrit, IReadOnlyList<DamagePart> Parts)`.
  - `public sealed record MitigationTotals(IReadOnlyDictionary<string, DamagePart> BySkill, DamagePart Total)` — части, просуммированные по всем типам. `SimResult.Mitigation` — `init`-свойство, по умолчанию пустое.
  - `LiveFight.DrainDamage()` отдает события с прошлого вызова.
  - Обнуленный урон считается по части, которую `HitCalculator` пропускает из-за `NullifiedDamageTypes`: ее величина до защиты.

- [ ] **Step 1: Тесты.**
  - `Damage_parts_satisfy_identity_on_every_reference_and_preset`.
  - `Final_sum_of_events_equals_damage_by_skill`: сумма `Final` по событиям скилла равна `DamageBySkill` с точностью 1e-6 относительно.
  - `Armor_cuts_physical_resist_cuts_elemental`: «Бронированный» и «Стихийный».
  - `Shock_shows_negative_taken_multiplier_part`.
  - `Avatar_of_fire_reports_nullified_physical`: keystone-билд из `data/builds/keystones/`.
  - `Simulator_numbers_unchanged`: базовая линия из задачи 1 по-прежнему совпадает.
- [ ] **Step 2–4:** убедиться, что падают, реализовать, полный прогон.
- [ ] **Step 5: Commit** `feat(core): per-hit defense breakdown`.

### Task 4: Песочница — тренировочная комната

**Files:**
- Create: `core/Dialoid.Sandbox/TrainingRoom.cs`, `FightSegment.cs`, `ReachRules.cs`, `CombatLogText.cs`; тесты `TrainingRoomTests.cs`, `MeterTests.cs`, `ReachRulesTests.cs`
- Modify: `core/Dialoid.Sandbox/Texts.cs` (тексты отказов, журнала, названия режимов измерителя)

**Interfaces:**
- Consumes: `SandboxSession` (Build, Revision, Changed, Data), `LiveFight` (задача 2), `DamageEvent` (задача 3).
- Produces:
  - `public static class ReachRules { public const double MeleeTiles = 2; public static PressRefusal Check(TagSet compiledTags, double distanceTiles, bool targetOnScreen); }` — по тегам **скомпилированного** скилла (спецификация §6.3):
    - `projectile` → всегда можно (снаряд может улететь за экран); важнее `melee`;
    - `melee` без `projectile` → ≤ `MeleeTiles`, иначе `OutOfReach`;
    - заклинание без `projectile` (наведение на цель/землю: Метеор, Ядовитый туман) → только если `targetOnScreen`, иначе `OutOfReach`;
    - клич/бафф на себя → всегда.
  - `public sealed record MeterRow(string Key, string Label, double Value, double Share)`.
  - `public sealed class FightSegment`:
    - `int Index`, `double Start`, `double End`, `double Duration`;
    - `IReadOnlyList<SimEvent> Events`, `IReadOnlyList<DamageEvent> Damage`;
    - `IReadOnlyList<MeterRow> DpsBySkill()`, `DamageBySkill()`, `DamageByType()`, `Mitigation()` (строки: «дошло», «броня», «сопротивления», «множитель получаемого урона», «обнулено»);
    - `IReadOnlyList<(double Time, double Damage)> DamageOverTime(double bucketSec)`, `IReadOnlyDictionary<string, DamagePart> MitigationBySkill()`.
  - `public sealed class TrainingRoom`:
    - `public const double SegmentIdleSec = 5`, `public const int MaxSegments = 10`, `public const int MaxCatchUpSteps = 20`;
    - `TrainingRoom(SandboxSession session)`; выбирает пресет `SetDummy(string dummyId)`, `SetMode(SimMode mode, int seed)`;
    - `void Advance(double realDt)` — накапливает время и делает ≤ `MaxCatchUpSteps` шагов `LiveFight` (остаток отбрасывается);
    - `PressResult Press(int slot, double distanceTiles, bool targetOnScreen)` — сначала досягаемость (иначе отказ `OutOfReach` и строка журнала), потом `LiveFight.Press`;
    - `StartChannel(int slot, double distanceTiles, bool targetOnScreen)`, `StopChannel()`;
    - `IReadOnlyList<FightSegment> Segments`, `FightSegment? Current`, `void ResetMeter()`;
    - `IReadOnlyList<string> LogLines` — последние строки журнала по-русски (ограничение 2000);
    - `event Action? Updated`.
  - **Сегменты.** Сегмент начинается с первого `DamageEvent` и закрывается через `SegmentIdleSec` без урона. DPS = урон / (последний урон − первый урон, минимум 1 шаг).
  - **Правка билда** (`session.Changed`) закрывает сегмент, перезапускает `LiveFight` с новым билдом и пишет строку «Билд изменен — бой начат заново». Подписка и отписка — явными методами `Attach()`/`Detach()`; комната однопоточная.
  - `CombatLogText.Line(SimEvent)` и `Line(DamageEvent)` дают строки вида «12,4 с · Огненный шар · попадание · 812 огнем (крит)» через `RuNumber` и `BreakdownText`.

- [ ] **Step 1: Тесты.**
  - `Meter_sums_equal_log_sums_for_segment`.
  - `Segment_closes_after_idle_and_new_one_starts_on_next_hit`.
  - `Keeps_last_ten_segments_and_reset_clears`.
  - `Build_change_restarts_fight_closes_segment_and_logs_line`.
  - `Build_change_during_channel_and_queued_press_is_clean`.
  - `Huge_dt_is_capped_to_max_catch_up_steps`.
  - `Out_of_reach_melee_is_refused_and_logged`, `Projectile_reaches_any_distance_even_off_screen`, `Ground_spell_needs_target_on_screen` (Метеор), `Meteor_with_starfall_becomes_projectile_and_ignores_screen`, `Shockwave_crush_counts_as_projectile`.
  - `Mitigation_rows_add_up_to_before_defense`.
  - `Log_lines_are_russian_and_capped`.
  - `Illegal_build_starts_no_fight_and_reports_violations`.
- [ ] **Step 2–4:** убедиться, что падают, реализовать, полный прогон.
- [ ] **Step 5: Commit** `feat(sandbox): training room with segments and meter`.

### Task 5: Проверка сборки и самопроверки на Windows

Первая из визуальных задач: без нее задачи 6–12 нечем проверить на машине владельца.

**Files:**
- Create: `tools/godot_check.ps1`
- Modify: `README.md` (разделы «Отладочная сцена» и «Проверка»: команды PowerShell, путь к Godot для Windows; команды для Mac остаются отдельным подразделом), `.gitignore` (строки `assets/`, `spike-3d/`, `game/vendor/`, если их еще нет)

**Interfaces:**
- Produces: `powershell -NoProfile -ExecutionPolicy Bypass -File tools/godot_check.ps1 [-SelfTest] [-ExpectLoadFailure] [-Screenshots <dir>] [-ImportTimeoutSec <n>]`. Тот же порядок шагов, лимиты и коды выхода, что у `tools/godot_check.sh`:
  - шаги: `dotnet build game/Dialoid.Game.csproj` (600 с) → `godot --headless --path game --import` (`-ImportTimeoutSec`, по умолчанию 600 с: первый импорт десятков FBX долгий) → `godot --headless --path game --quit` (60 с) или `-- --selftest` (300 с);
  - ловушки PowerShell 5.1: у `Start-Process -PassThru` прочитать `$p.Handle` сразу после запуска, иначе после `WaitForExit` `ExitCode` бывает `$null`; stdout и stderr — в разные временные файлы (`-RedirectStandardOutput`/`-RedirectStandardError`), лог читается `Get-Content -Encoding UTF8`;
  - коды: 0 — успех; 1 — сборка или импорт не прошли, в логе строка `^(SCRIPT )?ERROR:` (после снятия ANSI-кодов) или с `-ExpectLoadFailure` данные загрузились; 2 — Godot не найден или неверный аргумент; 3 — строка `DIALOID_LOAD_FAILED:` в логе; иначе — код самопроверки (он важнее проверок лога); 124 — шаг не уложился в лимит (процесс и его дочерние убиты);
  - Godot: `$env:GODOT`, иначе путь из Global Constraints, иначе `godot` в PATH; сообщение об ошибке по-русски с этими путями;
  - лимит — через `Start-Process -PassThru` + `WaitForExit(ms)` и `taskkill /T /F` при превышении; вывод Godot пишется во временный файл и печатается целиком;
  - `-Screenshots <dir>` запускает Godot в окне (не headless) с `-- --selftest --screenshots <dir>` (300 с) — замена ручной команды из README плана 3.
- Скрипт работает в Windows PowerShell 5.1 (без `&&`, `??`, тернарного оператора), файл в UTF-8 с BOM, чтобы русские сообщения читались.

- [ ] **Step 1:** написать скрипт.
- [ ] **Step 2: Проверка вручную, вывод в отчет:**
  - `tools/godot_check.ps1` → 0;
  - `tools/godot_check.ps1 -SelfTest` → 0 (все шаги самопроверки плана 3 зеленые на Windows);
  - `$env:DIALOID_DATA='C:\nonexistent'; tools/godot_check.ps1 -ExpectLoadFailure` → 0, без `-ExpectLoadFailure` → 3;
  - `$env:GODOT='C:\nope.exe'` → 2;
  - неизвестный аргумент → 2.
- [ ] **Step 3: README** — команды под PowerShell, коды выхода, где Godot на Windows.
- [ ] **Step 4: Commit** `build(tools): godot check for windows powershell`.

### Task 6: Ядро — состояние и события для экрана боя

Экран боя (спека §4 «Экран боя») и визуал (§5) должны брать всё из ядра, а не разбирать текст журнала (замечание техревью: `CombatLogText` разбирает английский текст `SimEvent`).

**Files:**
- Modify: `core/Dialoid.Core/Simulation/SimConfig.cs` (`SimEvent`), `SimLog.cs`, места `Log.Add` в `UseResolver.cs`, `Procs.cs`, `MechanicSimContext`/`CombatEngine.cs`, `core/Dialoid.Core/Combat/DamageEvent.cs`, `LiveTypes.cs`, `LiveFight.cs`
- Test: `core/Dialoid.Core.Tests/Simulation/LiveFightTests.cs`, `LiveStateTests.cs`

**Interfaces:**
- Produces (public, только дополнения):
  - `SimEvent` получает необязательные `init`-поля: `string? SkillId`, `int? Target`, `string? AilmentId`, `string? SourceSkillId`, `string? MechanicKey`, `long? DamageSeq`, `double? UseSec`. `Text` и `Kind` остаются как есть. Хеш эталона уже строится явно из `Time|Kind|Text` (`CombatEngineTests.cs` ~146), поэтому новые поля его не меняют; тест неизменности зеленый **без** перегенерации `engine-baseline.json`.
  - `DamageSeq` — сквозной номер попадания: у события `hit`/`crit` и у соответствующего `DamageEvent` он один и тот же. Это заменяет нынешнюю склейку по префиксу текста в `TrainingRoom` (попадание бывает многотипным, поэтому одиночного типа урона у `hit` нет — типы берутся из `DamageEvent.Parts`).
  - `UseSec` у события `use` — фактическая занятость этого применения из `CombatEngine` (с условными бонусами скорости), по ней сцена растягивает анимацию.
  - `DamageEvent` получает `init`-поля `string? AilmentId` и `string? ZoneId` (для тиков недугов и зон) и `long Seq`.
  - Строки механик: у события механики `MechanicKey` = id механики из данных; русский текст строится из имени механики в данных и числа счетчика, без английской детали. Отдельного API «стак с таймером» у механик не появляется.
  - `public sealed record SlotInfo(int Slot, string SkillId, string Name, TagSet Tags, double Cost, double CostPerSec, double CooldownSec, double UseTimeSec, bool IsChannel)` и `LiveFight.Slots` — по одному на заполненный слот боя (для подсказок; фактическое время применения — `UseSec` события).
  - `public sealed record TimedEffect(string Id, double? TimeLeft, int Stacks)`. Баффы и недуги — с таймером; счетчики механик (`MechanicCounters`) — как стаки с `TimeLeft = null`, но только из явного списка показываемых счетчиков с русским именем (в `Texts`); служебные счетчики (например, счетчик применений в `PyroMechanics`) скрыты, тест: у каждого показанного стака на 6 эталонах есть русское имя; ключевые вклады множителя получаемого урона на цели — как дебафф без таймера.
  - `LiveState` дополняется (новый `init`, старый конструктор работает): `string ResourceId`, `double BusyLeft`, `int? QueuedSlot`, `int? HeldSlot` (слоты боя), `IReadOnlyList<TimedEffect> HeroEffects`, `IReadOnlyList<IReadOnlyList<TimedEffect>> TargetEffects` (по каждой цели, упорядочено по id ordinal).
  - `LiveFight.State` кешируется до следующего `Step` **или** любой команды (`Press`, `StartChannel`, `StopChannel`): после нажатия `QueuedSlot`/`HeldSlot` видны сразу.

- [ ] **Step 1: Тесты.**
  - `Structured_fields_are_set_on_use_hit_ailment_proc_and_mechanic_events` — на каждом из 6 эталонов у событий `use`/`hit`/`crit`/`ailment`/`proc` заполнены `SkillId`, у `use` — `UseSec > 0`, у `hit` — `Target` и `DamageSeq`, у механик — `MechanicKey`.
  - `Every_hit_has_exactly_one_damage_event_with_same_seq` и `Dot_damage_events_carry_ailment_or_zone_id`.
  - `State_cache_is_dropped_by_commands` — после `Press` в занятом состоянии `State.QueuedSlot` виден без шага.
  - `Engine_extraction_keeps_every_reference_result_identical` зеленый без перегенерации эталона.
  - `Slot_info_matches_compiled_skill` — Огненный шар: стоимость и время применения совпадают с `SkillSheet` того же билда.
  - `State_reports_queue_hold_busy_and_timers` — очередь, канал Вихря, бафф Боевого клича с таймером, поджог на цели со стаками.
  - `State_is_cached_within_a_step` — два обращения подряд без шага возвращают один объект.
- [ ] **Step 2–4:** убедиться, что падают, реализовать, полный прогон.
- [ ] **Step 5: Commit** `feat(core): structured live events and combat screen state`.

### Task 7: Песочница — комната под 3D

**Files:**
- Create: `core/Dialoid.Sandbox/CombatLogEntry.cs`, `RoomFeed.cs`
- Modify: `TrainingRoom.cs` (журнал выносится в отдельный класс — замечание техревью о 300 строках), `CombatLogText.cs` (строки из структурных полей, без разбора английского текста), `ReachRules.cs`, `FightSegment.cs`, `Texts.cs`, `core/Dialoid.Core/Simulation/LiveTypes.cs` (одно значение `PressRefusal`)
- Test: `TrainingRoomTests.cs`, `MeterTests.cs`, `ReachRulesTests.cs`, `CombatLogTests.cs`

**Interfaces:**
- Produces:
  - **Досягаемость в метрах** (спека §6.3): `ReachRules.MeleeReachMeters = 2`; параметр `distanceTiles` переименован в `distanceMeters` во всех методах `TrainingRoom` и `ReachRules`. Слой игры передает расстояние **от края коллизии ближайшей фигуры манекена** (у босса свой радиус; на «Рое» — ближайшая из пяти). «Манекен в кадре» — хотя бы одна фигура в кадре. `PressRefusal.TargetNotVisible` добавляется в конец перечисления: заклинание без `projectile` при манекене вне кадра; `OutOfReach` — только ближний бой дальше `MeleeReachMeters`. Тексты: «Цель не видна», «Вне досягаемости».
  - `public enum LogKind { Use, Hit, Crit, Ailment, DotTick, Proc, Mechanic, Refusal, System }`.
  - `public sealed record CombatLogEntry(int? Segment, double Time, LogKind Kind, string? SkillId, int? Target, double? Amount, string Text, int Repeat)`; `Text` — готовая русская строка («12,4 с · Огненный шар · попадание · 812 огнем (крит)»); на «Рое» в строке номер цели («цель 3»). `Amount` — точное число урона строки (для сверки с измерителем), `Time` — от начала сегмента; строки вне сегмента (`use` и отказы до первого удара, после закрытия) имеют `Segment = null` и время боя.
  - Журнал (спека §7): числа с пробелом между разрядами; одинаковые отказы подряд схлопываются в одну строку с `Repeat` («отказ: перезарядка ×6»); тики недугов и зон идут строками `DotTick`, **агрегированными** по (скилл, `AilmentId`/`ZoneId`, цель) за 1 с, чтобы не топить окно (отменяет правило R7: тики теперь есть, но скрыты фильтром по умолчанию). Попадания склеиваются с `DamageEvent` по `DamageSeq`, без разбора текста; строки механик — по `MechanicKey`.
  - `TrainingRoom.Log` — `IReadOnlyList<CombatLogEntry>` (последние 2000), `LogLines` остается как текст тех же строк; `Filter(ISet<LogKind> kinds, string? skillId)` → строки; `DefaultHiddenKinds = { DotTick }`.
  - `RoomFeed` — то, что сцена забирает каждый кадр; доступен как `TrainingRoom.Feed`, метод `IReadOnlyList<VisualEvent> DrainVisual()`. `VisualEvent` — запись с `Kind` (`SkillUsed`, `Hit`, `AilmentApplied`, `Refused`), игровым слотом, `SkillId`, целью, главным типом урона (по наибольшей части `Final`), числом, критом, `IsDot`, `UseSec` (из события `use`), отказом и его русским текстом. Очередь ограничена (например, 512, старые выбрасываются); всплывающих чисел за кадр не больше 30 — лишние `Hit` по одной цели сливаются.
  - `TrainingRoom.Slots` — `SlotInfo` по **игровым** слотам 1–5 (null для пустого) с русским именем ресурса («Мана», «Ярость»); `TrainingRoom.State` — состояние с перезарядками, очередью и каналом в игровых слотах.
  - **Сегменты** (спека §6.4): `FightSegment.DummyId`, `bool AfterBuildChange`, `string Label` («№4 · 0:42 · Бронированный · после правки билда»), `double? DpsChangeVsPrevious` (доля, +0,12 = «DPS +12%»). `SetDummy` закрывает сегмент так же, как правка билда.
  - **Измеритель** (спека §8.1): `MeterRow` получает `bool IsBonus`; в режиме «Потеря на защите» прибавка от множителя получаемого урона — отдельная строка «+N от уязвимости» с положительным числом и `IsBonus = true`; отрицательная потеря на сопротивлении (пробивание ниже нуля) — так же отдельной строкой «+N от пробивания»; подпись режима говорит, что проценты не дают 100. `FightSegment.DamageOverTime(double bucketSec, string? skillId)` — серия по скиллу для графика §8.2 (по каждому скиллу своя ограниченная серия).
  - Хвосты из журнала ревью: `_heldSlot` снимается, когда бой сам закончил канал; `StartChannel` вне досягаемости при повторных вызовах пишет одну строку (через схлопывание).
  - `RuNumber.Short(double)` в ядре — «1,2 тыс.», «3,4 млн» (если такого метода нет).
- [ ] **Step 1: Тесты.**
  - `Log_lines_come_from_structured_fields` — у каждой строки журнала 6 эталонов × всех пресетов нет латиницы и нет голого «механика» (сохранить существующую теорию, перевести на новые строки).
  - `Identical_refusals_collapse_with_count`, `Swarm_lines_carry_target_number`, `Dot_ticks_aggregate_per_second_and_are_hidden_by_default`, `Segment_time_starts_at_zero`.
  - `Melee_out_of_reach_in_meters`, `Ground_spell_off_screen_is_target_not_visible`, `Projectile_ignores_screen_and_distance` (с дырой в слотах, как в финальном ревью задач 1–4).
  - `Visual_feed_reports_use_hit_and_refusal_in_order`, `Visual_feed_is_bounded`, `Floating_hits_per_frame_are_capped_on_swarm`.
  - `Segment_label_and_dps_change`, `Dummy_change_closes_segment`.
  - `Vulnerability_is_a_positive_bonus_row`, `Per_skill_damage_over_time_sums_to_skill_total`.
  - `Meter_sums_equal_log_sums_for_segment` по-прежнему зеленый, плюс `Log_amounts_sum_to_meter_on_short_segment` — по полю `Amount` видимых и скрытых строк короткого (≤ 2000 строк) сегмента.
  - `Lines_outside_segment_have_no_segment`.
  - `Swarm_reach_uses_nearest_figure` (через переданное слоем расстояние — проверяется только, что правило не зависит от номера цели).
  - `Negative_resist_loss_is_a_penetration_bonus_row`.
- [ ] **Step 2–4:** убедиться, что падают, реализовать, полный прогон.
- [ ] **Step 5: Commit** `feat(sandbox): training room log, feed and meter for the 3d room`.

### Task 8: Ассеты и 3D-комната

**Files:**
- Create: `tools/sync_assets.ps1`, `game/Room/AssetLibrary.cs`, `RoomView.cs`, `CameraRig.cs`
- Modify: `tools/godot_check.ps1` (вызывает `sync_assets.ps1` перед импортом), `game/project.godot` (рендерер Forward+, `rendering/textures/vram_compression`, размер `positional_shadow_atlas` под 8 теневых источников), `game/Main.cs`, `game/SelfTest.cs`, `README.md` («Ассеты комнаты»: какие файлы скачать с Mixamo и с какими настройками — FBX Binary, T-pose, With Skin для персонажей, Without Skin для клипов, 30 fps; какие текстуры Poly Haven, 2K; куда положить)

**Interfaces:**
- Produces:
  - `tools/sync_assets.ps1` — находит ассеты (`$env:DIALOID_ASSETS`, иначе `assets/` в корне), копирует в `game/vendor/` **только файлы из таблицы** `game/data/assets.json` (не весь пакет), если новее; имена нормализует (нижний регистр, пробелы и скобки → `_`: «sword and shield slash (2).fbx» → `sword_and_shield_slash_2.fbx`); печатает список отсутствующих; если нет `assets/LICENSES.md` — строка-предупреждение. Без ассетов — код 0 и строка «ассеты не найдены, будут заглушки».
  - **Импорт FBX.** `game/vendor/` вне git, поэтому `.import`-настройки там не хранятся: всё, что нужно, делает код в рантайме. В FBX от Mixamo клип называется `mixamo.com` — имя клипа берется из имени файла. Зацикливание (idle/walk/run/zombie idle) ставится в коде. Корень прибивается в коде: у трека позиции `mixamorig:Hips` обнуляются X и Z, Y остается (покачивание при беге). Префикс костей бывает `mixamorig:`, `mixamorig1:`, `mixamorig2:` — пути треков переписываются под скелет модели. Все эти правки делаются над **копией** `Animation` (`Duplicate`), а не над общим ресурсом из кеша `ResourceLoader`, иначе вторая модель получит клип, обработанный дважды. Каждый `Load` — только после `ResourceLoader.Exists`, иначе Godot печатает `ERROR:`.
  - `AssetLibrary` (Godot): `bool HasModels`, `bool HasTextures`; `Node3D Character(string id)` — модель из `res://vendor/…`, масштаб по **измеренной** высоте AABB модели до роста из спеки §4 (герой 1,85 м, мертвец 1,8 м, орк 2,25 м, босс — орк ×1,4 ≈ 3,15 м; не полагаться на «Mixamo в сантиметрах» — импорт может сам перевести в метры), либо заглушка-капсула того же роста; `Animation? Clip(string id)` — либо null; `Material Surface(string id)` — triplanar-материал Poly Haven или однотонный. Для каждого отсутствующего ассета — одна строка `GD.Print("ASSET_MISSING: …")`, без `PushError`. Таблица id → файл — в `game/data/assets.json` (имена файлов как на диске: `Paladin WProp J Nordstrom.fbx` и т. д.).
  - `RoomView : Node3D` — комната ~24×18 м по спеке §4: пол кусками из плит 0,75–1 м, стены с пилястрами, 4–5 колонн с крышками, бочки, факелы на стойках и стенах с мерцанием; тени по бюджету (Global Constraints): у факелов ближе ~10 м к герою тень с `shadow_opacity` 0,5–0,7, остальные без теней, всего теневых ≤ 8 — набор переключается по мере движения героя; цвет света факелов отличается от оранжевой вспышки огня по манекену; частицы огня с цветом ≤ 1 (без белых пятен); декали крови (процедурная текстура); окружение: почти черный фон, слабый холодный общий свет, filmic, свечение с порогом, SSAO включен, объемный туман выключен (флаг). Стены и колонны — коллизии. Загораживание: колонна или стена между камерой и героем/манекеном переходит в alpha-hash дизеринг. Огненных стен нет. `Vector3 HeroSpawn`, `Vector3 DummySpot`.
  - **Как комната сосуществует со старыми вкладками до задачи 10:** в задачах 8–9 `Main` добавляет первую вкладку «Комната» с `SubViewportContainer` и 3D-сценой внутри; остальные вкладки плана 3 не меняются. Самопроверка водит героя и жмет скиллы через методы `CombatDriver`/`HeroView`, а не через глобальный `Input`, поэтому поля ввода других вкладок фокус не перехватывают. Задача 10 заменяет вкладки окнами.
  - `CameraRig : Node3D` — ортографическая камера, направление около (18, 17, 18) от героя, следует за героем; размер кадра по умолчанию 10,5 м по высоте; колесо меняет зум в пределах, при которых манекен в радиусе 8 м от героя всегда в кадре; средняя кнопка — зум по умолчанию. `bool IsOnScreen(Vector3 worldPoint)` — точка внутри прямоугольника камеры (окна не учитываются, спека §6.3). `float Zoom`, `float MinZoom`, `float MaxZoom`.
- [ ] **Step 1:** реализовать.
- [ ] **Step 2: Самопроверка.**
  - Комната строится с ассетами и без них: `DIALOID_ASSETS` на пустую папку → капсулы и однотонные материалы, без `ERROR:`.
  - Частичный набор (нет одного клипа и одной текстуры) → заглушки и ровно по одной строке `ASSET_MISSING:` (Review Focus 5).
  - `IsOnScreen(DummySpot)` истинно при герое у манекена; `IsOnScreen` ложно для точки за пределами кадра (например, в 30 м от героя вдоль оси экрана — вне комнаты, проверяется сама проекция).
  - Минимальный и максимальный зум: манекен в 8 м от героя в кадре.
  - Теневых источников ≤ 8; у света героя полная тень; у всех теневых факелов `shadow_opacity` в 0,5–0,7.
  - Масштаб: высота AABB героя, мертвеца, орка и босса в пределах ±5 % от 1,85 / 1,8 / 2,25 / 3,15 м.
  - Каждый клип из таблицы находит кости у каждой из трех моделей (нет треков с ненайденным путем).
  - Загораживание: герой поставлен за колонну относительно камеры → материал колонны в режиме alpha-hash; герой вышел — материал обычный.
- [ ] **Step 3:** `tools/godot_check.ps1 -SelfTest` — 0. **Step 4: Commit** `feat(game): 3d dungeon room, camera and asset loading`.

### Task 9: Герой, манекен, анимации и живой бой в сцене

**Files:**
- Create: `game/Room/HeroView.cs`, `DummyView.cs`, `SkillAnimations.cs`, `game/data/skill_animations.json`, `ProjectileView.cs`, `FloatingNumbers.cs`, `CombatDriver.cs`
- Modify: `game/Main.cs`, `game/SelfTest.cs`

**Interfaces:**
- Consumes: `TrainingRoom` (`Advance`, `Press`, `StartChannel`, `StopChannel`, `UpdateReach`, `Feed.DrainVisual`, `Slots`, `State`), `CameraRig.IsOnScreen`, `AssetLibrary`.
- Produces:
  - `HeroView : CharacterBody3D` — Паладин; WASD (относительно камеры) и ЛКМ по полу: клик — идти, удержание — бежать за курсором; ЛКМ по манекену — подойти на дистанцию ближнего боя; плавный поворот по движению или к манекену при скилле; клипы `sword and shield idle/walk/run`; `PlayAttack(string clip, double useSec)` растягивает клип под `UseSec` события ядра; канал повторяет клип, пока клавиша зажата. **Выделение героя (делается первым, до теней факелов):** контур — unshaded второй проход или rim, тень его не гасит; заполняющий свет спереди-сбоку на уровне груди без теней (в лимит теневых не входит); теплый круг света радиусом ~8 м с полной тенью.
  - `DummyView : Node3D` — внешность по пресету (спека §4 «Выбор манекена»): «Тренировочный» — мертвец, «Бронированный» — орк, «Стихийный» — мертвец с бледно-золотыми рунами-кольцами, «Рой» — 5 мертвецов в кучке, «Босс» и «Атакующий босс» — орк ×1,4; враги приглушены (менее яркая кожа), без контура; клип `zombie idle`; реакция `sword and shield impact` не чаще раза в 0,35 с; тики дают только вспышку; вспышка цвета и формы по типу урона (огонь — оранжевый, холод — ледяной голубой, молния — сине-белые ломаные дуги, физика — белые искры и осколки, яд — зеленый; темно-фиолетовый зарезервирован за тьмой Отступника и не занимается); кольцо на полу под целью последнего `Hit` (на «Рое» — та фигура, по которой бьет ядро); свой источник света без тени, если манекен вне круга героя; `float CollisionRadius(int target)`; `Vector3 TargetPosition(int target)`.
  - `SkillAnimations` — таблица из `game/data/skill_animations.json`: на каждую запись клип и доля кадра контакта (`contact`, например 0,45), которая масштабируется при растяжении клипа под `UseSec`; `cleave`, `whirlwind` → `slash`; `crush`, `charge` → `attack`; остальные → `casting`; скилл не из таблицы — по тегам (`melee` → `slash`, иначе `casting`).
  - `ProjectileView` — шар-частица по типу урона летит от героя к манекену «для глаз»; одновременно со своим светом не больше 8, остальные только светящимся материалом. Число и вспышка у снаряда показываются по прилету шара (полет не дольше 0,6 с); ядро урон уже посчитало — числа ядра от этого не меняются.
  - `FloatingNumbers` — числа над целью, крит крупнее, значок типа урона рядом; выключаются флагом `Enabled`.
  - `CombatDriver : Node` — каждый кадр: расстояние от героя до края коллизии ближайшей фигуры манекена и `IsOnScreen` → `UpdateReach`; `Advance(delta)` с **настоящим** временем кадра; клавиши 1–5 — `Press` (канальный скилл — `StartChannel` по нажатию и `StopChannel` по отпусканию, один вызов на нажатие); `Feed.DrainVisual` → анимация героя (`SkillUsed`), снаряд, вспышка и число (`Hit`; в ближнем бою — на кадре контакта клипа), текст отказа над героем (`Refused`). **Hit-stop 40 мс** — только `speed_scale = 0` у `AnimationPlayer` героя и манекена и пауза эффектов; `Engine.TimeScale` и пауза дерева не трогаются, время ядра идет. Ввод не идет в бой, если фокус в поле ввода или событие забрал интерфейс (задача 10 подключает окна).
- [ ] **Step 1:** реализовать.
- [ ] **Step 2: Самопроверка.**
  - Загрузить эталон, поставить героя рядом с манекеном, синтетическими клавишами жать скиллы 5 с игрового времени: журнал не пуст, DPS текущего сегмента > 0, анимация атаки запускалась.
  - Нажать скилл на перезарядке: отказ «перезарядка» в журнале и текст над героем.
  - Отойти на 8 м и нажать ближний скилл: «Вне досягаемости».
  - Увести манекен из кадра: Метеор — «Цель не видна», снаряд (Искра или Метательный топор) — применяется.
  - Зажать Вихрь и уйти от манекена: канал прекращается, в журнале одна строка отказа.
  - Hit-stop: за время замирания время `TrainingRoom` выросло на реальный `delta`.
  - Ближний удар: вспышка показана не раньше кадра контакта и не позже конца применения.
  - Серия тиков поджога за 1 с: реакция манекена запускалась не больше трех раз, тики реакцию не запускают.
  - Кольцо: на «Рое» под фигурой последнего `Hit`.
  - Свет манекена включается, когда герой отходит дальше ~8 м.
  - Контур есть у героя (материал или проход unshaded) и отсутствует у манекенов.
  - Без ассетов всё то же на капсулах.
- [ ] **Step 3:** `tools/godot_check.ps1 -SelfTest` — 0. **Step 4: Commit** `feat(game): hero, dummy and live combat in the 3d room`.

### Task 10: Окна поверх комнаты и экран боя

**Files:**
- Create: `game/Ui/WindowHost.cs`, `TopBar.cs`, `CombatHud.cs`
- Modify: `game/Main.cs` (вместо `TabContainer` — комната во весь экран, окна и экран боя), `game/Room/CombatDriver.cs` (маршрутизация ввода), `game/SelfTest.cs` (существующие шаги переводятся на окна)

**Interfaces:**
- Produces:
  - `WindowHost`: в каждом окне одна существующая панель (панели не переписываются); клавиши C — персонаж, P — пассивки, N — асцендант, K — скиллы, B — билды, R — разбор, M — мгновенная симуляция, L — журнал, T — измеритель (D занята шагом вправо), G — графики, Esc — закрыть верхнее окно; открываются у краев (дерево и персонаж слева, измеритель справа, журнал слева внизу), фон непрозрачный, позиция окна запоминается в `user://windows.cfg`. `OpenWindow(string id)`, `CloseWindow(string id)`, `bool IsOpen(string id)`; `ShowBreakdown` открывает окно «Разбор».
  - `TopBar` — кнопки с подписью клавиши («Пассивки (P)»), выпадающий список пресетов манекена из `data/dummies.json`.
  - **Ввод** (спека §4 «Ввод», «Мышь над окном»): клавиши боя и окон игнорируются, пока фокус в `LineEdit`/`SpinBox`/`TextEdit`; Enter, Esc или клик по полу снимают фокус; клик, колесо и перетаскивание над окном не доходят до героя и камеры (бой слушает `_UnhandledInput`).
  - `CombatHud` (спека §4 «Экран боя»): панель 1–5 внизу по центру — имя скилла, номер клавиши, затемнение перезарядки с секундами, красная рамка и затемнение при нехватке ресурса, подсветка слота в очереди и зажатого канала, мигание слота при отказе; подсказка при наведении — стоимость, перезарядка, время применения из `SlotInfo`; полоса ресурса «Мана 45 / 100»; ряд значков баффов и стаков героя с таймерами; плашка над манекеном — имя пресета, недуги и дебаффы со стаками и таймером. Все числа — через `RuNumber`; число, у которого есть `NumberLine`, — через `NumberLabel`.
  - `Main` сохраняет публичные свойства панелей плана 3 (`Character`, `Passives`, `Skills` и т. д.), вместо `SelectTab` — `OpenWindow`.
- [ ] **Step 1:** реализовать.
- [ ] **Step 2: Самопроверка.**
  - Все старые шаги самопроверки плана 3 зеленые на окнах.
  - C открывает и закрывает персонажа, N — асцендант, Esc закрывает верхнее окно.
  - Фокус в поле сида: цифра 1 уходит в поле, а не в бой (Review Focus 3).
  - Клик и колесо над окном: герой не двинулся, зум не изменился.
  - Экран боя: после нажатия скилла его слот показывает перезарядку, равную `State`; при нехватке ресурса — красная рамка; бафф Боевого клича виден с таймером; поджог виден на плашке манекена.
- [ ] **Step 3:** `tools/godot_check.ps1 -SelfTest` — 0. **Step 4: Commit** `feat(game): overlay windows and combat hud`.

### Task 11: Журнал, измеритель и графики

**Files:**
- Create: `game/Ui/CombatLogWindow.cs`, `MeterWindow.cs`, `ChartsWindow.cs`
- Modify: `game/SelfTest.cs`

**Interfaces:**
- Consumes: `TrainingRoom.Log/Filter`, `Segments/Current`, `FightSegment` (задача 7).
- Produces:
  - `CombatLogWindow` (спека §7): строки `CombatLogEntry`; фильтры по видам (тики скрыты по умолчанию) и по скиллу; автопрокрутка, которая останавливается при ручной прокрутке вверх; колонка имени вмещает «Возвращающийся топор» без обрезки.
  - `MeterWindow` (спека §8.1): полосы по режиму («DPS по скиллам», «Урон по скиллам», «Урон по типам», «Потеря на защите»), число и доля, полоса уязвимости другого цвета с подписью «+N от уязвимости»; обновление 4 раза в секунду; выбор сегмента с подписью и разницей с прошлым; переключатель «бросок / среднее», сид, «Сбросить» с подтверждением, «Графики», переключатель всплывающих чисел; клик по полосе скилла открывает разбор главной строки урона этого скилла через `Main.ShowBreakdown`. Сокращения — `RuNumber.Short`.
  - `ChartsWindow` (спека §8.2) для выбранного сегмента: линии урона по времени (общий и по скиллам, корзины 1 с), столбики DPS по скиллам, доли урона по типам, столбики потери на защите по скиллам («дошло / броня / сопротивления / множитель / обнулено»). Рисуется на `Control._Draw`, без новых зависимостей.
- [ ] **Step 1:** реализовать.
- [ ] **Step 2: Самопроверка.**
  - После короткого боя (меньше 2000 строк журнала) полосы измерителя равны `FightSegment` до единицы, а сумма поля `Amount` видимых и скрытых строк журнала сегмента — сумме измерителя.
  - Сумма строк «Потеря на защите» и «дошло» с учетом уязвимости равна урону до защиты.
  - Окно графиков строит непустые серии.
  - Снять узел пассивок в окне P, ударить снова: новый сегмент с подписью «после правки билда», его DPS отличается от прошлого.
- [ ] **Step 3:** `tools/godot_check.ps1 -SelfTest` — 0. **Step 4: Commit** `feat(game): combat log, damage meter and charts`.

### Task 12: Приемка, скриншоты, нагрузка, README

**Files:**
- Modify: `game/SelfTest.cs`, `README.md`, `docs/superpowers/debug-scene-screenshots/` (новые снимки)

- [ ] **Step 1: Приемочный сценарий самопроверки по спеке §10.5**, одним прогоном:
  - каждый из 6 эталонов бьет «Тренировочный» манекен 5 с через синтетический ввод;
  - измеритель равен журналу;
  - отказ на перезарядке;
  - правка узла дает новый сегмент с другим DPS;
  - всё работает без ассетов.
- [ ] **Step 2: Снимки из настоящего окна 2560×1440** (`tools/godot_check.ps1 -Screenshots docs/superpowers/debug-scene-screenshots`, спека §10.7), снимки боя — с открытыми журналом и измерителем: зум по умолчанию, минимальный (на «Боссе»: не обрезан сверху) и максимальный; огненный скилл; «Рой»; «Рой» со всеми эффектами, где герой и манекен стоят в перекрестных тенях факелов; каждый пресет манекена; по вспышке на каждый тип урона (огонь, холод, молния, физика, яд) на «Бронированном»; окно графиков; окно пассивок поверх комнаты. Снимки проверяет `dialoid-art-curator` (герой выделяется сильнее всех, тени не прячут героя и манекен, белых пятен нет, манекен читается); вердикт — в отчет задачи.
- [ ] **Step 3: Нагрузка** (спека §10.8). Самопроверка с флагом `--perf` 20 с держит «Рой» и самый частый скилл со всеми эффектами при 2560×1440, печатает средний FPS и худший 1 % строкой `SELFTEST PERF …`. Прогон на ПК владельца (RX 6750 XT) — числа в отчет; худший 1 % ниже 60 — не принято.
- [ ] **Step 4: README.** Управление (WASD, клик, удержание, 1–5, клавиши окон, колесо и средняя кнопка); где взять ассеты и `sync_assets.ps1`; учет лицензий `assets/LICENSES.md` (Mixamo — условия и ссылка, Poly Haven — CC0 и список текстур, источник текстуры крови); модели Mixamo входят в экспортированный `.pck` в составе игры (так разрешено), отдельно FBX не раздаются; что показывает измеритель; известное поведение: при бесконечной жизни манекена условия «цель ниже X % жизни» включаются циклически (жизнь доливается до максимума); отложено: модификаторы манекена, летальный режим.
- [ ] **Step 5:** `dotnet test core/Dialoid.sln`, `tools/godot_check.ps1`, `tools/godot_check.ps1 -SelfTest` — всё зеленое. **Step 6: Commit** `feat(game): training room acceptance, screenshots, readme`.

---

## Покрытие спецификации

| Раздел спецификации | Задача |
|---|---|
| §2 окна поверх, управление, манекен, бесконечное здоровье | 1, 9, 10 |
| §4 комната, масштаб, свет, загораживание, камера | 8 |
| §4 бюджет кадра | 8, 9, 12 |
| §4 читаемость боя, манекен в бою | 9 |
| §4 окна, ввод, мышь над окном, выбор манекена | 10 |
| §4 экран боя | 6, 7, 10 |
| §4 наведение | 9 |
| §5 герой, анимации, каналы, снаряды | 9 |
| §6.1–6.2 живой бой, общий двигатель, случайность, время | 1, 2, 4 |
| §6.2а здоровье пресетов | 1 |
| §6.2б правка билда посреди боя | 4 |
| §6.2в отказы нажатия, очередь 0,4 с | 2, 7 |
| §6.3 досягаемость | 4, 7, 9 |
| §6.4 сегменты | 4, 7, 11 |
| §7 журнал | 4, 7, 11 |
| §8 измеритель, графики, защита манекена | 3, 4, 7, 11 |
| §9 ассеты вне git, заглушки, лицензии | 8, 12 |
| §10 приемка | 1–4, 7 (тесты), 5, 12 |
| Windows как основная платформа | 5 |
