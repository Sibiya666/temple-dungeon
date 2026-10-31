# Bundled Dialoid UI icons

Current art direction: [tower-world guide](../../../../docs/art/ART_DIRECTION.md). The 2026-10-31 rollout replaces the earlier paintings with 40 restrained tower-world images (including 8 buff images), updates 37 category/navigation/elite SVGs and uses 6 new damage SVGs. Total: 83 active visual resources. Older damage PNGs remain unused. Exact prompts, reference sources, export script and verification: [rollout report](../../../../docs/art/icons/2026-10-31-tower-icon-rollout/README.md). The historical notes below describe the earlier baseline and are superseded for current appearance.

Runtime resources: 13 skill paintings, 6 ailment paintings, 6 neutral damage symbols, 6 dedicated keystone paintings, 4 weapon paintings, 2 class emblems, 35 editable category/navigation SVGs, 2 elite SVG badges and 1 health-potion painting (75 visual files). PNG skills/ailments/perks/equipment/classes/potion are 64 px; damage symbols are 32 px. Icons are decorative; localized labels, IDs, input handling and live core values remain authoritative. Frames, focus, timers and glyphs are separate layers.

Original generated art uses built-in Codex image_gen. No third-party image source, paid external API or emoji. Source masters and exact prompts: `assets/icons/2026-10-31-probe`, `assets/icons/2026-10-31-combat-states-v1`, `assets/icons/2026-10-31-skill-completion`; provenance in matching `docs/art/icons` folders. SVGs are original code-authored geometry. Do not label generated assets CC0 or claim exclusivity. This directory is intentionally bundled under `res://` so icons survive export without `DIALOID_ASSETS`.

GameIcons caches resources and whitelists semantic IDs. Trigger aliases: returning_axe → throwing_axe, fireball_blast → fireball, blink_flare → blink. Tree nodes reuse category symbols; a shared symbol does not describe all effects of a complex node. Potion art is provided for the future mechanic; no fictitious charge counter or active potion button was added.

Integration details and actual engine screenshots: [report](../../../../docs/art/icons/2026-10-31-integrated/README.md).

Equipment/class selection, simulation rows/log and elite rank follow-up: [report](../../../../docs/art/icons/2026-10-31-equipment-simulation/README.md). Staff and wand use selected v2 masters for24px readability; original and revision prompts are retained in that report's provenance.md.

Current integration after the accepted 3D-room merge: [perks and interface report](../../../../docs/art/icons/2026-10-31-perks-ui/README.md). That report supersedes the prototype integration screenshots. The actual room uses GameIcons from TopBar, GameWindow, TreeView, PassivePanel, AscendancyPanel, character/skill editors, SkillSlotView and EffectChip. Icons are always available independently of the default HUD skin. No abandoned prototype HUD or tower code is restored.
