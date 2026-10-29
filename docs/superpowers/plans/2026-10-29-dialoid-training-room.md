# Dialoid Training Room Implementation Plan (план 4)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Отладочная сцена становится тренировочной комнатой в духе манекенов WoW. Герой из пака Anokolisa бьет манекен в реальном времени, журнал боя идет в чат, измеритель как Details! показывает DPS и потерю урона на защите манекена. Окна билда открываются поверх комнаты.

**Architecture:**
- **Ядро.** Цикл `SimRun` выносится в общий двигатель шага `CombatEngine`. Мгновенная симуляция — это тот же двигатель, где команды выдает ротация, а живой бой (`LiveFight`) получает команды игрока. Поэтому числа совпадают по построению.
- **События урона.** Каждое попадание и тик пишутся структурным событием `DamageEvent` с разложением защиты цели.
- **Песочница.** `Dialoid.Sandbox` получает `TrainingRoom`: живой бой текущего билда, сегменты, измеритель, журнал по-русски и правило досягаемости по тегам. Все это тестируется без Godot.
- **Слой Godot.** Только рисует комнату, героя и окна и передает нажатия.

**Tech Stack:** .NET 8 (`net8.0`, RollForward Major, SDK 10), C# 12, xUnit, Godot 4.7.2 .NET (`Godot.NET.Sdk/4.7.2`), ассеты Anokolisa «Pixel Crawler — Free Pack» 2.11 (вне git).

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
  - Где искать: переменная `DIALOID_ASSETS`, иначе соседняя с репозиторием папка `dialoid-assets/anokolisa-pixel-crawler-free/extracted/Pixel Crawler - Free Pack/`.
  - Без ассетов комната рисуется заглушками, а самопроверка проходит.
- **Godot (§3.2 спеки ядра).** Каждый вызов `godot` под таймаутом. `tools/godot_check.sh` и `tools/godot_check.sh --selftest` дают код 0, в логе нет строк `^(SCRIPT )?ERROR:`.
- **Тексты и коммиты.** Тексты для владельца — по-русски, идентификаторы — по-английски. Коммиты в стиле conventional commits, в конце строка `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- **Паттерны плана 3 сохраняются.**
  - Панели наследуют `SessionPanel`: отложенная, склеенная перерисовка, подписка в `_EnterTree`.
  - Контрол не освобождается внутри собственного сигнала.
  - `SandboxSession` однопоточная (UI-поток).
  - Каждое число на экране выводится через `NumberLabel` из `NumberLine` или из итогов ядра.

## Review Focus

1. **Низкий FPS или долгий кадр** (вкладка свернута, отладчик). Ожидание: слой игры делает не больше `MaxCatchUpSteps` шагов ядра за кадр. Лишнее время отбрасывается, а не накапливается в лавину. Бой не «телепортируется» на минуты вперед. Тест в задаче 4 (`TrainingRoom` получает огромный `dt`).
2. **Правка билда во время канала или с нажатием в очереди.** Ожидание: бой перезапускается чисто, канал и очередь сброшены, исключений нет, сегмент закрыт. Тест в задаче 4.
3. **Клавиши боя, пока фокус в поле ввода окна** (сид, импорт, уровень скилла). Ожидание: цифры уходят в поле, а не в бой. Проверка в самопроверке задачи 7.
4. **Нажатие скилла, которого нет в слоте, или пустой слот.** Ожидание: отказ «пустой слот», без исключения. Тест в задаче 2.
5. **Частичный набор ассетов** (нет одного листа). Ожидание: недостающий спрайт заменяется заглушкой, одна строка-предупреждение без `ERROR:`, сцена работает. Тест в задаче 5.

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
game/Room/AssetLibrary.cs                      загрузка PNG вне res:// и заглушки
game/Room/RoomView.cs                          комната из тайлов, камера
game/Room/HeroView.cs                          герой: движение, анимации
game/Room/DummyView.cs                         манекен по пресету
game/Room/CombatDriver.cs                      fixed timestep, клавиши 1–5, визуал попаданий
game/Ui/WindowHost.cs                          окна поверх комнаты, горячие клавиши
game/Ui/CombatLogWindow.cs, MeterWindow.cs, ChartsWindow.cs
game/Main.cs, game/SelfTest.cs                 переделка корня; самопроверка
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

### Task 5: Ассеты, комната, герой, манекен

**Files:**
- Create: `game/Room/AssetLibrary.cs`, `RoomView.cs`, `HeroView.cs`, `DummyView.cs`
- Modify: `README.md` (раздел «Ассеты комнаты»: где скачать пак, куда положить, `DIALOID_ASSETS`), `game/SelfTest.cs`

**Interfaces:**
- Produces:
  - `AssetLibrary.Load(string? root)` — грузит PNG через `Image.LoadFromFile` → `ImageTexture` (вне `res://`), режет листы на кадры:
    - герой — кадр 64×64;
    - манекены: `Idle` кадр 32×32, `Run` 64×64, `Death` по высоте листа;
    - тайлы 16×16.

    Для любого отсутствующего файла — заглушка (цветной прямоугольник) и одна строка `GD.Print("ASSET_MISSING: …")`, без `PushError`. Есть свойство `bool HasPack`.
  - `RoomView`:
    - комната ~20×14 тайлов из `Dungeon_Tiles`, `Floors_Tiles`, `Wall_Tiles`; координаты тайлов в листах подбирает исполнитель;
    - стены непроходимы;
    - целочисленный масштаб без сглаживания (`TextureFilter = Nearest`);
    - `Camera2D` следует за героем;
    - `double DistanceTiles(Vector2 a, Vector2 b)`.
  - `HeroView`:
    - движение на WASD (8 сторон, 4 анимации: вниз, вверх, вбок с зеркалом) и по клику ЛКМ по полу;
    - анимации `Idle`/`Walk`/`Run`;
    - `PlayAttack(string animation, double durationSec)` растягивает кадры атаки под время применения из ядра;
    - оружие в руке из `Weapons/*` по типу оружия билда (**[Билдеру]**: таблица «тип оружия → лист»).
  - `DummyView`: внешность по пресету (спецификация §4). Для «Роя» — 5 фигур по кругу. Вспышка при попадании (модуляция цвета на 0,1 с), анимация `Idle`. Смерти нет: здоровье бесконечное.

- [ ] **Step 1:** реализовать.
- [ ] **Step 2: Самопроверка.**
  - Комната строится с ассетами и без них: `DIALOID_ASSETS` указывает на пустую папку → заглушки, без `ERROR:`.
  - При частичном наборе ассетов отсутствующий лист заменяется заглушкой (Review Focus 5).
  - Герой проходит 3 тайла на WASD через синтетический ввод и упирается в стену.
- [ ] **Step 3:** `tools/godot_check.sh --selftest` — 0. **Step 4: Commit** `feat(game): room, hero and dummy views`.

### Task 6: Комната как главный вид, окна поверх

**Files:**
- Create: `game/Ui/WindowHost.cs`
- Modify: `game/Main.cs` (вместо `TabContainer` — комната во весь экран и окна), `game/SelfTest.cs` (существующие шаги переводятся на окна)

**Interfaces:**
- Produces:
  - `WindowHost`: окна — Godot `Window`, встроенные во вьюпорт (`embedded`), или `PanelContainer` с заголовком и кнопкой закрытия (**[Билдеру]**). В каждом окне одна существующая панель, сами панели не переписываются.
  - Клавиши: C — персонаж, P — пассивки, A — асцендант, K — скиллы, B — билды, R — разбор, M — мгновенная симуляция, L — журнал, D — измеритель, G — графики, Esc — закрыть верхнее окно.
  - Полоса кнопок сверху дублирует клавиши.
  - Горячие клавиши и клавиши боя игнорируются, пока фокус в `LineEdit`/`SpinBox`/`TextEdit`.
  - `Main` сохраняет публичные свойства панелей из плана 3 (`Character`, `Passives`, `Skills` и т. д.), чтобы старые шаги самопроверки работали.
  - Вместо `SelectTab` появляются `OpenWindow(string title)` и `CloseWindow(string title)`.
  - `ShowBreakdown` открывает окно «Разбор».

- [ ] **Step 1:** реализовать.
- [ ] **Step 2:** самопроверка — все 33 старых шага зеленые на окнах, плюс шаги «C открывает и закрывает персонажа», «Esc закрывает верхнее окно».
- [ ] **Step 3:** `tools/godot_check.sh --selftest` — 0. **Step 4: Commit** `feat(game): room as main view with overlay windows`.

### Task 7: Живой бой в сцене и журнал

**Files:**
- Create: `game/Room/CombatDriver.cs`, `game/Ui/CombatLogWindow.cs`
- Modify: `game/Main.cs`, `game/Room/HeroView.cs`, `DummyView.cs`, `game/SelfTest.cs`

**Interfaces:**
- Consumes: `TrainingRoom` (задача 4), `RoomView.DistanceTiles`, `HeroView.PlayAttack`.
- Produces:
  - `CombatDriver : Node`. Каждый кадр вызывает `TrainingRoom.Advance(delta)`.
    - Клавиши 1–5 — `Press(slot, distance, targetOnScreen)` (`targetOnScreen` — манекен внутри видимого прямоугольника `Camera2D`); удержание клавиши канального скилла — `StartChannel`/`StopChannel`.
    - По событию `use` из журнала запускается анимация атаки нужного вида (таблица «скилл → Slice/Crush/Pierce» в `game/Room/SkillAnimations.cs`: `crush`, `charge` → Crush; `cleave`, `whirlwind` → Slice; остальные → Pierce; для скилла не из таблицы — по тегам: `melee` → Slice, иначе Pierce).
    - Для снарядов — визуальный кружок по типу урона от героя к манекену.
    - По `DamageEvent` — вспышка манекена и всплывающее число урона (крит крупнее).
    - Отказ нажатия выводится строкой в журнал и коротким текстом над героем.
  - `CombatLogWindow`:
    - `RichTextLabel` с `LogLines`;
    - фильтры по виду события и по скиллу;
    - автопрокрутка, которая останавливается при ручной прокрутке вверх.
  - Выбор манекена — выпадающий список у верхней полосы (пресеты из данных), режим «бросок» или «среднее» и сид.

- [ ] **Step 1:** реализовать.
- [ ] **Step 2: Самопроверка.**
  - Загрузить эталон, поставить героя рядом с манекеном, нажать клавиши скиллов через синтетический ввод на 5 с игрового времени: журнал не пуст, DPS текущего сегмента > 0.
  - Нажать скилл на перезарядке: строка отказа «перезарядка».
  - Отойти на 8 тайлов и нажать ближний скилл: отказ «вне досягаемости».
  - Увести манекен за край экрана (герой отходит так, чтобы манекен вышел из камеры): Метеор — отказ «цель за пределами экрана», снаряд (Искра или Метательный топор) — применяется.
  - Фокус в поле сида: цифра 1 уходит в поле, а не в бой (Review Focus 3).
- [ ] **Step 3:** `tools/godot_check.sh --selftest` — 0. **Step 4: Commit** `feat(game): live combat and combat log`.

### Task 8: Измеритель и графики

**Files:**
- Create: `game/Ui/MeterWindow.cs`, `game/Ui/ChartsWindow.cs`
- Modify: `game/SelfTest.cs`

**Interfaces:**
- Consumes: `TrainingRoom.Segments/Current`, `FightSegment` (задача 4).
- Produces:
  - `MeterWindow`:
    - горизонтальные полосы (`_Draw` или `ProgressBar`) по выбранному режиму: «DPS по скиллам», «Урон по скиллам», «Урон по типам», «Потеря на защите»;
    - число и доля у каждой полосы;
    - выбор сегмента («текущий», «№ N»), кнопки «Сбросить» и «Графики»;
    - обновление 4 раза в секунду;
    - клик по полосе скилла открывает разбор главной строки урона этого скилла через `Main.ShowBreakdown`.
  - `ChartsWindow` для выбранного сегмента:
    - линии урона по времени (общий и по скиллам, корзины по 1 с);
    - столбики DPS по скиллам;
    - доли урона по типам;
    - столбики потери на защите по скиллам: «дошло / броня / сопротивления / множитель / обнулено».

    Всё рисуется на `Control._Draw`, числа и подписи — через `RuNumber`. Новых зависимостей нет.

- [ ] **Step 1:** реализовать.
- [ ] **Step 2: Самопроверка.**
  - После боя из задачи 7 значения полос измерителя равны `FightSegment` до единицы.
  - Сумма строк «Потеря на защите» и «дошло» равна урону до защиты.
  - Окно графиков открывается и строит непустые серии.
  - Снять узел пассивок в окне P, ударить снова: новый сегмент, его DPS отличается от прошлого.
- [ ] **Step 3:** `tools/godot_check.sh --selftest` — 0. **Step 4: Commit** `feat(game): damage meter and charts`.

### Task 9: Приемка, скриншоты, README

**Files:**
- Modify: `game/SelfTest.cs`, `README.md`, `docs/superpowers/debug-scene-screenshots/` (новые снимки)

- [ ] **Step 1: Шаги самопроверки по спецификации §10.5**, собранные в один приемочный сценарий:
  - каждый из 6 эталонов бьет «Тренировочный» манекен 5 с через синтетический ввод;
  - измеритель равен журналу;
  - отказ на перезарядке;
  - правка узла дает новый сегмент с другим DPS;
  - всё работает без ассетов.
- [ ] **Step 2: Снимки из настоящего окна** (как в плане 3, флаг `--screenshots`): комната в бою с открытыми журналом и измерителем; окно графиков; окно пассивок поверх комнаты.
- [ ] **Step 3: README.**
  - Управление: WASD, клик, 1–5, клавиши окон.
  - Где взять ассеты.
  - Что показывает измеритель.
  - Отложено: модификаторы, летальный режим.
- [ ] **Step 4:** `dotnet test core/Dialoid.sln`, `tools/godot_check.sh`, `tools/godot_check.sh --selftest` — всё зеленое. **Step 5: Commit** `feat(game): training room acceptance, readme`.

---

## Покрытие спецификации

| Раздел спецификации | Задача |
|---|---|
| §2 окна поверх, управление, манекен, бесконечное здоровье | 1, 5, 6, 7 |
| §4 комната, масштаб, окна, выбор манекена, внешность | 5, 6, 7 |
| §5 герой, анимации, каналы, снаряды | 5, 7 |
| §6.1–6.2 живой бой, общий двигатель, случайность, время | 1, 2, 4 |
| §6.2а здоровье пресетов | 1 |
| §6.2б правка билда посреди боя | 4 |
| §6.2в отказы нажатия, очередь 0,4 с | 2 |
| §6.3 досягаемость | 4, 7 |
| §6.4 сегменты | 4, 8 |
| §7 журнал | 4, 7 |
| §8 измеритель, графики, защита манекена | 3, 4, 8 |
| §9 ассеты вне git, заглушки | 5 |
| §10 приемка | 1–4 (тесты), 9 |
