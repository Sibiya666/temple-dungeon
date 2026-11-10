# Approved Dialoid UI delivery — 2026-11-10

Owner explicitly requested merging the approved work into the primary branch and closing the related Beads tasks. The primary branch is main. Existing Tower prerequisite PR #3 is merged at 79d87501597e9c000af888ad8e167bd3301be416.

This delivery includes the selected Tower05 with its exact approved architecture, 15 currency/service portraits, required standalone activity catalogue and legacy migration, the reviewed Skills window reference and opt-in theme, the HTML styleguide presentation and seven-direction comparison/selected prototype, their sources/provenance and local acceptance evidence. Original dirty worktrees remain intact. No .beads configuration, Task Tower tooling, unrelated Character window changes, combat/balance changes or licensed vendor files are included.

Independent source audits confirmed the Tower prerequisites and safe Skills ownership. Whole-candidate technical review: APPROVE after preserving both TowerReference and SkillsReference registrations. Existing art/UI asset and runtime APPROVE remain recorded with their exact source screenshots; integrated real 720/900 frames were checked again.

Clean integration verification:
- Core solution build and game build: 0 warnings/errors, NuGetAudit=false for unavailable audit feed.
- Full Core: 1004/1004 PASS. Full Sandbox: 144/144 PASS, restored and actually executed.
- Godot graphical Tower: 18/18 PASS. Tower headless: 18/18 PASS.
- Godot graphical Skills: 7/7 PASS. Gamepad: 15/15 PASS.
- Offline HTML selected prototype: 32/32 PASS; styleguide: 17/17 PASS; no external requests or broken images.
- Integrated screenshots: Tower installed/empty/whole/focus, 16 groups, actual currencies; Skills catalogue/detail at 720/900.

The initial combined filter attempt selected only panels and was not accepted as evidence. Each supported prefix was rerun separately. A missing TowerReference registration was caught by the 17/18 count, restored, and the full18 rerun passed. No checks were weakened.

Known limits remain: Character windows.layout width debt, existing node semantic-icon debt, system Georgia/Times font fallback, synthetic gamepad rather than a physical controller, long advanced details scrolling. This is targeted Godot UI acceptance plus full Core/Sandbox, not an assertion that every unrelated Godot scenario is green. Browser prototypes retain their explicitly labelled simulation limitations.

Related tasks to close after exact remote main verification: dialoid-ku0 (PR3 dependency), dialoid-tsa, dialoid-2tt, dialoid-7wx, dialoid-dqq. Already completed HTML tasks dialoid-5kl/dialoid-2xr/dialoid-vkr remain closed. Unrelated dialoid-9rm and dialoid-cak remain untouched.

Final merge SHA and post-merge exact-main verification are recorded in Beads delivery notes. This file records the verified candidate; it does not fabricate an unknown future merge SHA.
