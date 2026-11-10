---
name: dialoid-ui
description: Use when designing, refining, implementing or reviewing Dialoid game windows, HUD or reusable UI components with oil-ui while preserving the established art direction and Godot input behaviour.
---

# Dialoid UI with oil-ui

Use the installed oil-ui workflow (`~/.codex/skills/oil-ui/SKILL.md`) for design judgement. This adapter applies to Dialoid game UI, including Task Tower worktrees. It does not replace the art direction, runtime architecture or task workflow.

## Establish the current reference

Read `AGENTS.md`, `docs/art/ART_DIRECTION.md`, the affected window specification and `docs/tasks/README.md`. Open the actual approved reference images and current runtime screenshots. If the worktree lacks a local tool/reference, locate the main checkout via git common-dir/worktree list; never copy secrets.

For skills-window work, read `docs/superpowers/specs/2026-11-10-skills-window-reference-design.md` and current evidence in `docs/art/ui-styleguide/2026-11-10-skills-reference/` when present. The first general sheet is an exploratory presentation, not runtime acceptance or approval of every proposed frame, font or colour.

## Apply oil-ui in this project

- Continue an already selected direction. Do not restart brand/style exploration for each window. State the player's task, main object, primary action and important states.
- Use the existing C#/Godot controls, RoomHudTheme and window host. Reuse named colour/spacing/type/state helpers; keep each window's layout responsibility explicit. Do not add a web component framework or browser prototype as the production UI.
- Preserve ART_DIRECTION: charcoal and cool metal, ivory text, restrained light, calm data surfaces and architectural imagery at the periphery. Gold from a proposal is not a universal frame. Existing approved stained-glass skills do not approve every new icon or background.
- Keep selection, hover, keyboard/gamepad focus, assigned and unavailable states semantically distinct. States must not move hit targets, resize icons or cover counters. A locked skill may still be browsable; explain why editing is unavailable.
- Preserve session commands, revision semantics, navigation/back, scroll restoration, input routing and undo. Changes to appearance do not authorize combat or content redesign.
- New backgrounds and visual concepts use Venice under `.agents/skills/dialoid-venice/SKILL.md`; icons use the approved GPT handoff. Do not let upstream provider defaults override this route. Do not incur new costs without an existing budget. Reuse existing approved assets for refinement when sufficient.
- Every newly added skill/node needs its own semantic icon under the project readiness rules. Existing icon debt must remain visible; acceptance of window chrome does not close content debt.

## Evidence and review

Capture baseline before changes and actual Godot screenshots after at the target viewport, normally 1280x720 plus 1600x900 for this window family. Show a representative catalogue/detail and the states affected by the change; include real HUD/world context. Inspect screenshots yourself at native size; inspect 64/32/24 icon exports where relevant. A static montage is not runtime verification.

Use existing targeted Skills/windows/gamepad checks and a project build as needed. Browser/mobile/lint-only defaults from oil-ui do not replace Godot build, input and game-camera verification. Avoid unrelated full-suite runs unless integration risk requires them.

Assign one implementation writer per file. Art direction review belongs to a separate `dialoid-art-curator`, UI/input usability to a separate `dialoid-ui-curator`; implementation normally uses `dialoid-godot-developer`, assets `dialoid-technical-artist`. Provide reviewers current screenshots, original task and constraints; do not substitute the author's explanation for visual evidence. Fix blocking findings and verify the affected path.

Store final reference evidence/provenance under `docs/art/ui-styleguide/<task>/`, design/plan and task records in the existing project locations. Clearly distinguish approved references, implementation choices and proposals. Report what was checked, known debt and what remains unverified; never claim repository delivery/main verification from a local-only result.
