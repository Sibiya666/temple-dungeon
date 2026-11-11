# Character and Skills runtime implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement task-by-task.

**Goal:** extend accepted Tower05 runtime UI to Character and Skills, keeping live commands and input.
**Architecture:** reuse current host/expand/theme/adaptive panes; refactor only the two existing panel layouts. Shared coordinator owns host/capture changes, panel writers have disjoint files.
**Tech Stack:** Godot4.7.2 .NET, C#, existing SandboxSession/NumberLabel/SkillTreeGraphView.
**Spec:** docs/superpowers/specs/2026-11-11-character-skills-runtime-design.md

## Global constraints
- Preserve accepted dialoid-cjd changes already in the current worktree, plus all unrelated work.
- No new content, assets generation, spending or Core/data changes.
- Exact approved portraits/backgrounds; existing skill/node icon debt remains explicit.
- Side geometry remains; expand/restore now includes these two windows, Passives remains outside the allowlist.
- One writer per file; no concurrent builds/imports. Baseline capture before production edits.

## Review focus
- Long class/ascendancy/weapon names and level slider must fit narrow Character without cropping numbers or expanding window minimum width.
- Detail redraw during skill point/level changes must preserve graph selection, manual pan/zoom and keyboard/gamepad focus.
- Locked/unassigned browsing cannot change Revision or offer editing; assignment/undo preserve exact commands.
- Back restores catalogue scroll and visible focused card; expanded→side and pad footer cannot crop graph/controls.
- Hover, selection, assignment, unavailable and focus remain distinguishable; every number breakdown and equipment action remains accessible.

## Task1 — baseline, host and checks (/root/char_skills_shared; parent vec)
Files: WindowHost.cs minimal allowlist; WindowBackdrop.cs if needed; Art/WindowPanels/Seven/character.png and skills.png/import; SelfTest.CharacterSkills.cs; SelfTest.cs registration; SelfTest.SevenWindows.cs unaffected-window check moved from Character to Passives under new scope.
Review ruling: coordinator also owns a minimal WindowHost.Gamepad.cs Navigate addition honoring explicit, valid Godot FocusNeighbor paths within the current root before the unchanged spatial fallback. Skills owns its local neighbor links. This fixes graph/detail directional traps without changing scoring for the seven existing windows; stale, disabled and external targets must fall back safely.
- [x] Capture current side Character and Skills catalogue/detail at720/900 in before/ and record RED missing expansion before production edits; signal authors.
- [x] Extend allowlist using existing reversible host, exact two approved Venice masters and same overlay/crop/theme. No shared tree/global restyling.
- [x] Add meaningful charskills checks for expansion/restore/default bounds, live stats/commands, browse Revision, assignment/back/scroll/focus and actual visible graph/controls.
- [x] Capture after720/900 normal/invalid/locked/assigned/unassigned/point/focus states and valid world/HUD, including side and lower scrolled controls.
- [x] Run targeted original-order group character|skills|windows|gamepad|builds and charskills GPU plus prior sevenwindows regression when shared host changes. Preserve assertions; only intentional scope selector update.

## Task2 — Character (/root/character_ui; dialoid-5u9)
Files: CharacterPanel.cs and optional matching partials only.
- [x] Audit existing public fields/NumberLabels/tests; await baseline-ready.
- [x] Use Tower05 theme and adaptive identity/equipment:left, live grouped stats:right; wrap narrow fields instead of fixed240px captions.
- [x] Preserve commands, slider release/deferred redraw, errors/violations, NumberFor/Numbers/LineNumbers/ItemBoxes/weapon APIs and gamepad/number breakdown behavior.
- [x] Self-review; report targeted test requests and hand over for coordinator build and independent review.

## Task3 — Skills (/root/skills_ui; dialoid-eo6)
Files: SkillsPanel.cs, SkillsPanel.Catalog.cs, SkillsPanel.Graph.cs, optional matching partials only.
- [x] Audit accepted flow/input/graph tests; await baseline-ready.
- [x] Apply Tower05 catalogue context/cards and wide graph/detail arrangement, adaptive narrow layout, using existing exact images and graph topology.
- [x] Preserve locked browsing/Revision, slots/assignment/points/level/undo/rotation, back/scroll/focus, graph transforms after rank redraw.
- [x] Keep hover/focus distinct; account for actual viewport and pad prompt height. No global TreeView changes.
- [x] Self-review and report regression needs; coordinate all builds.

## Integration (root)
- [x] Claim/show Beads scopes; preserve current source snapshot/hashes and foreign WIP.
- [x] Verify actual720/900 screenshots, command results and exact existing asset provenance.
- [x] Resolve all new failures and independent art/UI/code blockers, repeat only affected verification after changes.
- [x] Record final evidence and known debt in docs/art/ui-styleguide/2026-11-11-character-skills-runtime/ and Beads; launch visible verified preview if feasible.
- [x] Deliver local reviewed implementation without claiming main delivery or closing repository stage prematurely.
