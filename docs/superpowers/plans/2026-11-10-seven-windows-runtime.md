# Seven windows runtime implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** seven live Godot windows matching approved Tower05 compositions, including the corrected icon tree.
**Architecture:** existing WindowHost/GameWindow with reversible expand mode; adaptive existing panels; opt-in Ascendancy TreeView renderer. Shared host/theme writer is separate from panel writers.
**Tech Stack:** Godot4.7 .NET, C#, existing SandboxSession and selftests.
**Spec:** docs/superpowers/specs/2026-11-10-seven-windows-runtime-design.md

## Global constraints
- Base main9a5f95c; preserve all foreign WIP using isolated seven-windows-runtime checkout.
- Existing side-window geometry and commands remain; full compositions available through expand/restore.
- No fixed demo data, new mechanics, new costs or new image generation.
- ExactID icons from approved artifact; author and reviewers separate.

## Review focus
- Expand/restore and viewport resizing preserve saved side geometry and back/focus.
- Narrow controls wrap/scroll without causing minimum-size overflow or lost actions.
- Revision changes never present stale simulation/snapshot data as fresh.
- Selected tree icon matches detail; correct points and locking under keyboard/gamepad.
- Log auto-scroll/filter, Meter mode/reset, Charts selected segment and Builds clipboard continue working.

## Task1 — shared host and runtime checks (writerC)
Files: WindowHost.cs, WindowHost.Gamepad.cs only if needed, Art/RoomHudTheme.cs, Art/WindowBackdrop.cs, SelfTest.SevenWindows.cs, SelfTest.cs registration. Main.cs only capture startup hook if needed.
- [x] Add failing selftest for expand/restore exact side rect, viewport clamping and seven-window allowlist; record baseline failure.
- [x] Add public WindowHost.SetExpanded(string id,bool expanded) and GameWindow.Expanded with expand/restore title control. Keep Content identity/IPadPanel. Expanded mode cannot drag/resize; restore side rect. Existing defaults unchanged.
- [x] Add RoomHudTheme.TowerWindowTheme(), TowerHeading(string,int), TowerSurface(int) helpers; explicit seven-ID opt-in; no broad global restyle.
- [x] Render approved existing per-window architecture with quiet data surfaces and resize-safe clipping.
- [x] Add selftest sevenwindows.reference capturing all seven at720/900, expanded/restore/focus/state and world return; use live session.
- [x] Run build and new checks after all writers finish; coordinate single build process.

## Task2 — Ascendancy, Builds, Breakdown (writerA)
Files: AscendancyPanel.cs, TreeView.cs opt-in only, BuildsPanel.cs, BreakdownPanel.cs; optional new partial files for these classes; Art/AscendancyIcons.cs and Art/Icons/ascendancy/berserker/*.png/import only.
- [x] Verify existing relevant baseline checks; add meaningful assertions for exact seven-ID icon map and graph edges in own optional SelfTest.SevenAscendancy.cs, registration byC.
- [x] Copy accepted seven originals to runtime resources and load exactIDs; no shared icon stand-ins.
- [x] Implement opt-in cut sockets and selected/focus presentation in existing TreeView, radial graph preserved; adaptive right detail on wide and stacked on narrow.
- [x] Re-layout Builds catalogue/load and code exchange, Breakdown history/snapshot/revision within existing APIs.
- [x] Use shared theme helpers fromTask1; retain current theme as fallback until shared helper lands.
- [x] Report checks and request independent review.

## Task3 — Simulation, Log, Meter, Charts (writerB)
Files: SimulationPanel.cs, CombatLogWindow.cs, MeterWindow.cs, ChartsWindow.cs; optional new partial files for these classes. No shared files.
- [x] Review baseline targeted tests; preserve their behavior and public APIs.
- [x] Adaptive Simulation settings/results, Log filters/event table, Meter segment/modes/reset and Charts dashboard using actual existing data.
- [x] Wide layout matches approved references; narrow retains all controls via wrapping/scrolling. Preserve callbacks, selftest selectors and focus paths.
- [x] Remove chart-specific old gold; keep meaningful chart series colours and distinguish legend data.
- [x] Report targeted coverage and review limits.

## Integration and acceptance (root)
- [x] Claim/show Beads parent and scoped subtasks; maintain ledger/card and ownership.
- [x] Confirm clean baseline build (already0warnings/errors).
- [x] Import/build; run targeted windows/gamepad/ascendancy/simulation/log/meter/charts suites plus new sevenwindows checks. Do not weaken old tests.
- [x] Inspect actual720/900 GPU captures; run icon64/32/24 check and actual detail states.
- [x] Independent UI/art/code review; fix blockers and repeat affected checks.
- [x] Launch visible final game, verify screen if access permits; if permission denies report it and use project-native capture without calling it a desktop screenshot.
- [x] Deliver local verified implementation/evidence, leaving main unchanged until repository delivery is authorized.
