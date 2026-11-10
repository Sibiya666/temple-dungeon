# Tower UI Reference Implementation Plan

> **For agentic workers:** Use superpowers:subagent-driven-development. One Godot writer; independent read-only art/UI reviewers.

**Goal:** Make the mature Tower board and activity detail use the Dialoid reference UI language without changing gameplay.
**Architecture:** Preserve the mature worktree as immutable source; copy its WIP dependencies into a native isolated checkout. Add opt-in theme/style helpers and narrow Board/GlassWindow hooks. Keep shader frame encoding explicit.
**Tech Stack:** Godot .NET, C#, shaders, PowerShell.
**Spec:** `docs/superpowers/specs/2026-11-10-tower-ui-reference-design.md`.

- [x] Native checkout prepared and source snapshot verified; baseline files/hashes recorded (103 source files, 6 affected existing-file copies).
- [x] Register/claim Beads task and confirm ownership: dialoid-2tt.
- [x] Baseline runtime capture and build; implement assigned style/frame/detail scope.
- [x] Targeted Tower/reference input/state checks and runtime 720/900 after capture.
- [x] Independent art/UI/code review, necessary fixes, final integration checks.
- [x] Save local task-specific patch, provenance, before/after and reference documentation; report integration limits.

Final: art and UI/code APPROVE. Root independently verified candidate build/Tower; then applied SHA-pinned 9-file UI patch to mature Tower after 109 baseline records matched. Source build 0/0, import, Tower 15/15 and GPU 2/2 all pass clean. 99 unrelated source snapshot files unchanged, 9 own files match candidate. Core Tower 186/186. Receipt and integrated-after screenshots saved. Local-only delivery; no main merge or commit.

Scope/ownership is exact in TASK-tower-ui-reference.md. Core/data/test dependencies copied as baseline are not authored by this task. No secrets, new generation, purchase, main integration or unrelated changes.

Integration ruling: after independent acceptance, root may apply only the task-specific UI patch to the mature source checkout if baseline fingerprints still match and apply --check passes. Repeat source build/import/Tower checks afterward. This fulfills the owner's request in the actual Tower working copy while preserving all unrelated WIP; it is not main delivery. If fingerprints diverge, retain the isolated candidate and resolve only the overlapping edits.

Native checkout: `C:/Users/sibiya666/.codex/worktrees/tower-ui-reference/dialoid`, detached at source `4fc2734c31805fe22ea4f09117d8b1f7c6833e1c`; source mature worktree immutable. Remote main read `88712caf143e15bbe78b5b93072ee5ca3de54048`. Source snapshot dependencies are separately recorded by the UI implementer, excluding secrets and unrelated generated files.
