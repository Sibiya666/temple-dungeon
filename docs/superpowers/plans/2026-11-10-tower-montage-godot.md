# Tower Montage Godot Implementation Plan

> **For agentic workers:** Use superpowers:subagent-driven-development for assigned implementation and independent review. One UI writer, preserve all prior WIP.

**Goal:** Implement the owner-selected05 Tower composition with existing Godot commands and real game verification.
**Architecture:** Refactor only Tower view composition/visual helpers and scoped capture/tests. Keep Core/data command semantics, target rules and source inventory models unchanged. HTML is a visual reference only.
**Tech Stack:** Godot4.7.2 .NET/C#, existing PNG/SVG/shaders, Windows PowerShell.
**Spec:** docs/superpowers/specs/2026-11-10-tower-montage-godot-design.md

- [x] Register task, record scoped baseline fingerprints and runtime capture.
- [x] Map preserved fields/commands/targets to chosen composition, confirm exact file ownership.
- [x] Add meaningful regression/capture scenario for new layout/behaviour; implement narrow UI changes.
- [x] Build, targeted Tower checks and Core Tower tests; actual720/900 screenshots, input/focus/wheel and real currencies.
- [x] Independent art/UI/code review; fix blocking findings and repeat affected verification.
- [x] Root integration verification; update reference docs and task evidence, deliver real game frames.

Exact ownership is in TASK-tower-montage-godot.md. Root owns this spec/plan/task and final integration record; UI implementer owns named Tower view/test files, reviewers remain read-only. No gameplay/data changes, no unrelated cleanup.

Rulings from inventory: Rules.Floors is5 at every rank; catalogue preserves16slots/pagination and modified singleton groups. Source inventory selection is distinct from currency target; same-type modified replacement remains valid and must not inherit the HTML simplification. Names below square icons require a scoped SocketBounds path in TowerGlassWindow, not stretched octagonal cells. Old4×4 geometry assertions may be updated to8×2, preserving actual input/operation assertions. Existing target-aware crafting, quality preview, biome/mod controls and shuffle/refusals remain accessible.

Baseline: actual GPU2/2 capture720/900 and file fingerprints. Plain build encountered offline NU1900 audit lookup; validation uses -p:NuGetAudit=false rather than changing repository configuration. Any baseline certificate/shader-cache warnings are recorded; game assets0missing. Remote main observed88712caf143e15bbe78b5b93072ee5ca3de54048.

