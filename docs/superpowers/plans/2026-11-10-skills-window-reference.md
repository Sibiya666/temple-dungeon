# Skills Window Reference Implementation Plan

> **For agentic workers:** Use superpowers:subagent-driven-development for the assigned UI implementation and independent review. Preserve other contributors' changes.

**Goal:** Install oil-ui, adapt its workflow to Dialoid, and produce a verified in-game skills window reference.
**Architecture:** Preserve existing session commands and navigation. Add reusable visual helpers to RoomHudTheme and apply them locally to SkillsPanel; quiet the skills backdrop without changing other windows.
**Tech Stack:** Godot .NET / C#, PowerShell; oil-ui design workflow.
**Spec:** `docs/superpowers/specs/2026-11-10-skills-window-reference-design.md`.

## Tasks

- [x] Install upstream oil-ui into Codex skills; record version/revision. Keep upstream intact and put Dialoid adaptation in a separate discoverable project skill.
- [x] Capture baseline, implement catalogue/detail visual hierarchy and shared helpers; preserve session behaviour and existing controls.
- [x] Run build and targeted meaningful Skills/gamepad checks, capture actual target-size screenshots.
- [x] Independent art review and UI/code review; fix material issues and verify changes.
- [x] Publish local reference documentation and evidence; record Beads status and known content debt honestly.

## Ownership

Integrator: this spec, plan, task card, project skill and final reference documentation.
Godot implementer: `game/Ui/SkillsPanel*.cs`, additive helpers in `game/Ui/Art/RoomHudTheme.cs`, skills-specific changes in `game/Ui/Art/WindowBackdrop.cs`, a new scoped selftest/capture partial if needed, and `docs/art/ui-styleguide/2026-11-10-skills-reference/` evidence.
Reviewers are read-only. Any expansion requires integration coordination. Never alter unrelated Tower files, task README, or Beads configuration.

## Execution record

- Upstream installed at `C:/Users/sibiya666/.codex/skills/oil-ui`, version 0.33.3, commit `40ae49ab89bc496459bcea9c360c28d599358297`. Standard installer could not run because Python aliases are not an installed interpreter; direct Git clone used. Upstream working tree remains unchanged. Its update-check script skips source Git checkouts by design.
- Project adapter: `.agents/skills/dialoid-ui/SKILL.md`, independently reviewed by `/root/skills_ui_review`: APPROVE for art-direction preservation, icon/provider rules and honest runtime acceptance.
- Current remote main observed: `88712caf143e15bbe78b5b93072ee5ca3de54048`.
- Beads executable located through existing Task Tower configuration. Initial sandbox read reported database missing; retry with approved filesystem access succeeded without database repair. Ready queue empty; created and claimed `dialoid-tsa`, assignee verified by show. No configuration or database initialization changes.
- Implemented local theme, catalogue density/hierarchy, calmer skills backdrop, separate graph selection/target, truthful locked state and visible return focus. Existing gamepad field test updated to navigate from catalogue to the assigned skill through A before editing; assertions retained.
- Independent `/root/skills_art` APPROVE and `/root/skills_ui_review` APPROVE including the targeted gamepad test adjustment. Root independently ran build (0 warnings/errors), Skills 7/7 and Gamepad 15/15. Actual graphical capture 2/2 and 10 final PNG at target viewports; before/after crop viewed by root.
- Additional Windows run 7/8: Character window exceeds one-third width. Character/Host unchanged; no baseline execution of this failure, so no claim that it was proven pre-existing. Content node-icon debt and detail scrolling remain explicit.
- Local delivery only: `docs/art/UI_REFERENCE.md`, discoverable adapter, evidence README and screenshots. No commit/PR/main-delivery assertion. Local stage is review; Beads lacks that custom status, so its task stays in_progress with review-complete notes, not closed. No configuration changes to add a status.
