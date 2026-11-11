# UI/art integration audit — 2026-11-11

Author: /root/ui_art_merge_audit (Godot developer). Scope is runtime art only; no Core/data changes, no panel/host edits.

## Ancestry finding

`c6e70aa` (device-handoff) and delivered `9a5f95c` have merge-base `4466021`. The 197 new icons, glass/spear/key cursor selection and character/3D art were never merged to current main. They were not deleted by Tower delivery. `git log --diff-filter=D` for swordsmen catalog is empty. Device branch contains `7066bc0`, `faa8e30`, `dc510c7`, `cf9da70`; main proceeds through independent Tower work.

## Preserve current branch

Do not copy old Character/Skills/TreeView/WindowHost, RoomHudTheme/WindowBackdrop, HUD/SkillSlotView or CombatLogWindow over current files. Current Tower05 seven-window + Character/Skills implementations supersede source-side WIP. Old CombatLog lacks restore-default-filters; old HUD56x52 predates delivered72x64. TowerBoard source-side edits are earlier interactive work and would overwrite delivered Tower05 montage. Core/data differences are outside UI/art scope.

## Scoped runtime import (authorized by root)

- GameIcons.cs + GameIcons.Swordsmen.Generated.cs/.uid: exact skill/node whitelist for12 families; missing art fallback retained only for unfinished other families. No content activation. Six Apostate skill portraits and104Apostate nodes are resource-only; active catalog remains Warrior/Sorcerer.
- SwordsmenIconsCheck.cs/.uid/.tscn: frozen208node+6portrait checker, unsafe-ID guard, active catalog invariant, 64/32/24 sheets and actual Warrior trees.
- Icons/skill_nodes/:191missing original-node PNGs and imports. Existing17Cleave PNGs/imports exactly equal source, preserved.
- Icons/skills/{awakening,crescent_flurry,dark_slash,dark_wave,shadow_step,space_rend}.png/.import: six archived resource portraits. Existing13skills, including approved5stainedglass, byte-equal source and preserved.
- Cursors/{glass,key,spear}.png/.import + TowerCursor.cs/TowerCursorCheck.cs: approved48px persistent selection,F8 guard against text editing/modifiers/echo, isolated preference test. Existing sword/hand SVGs retained as historical source assets, not deleted.

Total:400newassetfiles (191nodes+6portraits+3cursors, each PNG/import),8code/checkfiles. All copied only from explicit UI/art paths; no production panels, shared Main/project, Core/data or foreignWIP changed.

## Art archive recommendation

Root can import added docs/art families from device branch (especially swordsmen, concepts, ronin/kitsune, stainedglasscursor) without importing older runtime. Keep main-owned colliding docs as current and preserve alternate versions under archive provenance. 3D models/source asset packs should remain provenance-labelled; do not activate old Room/AssetLibrary or model mappings automatically.

## Verification

Pending serialized build/import and checks; results appended below.

## Scoped verification results

- `dotnet build game/Dialoid.Game.csproj --no-restore --nologo -v quiet`: PASS,0warnings/errors.
- Godot headless editor import: exit0, imported400newassetfiles.
- SwordsmenIconsCheck headless + GPU: PASS,208exact64pxnodePNG+6resourceportraits; unsafeIDrejection; activecatalogunchanged.19GPUframes:6realWarriortrees+12nodefamily64/32/24sheets+APportraitsheet.
- TowerCursorCheck headless + GPU: PASS;3distinct48pxdesigns, opaquehotspots, ordinary/disabled/explicitbuttoncursorhandling, F8input+echo/text/modifierguards, save/load/corrupt/unknownsettings, actualprofileunchanged. GPU native=True;3actualframes. Expected Godot ConfigFile parse ERROR is emitted by the deliberately malformed negative fixture; checker exits0and printsCURSOR_SELECTION_OK.
- Original-order group `charskills|sevenwindows|character|skills|gamepad|windows|builds`:46/46PASS on unified runtime.
- Durablelogs/frames copied to `docs/art/ui-styleguide/2026-11-11-ui-art-unified/`.
- Personally inspected actual WarriorCrush treeframe, noicon/topologyregression; root to arrange independentfinalreview.

- Final GPU filter charsSkills|sevenwindows:8/8PASS; actualframesinunified/after. Production unchanged after these captures.

## Final integration follow-up: accurate icon readiness disclosure

Reviewer found the old `skill.Id != "cleave"` warning condition was stale after the imported Warrior icon families. Final scoped production changes:

- GameIcons.HasDedicatedSkillNodeArt(skillId,nodeId) requires exact whitelist pair and an actually loaded dedicated texture; no effect fallback can count as completion.
- Skills warning is shown iff an actual tree node lacks that dedicated art. Completed Warrior families no longer get a false warning; Sorcerer remains disclosed.
- Swordsmen checker asserts all208positivepairs, cross-skill/unknown/unsafe negatives and the actual rendered panel labels: Crush no warning, Fireball warning retained.

Rebuilt final sources0/0; repeated full headless46/46PASS; GPU8/8PASS with66fresh720/900frames; headless/GPU208+6iconcheckPASS and19iconframesupdated. No production changes after this verification. Independent code reviewer notified with exact updated logs. Build/Godot slot released.
