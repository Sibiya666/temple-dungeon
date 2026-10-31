# Icon integration implementation plan

**Goal:** Generate missing skill art and insert project-owned icons in the existing runnable UI.

**Architecture:** A cached GameIcons catalog loads bundled Godot resources; existing panel controls retain their labels, selection IDs and command handlers. Source art and prompts remain under assets/icons and docs/art/icons. No new combat HUD or gameplay rules.

**Tech stack:** Godot 4.7 .NET, C#, PNG and SVG, built-in image_gen.

**Spec:** docs/art/icons/2026-10-31-probe/README.md and docs/art/icons/2026-10-31-combat-states-v1/README.md. User explicitly requested insertion on 2026-10-31.

- [x] Add opt-in integration check scene. Confirm it fails before catalog insertion.
- [x] Generate ten missing selectable skills and health potion; inspect contact sheet and preserve prompts. Bundle all selectable skills, six ailments and six damage symbols in game/Ui/Art/Icons.
- [x] Implement cached catalog with triggered-skill aliases and safe unknown fallback; add skill selector/tree/rotation art and actual-event log icons, plus actual-type result icons.
- [x] Run build, import, integration check and existing selftest; capture runnable UI screenshot; document exact coverage and remaining integration surfaces.

Constraints: preserve other uncommitted changes; no branch switch or commits. Skill ID and selection order must not change. Unknown icons retain full text. Match latest gray stone/iron theme; no bronze baked frames. Do not invent absent HUD screens or show static state examples as live game state.
