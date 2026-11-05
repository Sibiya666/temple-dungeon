# Tower Inventory Completion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Завершить рабочую доску: пустой старт и явное безопасное заполнение шафлом из инвентаря.
**Architecture:** ActivityInventory владеет экземплярами. Новый FillRandom заполняет пустые слоты через Core, TowerAssembly учитывает успешный шафл. Godot только вызывает Core и отображает результаты.
**Tech Stack:** C#/.NET8, xUnit, Godot4.7 .NET.
**Spec:** docs/superpowers/specs/2026-11-01-tower-inventory-completion.md

## Global Constraints

- Башня изначально пустая; открытие экрана, повторное открытие и смена ранга не заполняют этажи.
- Только явное нажатие кнопки шафла вызывает случайное заполнение из принадлежащих игроку активностей.
- Шафл заполняет только пустые этажи; установленные экземпляры и их намазки сохраняются.
- Недостаток экземпляров или валюты — полный отказ без изменения состояния и RNG.
- Сохранять чужие изменения, текущую рабочую копию и существующий тестовый запас; без внешних расходов и новых ассетов.

## Review Focus

1. Пустой старт/повторное открытие: runtime-тест до любых операций.
2. Нехватка экземпляров/валюты: Core тест снимков состояния и RNG.
3. Частичная башня с намазанным этажом: точное сохранение FloorSpec и количества экземпляров.
4. Стак с несколькими экземплярами/смешение рангов: отбор по экземплярам текущего ранга без дубликатов, roundtrip.
5. Заполненная башня/повторное нажатие: отказ без списания; UI показывает причину и стоимость до применения.

### Task 1: Core FillRandom

Files: core/Dialoid.Core/Tower/ActivityInventory.cs; TowerAssembly.cs; TowerRefusal.cs; core/Dialoid.Core.Tests/Tower/ActivityInventoryTests.cs.
Interface: public TowerRefusal ActivityInventory.FillRandom(TowerAssembly assembly, Wallet wallet); new refusals NoEmptyFloors, NotEnoughActivities; internal TowerAssembly.RecordInventoryShuffle().

- [x] Написать и запустить падающие тесты для всех пяти Review Focus по Core.
- [x] Реализовать FillRandom: проверить свободные слоты/наличие/валюту до RNG; выбрать экземпляры без возвращения, установить точные сохранённые FloorSpec, списать NextShuffleCost, отметить успех. Другие ранги и установленные слоты не затрагивать.
- [x] Запустить Tower suite, записать red/green и self-review.
- [x] Независимое task review: соответствие спецификации и качество; исправить замечания.

### Task 2: Godot wiring and acceptance

Files: game/Ui/Tower/TowerBoardView.cs; game/SelfTest.Tower.cs; docs/art/tower/2026-11-01-superpowers/README.md and runtime captures.
Consumes: Task1 FillRandom and refusals. Produces: кнопка шафла, понятное описание, тесты пустого старта/повторного открытия/заполнения/partial-preservation.

- [x] Написать и выполнить падающий runtime-тест клика кнопки шафла и пустого старта до существующих tower.crafting.
- [x] Подключить кнопку к Inventory.FillRandom, описание цены/защиты установленных этажей, существующий счётчик валюты. Не делать автоматических вызовов FillRandom.
- [x] Запустить строгую сборку и целевые Godot-тесты в настоящем окне; сохранить кадры до/после/720p.
- [x] Независимое UI/техническое task review, исправления.

### Final integration

Final-review fix scope: `game/Ui/Tower/TowerBoardView.cs`, `game/Main.Tower.cs`, `game/SelfTest.Tower.cs`. Preserve mousewheel freedom after a clicked node while keeping keyboard/pad focus visibility after layout; restore only focusable prior controls. Add observed red/green runtime regressions for both sidepanels at720p and input-device switch.

- [x] Broad final review текущей работы по инвентарю/рамкам/шафлу, включая более ранние незакоммиченные файлы этой сессии.
- [x] Повторная необходимая проверка после исправлений, актуальные карточки и отчёт об известных ограничениях.
- [x] Оставить изменения в текущей рабочей копии без push/merge и без коммита чужих файлов.
