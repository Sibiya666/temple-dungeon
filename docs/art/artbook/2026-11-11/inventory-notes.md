# Artbook source audit — 2026-11-11

Read-only audit by /root/artbook_inventory, role dialoid-technical-artist. No generation, costs, source mutations or licensing changes. Only this note and family-rules.json authored. Reviewed ART_DIRECTION.md, docs/agents.md, docs/tasks/README.md and the selected D4 image.

## Source roots

- WORKTREE: C:/Users/sibiya666/.codex/worktrees/seven-windows-runtime/dialoid
- MAIN checkout (not necessarily identical to remote main): C:/Users/sibiya666/Documents/project/dialoid

Important: MAIN contains significant local/untracked art absent from WORKTREE. Never equate MAIN checkout availability with delivery in GitHub main. For the book, record source_root per asset; current runtime implementation and historical archives have different scope.

## Families in WORKTREE (file counts include derived contact sheets / review frames)

| Family | Visible source files | Meaning/status |
|---|---:|---|
| docs/art/icons/2026-10-31-venice-ui-concepts | 3 | D4 specifically chosen by owner; other concepts exploration |
| docs/art/icons/2026-10-31-venice-tower-icons | 5 | selected icon shape family, Venice |
| docs/art/icons/2026-10-31-venice-tower-backgrounds | 4 | 3 architectural masters + comparison |
| docs/art/icons/2026-10-31-venice-full-backgrounds | 8 | 7 independent architecture masters + comparison, replaces GPT background trials |
| docs/art/icons/2026-11-03-stained-glass-skills | 5 masters,15 size exports,11 adoption,5 previous,1 reference,preview | owner accepted all five; avoid treating 15 exports as 15 designs |
| docs/art/icons/2026-10-31-cleave-nodes | 27 | 17 unique semantic node images, contact/size sheets + historical/final runtime |
| docs/art/icons/{integrated,perks-ui,equipment-simulation,combat-states-v1,probe,tower-icon-rollout} | 4/10/6/1/1/7 | original historical integration sheets; no blanket current-style approval |
| docs/art/icons/2026-10-31-gpt-controlled-edit | 3 | controlled subject edits, research, not runtime |
| docs/art/icons/2026-10-31-gpt-remaining-backgrounds | 8 | superseded by Venice full backgrounds |
| docs/art/skills/2026-10-31-all-skill-trees | 18 | 13 real skill graph screenshots + derived sheets, historical data/layout proof |
| docs/art/tower/activity-glass-icons and subfamilies | 24 | 3 base + 5 revision families×4 + palette. Separate revisions, not all selected finals |
| docs/art/tower/standalone-activities | 3 base + escort4 + switching5 | approved/iterative isolated activity motifs; provenance per subfolder |
| docs/art/tower/timed-run-glass | 5 | selected glass/hourglass reference + derivatives |
| docs/art/tower/currency-icons-2026-11-10 | originals16/previews62/reference3 | 15 final semantic currencies/emblems + supporting art; exact IDs in README |
| docs/art/tower-seven | 22 assets, 25 phase shots, 1sheet | seven Tower UI comparison exploration; do not confuse with seven runtime windows |
| docs/art/tower-selected;styleguide-presentation | 4qa/7qa | existing presentation/artbook-like guide history |
| docs/art/ui-styleguide/2026-11-10-seven-windows | 9layoutPNG +7iconoriginals+21sizeexports | final static Tower05 seven layouts, all approved; 7 own Berserker node icons |
| docs/art/ui-styleguide/2026-11-10-seven-windows-runtime/root-after | 40 | root verified runtime; after/ is duplicate coordinator pass, prefer root-after |
| docs/art/ui-styleguide/2026-11-11-character-skills-runtime/root-after | 26 | latest Character/Skills runtime; after/26 is older/intermediate; before/8 historical |
| docs/art/ui-styleguide/2026-11-10-tower-currency-icons/root-after | 40 +45 runtime-icon exports | accepted latest Tower stage; after/ duplicates another pass |
| docs/art/ui-styleguide/2026-11-10-tower-montage-godot/parity-correction/root-after | 38 | earlier Tower parity stage, compare selected representative vs final currency stage |
| game/Ui/Art/Icons | 113 SVG/PNG across types | runtime icons classes,damage,ailments,buffs,equipment,items,skills,nodes,symbols,berserker. Mostly duplicate scaled exports of masters; dedup by content/pixels where possible |
| game/Ui/Tower/Art | 55 SVG/PNG + activity paths | exact runtime sources and older SVG symbols; not all original standalone art |
| game/vendor/dialoid-room-v1 | 12texturePNG | albedo/normal/roughness four material families, external provenance must be retained |
| game/vendor/mixamo | 24FBX | third-party characters/animations, register in appendix; do not claim original Dialoid concept art |

## Major MAIN-only art (must include for “all artbook”)

1. **docs/art/icons/2026-11-01-swordsmen/**: 197 new masters (87 Warrior nodes,6 Apostate skills,104 Apostate nodes), 14 chosen revisions. All197 APPROVE according to README; 214 runtime textures including17 existing Cleave. Read **manifest.json** for selected masters, **selected-revisions.json** overrides. Review/in-game PNGs are derived contact sheets/evidence; do not count as new originals. Subfamilies awakening,battle_cry,charge,crescent_flurry,crush,dark_slash,dark_wave,shadow_step,space_rend,throwing_axe,whirlwind,skills. Apostate art is resource-only preview; class not activated by this artwork.
2. **docs/art/concepts/2026-10-31-rogue-kitsune/**: 3 Venice full-body concepts, base/teleport-illusion/tower-devices. Early README says pending independent review; later spike-3d/briefs/rogue-kitsune/README confirms owner-approved source concepts. Do not canonize anatomical proposals beyond the art.
3. **spike-3d/briefs/rogue-kitsune/**: 3technicalreferences,9cancelled local cutout/grid preparation images,3final bodies×6renders,comparison; original and rigGLB/FBX/BLEND. Final bodies ~11.5ktriangles,65bones,1.8m,3PBR2048maps. Status research model, no final runtime acceptance; neck seam onbase, no tail/earbones, full game clips notchecked. Prefer each final/preview.png/front/side/back plus comparison; canceled input cutout is history only.
4. **spike-3d/briefs/rogach-rubaka/**: 8conceptPNG (front,side,back,axe,2seed trials,2side trials), GLB/rig outputs. **rogach-shaman/**:4conceptPNG front/side/back/staff +models. Venice concepts/Tripo model pipeline, research unless current asset integration evidence specifically says otherwise.
5. **docs/art/ronin/**: playable33frames+review41,outline-only1,grounding-fixed1. Latest grounding-fixed frame supersedes floor penetration. README says earlier full108/111 with known failures; later targeted35/35. Do not label earlier fulltest green. **docs/art/kitsune/** playable1+review1. Both absent in WORKTREE.
6. **docs/art/skills/2026-10-31-art-guide-runtime/**:33frames+review41; screenshots of combat art guide corrections, use representative final capture plus full archive index.
7. **docs/art/cursor/2026-11-03-venice-358/**:4PNG+12cutout derivatives (WORKTREE only glass original1). **2026-11-03-selection/**:3runtimeframes; F8 selects glass,spear,key48px. UIAPPROVE as playtest, not onefinal choice. Old sword and gauntlet hand inactive/superseded according to this later README. Owner liked glass material: main docs/art/STAINED_GLASS_EXPLORATION.md.
8. Earlier icon style experiments only MAIN: buffs-v1(7),gpt-from-venice(2),painted-style-v2(1),restrained-style-v3(4),smooth-backgrounds-v4(4),three-game-styles(3). Put in history chapter, not current approved direction.
9. **spike-3d/reports/**37images: lineup/silhouette/materialalbedo/room_default/resume-rubaka-room/animation retarget diagnostics. **spike-3d/room-textures-v1/reports/**9images materials-and-tiling, artguide-light-high/low etc. Good world/material/scale chapter.
10. **spike-3d root**45images: showcase,combat_sheet,shot_combat,efk,shots andburstframes. Choose showcase/combat_sheet/efk and representative scene. Root has no .md; exact status research/historical.
11. **assets/teaser/dialoid_teaser.mp4** and **dialoid_teaser_720p.mp4**: existing promo media, likely resolution variants; appendix + selected video frame if tooling supports extraction. Do not generate new trailer.
12. MAIN docs/art/tower runtime/2026-11-01-* captures are historical Tower UI, prefer newer WORKTREE Tower05 for actual current UI; retained archive for completeness.

## Safe scan / allow roots for stablegen

Never recursively include spike-3d/stablegen-trial wholesale: contains Blender app distribution, Python site-packages, ComfyUI plugin examples and ~1000+ third-party docs samples unrelated to project.

Only these project outputs are in scope:
- MAIN/spike-3d/stablegen-trial/output/ (10 rasterfiles atroot; nested Scene/2026-10-31T11-32-10/generated/2; controlnet/depth/2 are technical maps)
- MAIN/spike-3d/stablegen-trial/ComfyUI/output/dialoid-room-v1/ (4projectrenders)
- MAIN/spike-3d/gen/stablegen/ (godot-compare.png,rogach-stablegen_rogach-basecolor.png)
- If including general ComfyUI/output/5 more files, label origin unknown research until inspected; avoid ComfyUI/input/ originals from libraries without provenance.

## Third-party content and exclusions

- Exclude efk/**/Texture(s),Parts and Model from main visual art chapter unless used effect evidence establishes project use; these are Effekseer example libraries, not unique Dialoid authored assets. One actual runtime efk.png/scene is useful, file register may list dependencies separately.
- Exclude assets/polyhaven raw normal/roughness duplicates from hero pages; show 4material families with README/attribution and retain dependency register. Never infer commercial license for generated work.
- Exclude .godot/,bin/,obj/,node_modules/,pythonlibs/,site-packages/,blender distribution,export_templates,addons,thirdparty docs/sampledata,import files from artwork inventory.
- Exact repeated filehashes and exports64/32/24 aren't distinct artwork. Keep original master + alias paths in manifest. Different revisions remain distinct with status.
- Earlier README may be superseded by later records. Machine rules choose precise prefix/source_root and longer rules first, then default archive/unknown. source_root main means original checkout, not verified GitHub main.

## Provenance / rights

Existing Venice and GPT images retain their original prompts, JSON and chosen references. No new generation. Concept/model sources include Venice,Tripo and local Blender; runtime Mixamo/PolyHaven/Effekseer dependencies have separate provenance. Historical noncommercial-research condition is not a newly verified license; publishing a book does not establish asset commercial rights. Record unknown when not backed by sidecar or README.
