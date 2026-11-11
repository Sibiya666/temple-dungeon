# Seven windows — independent root verification

Beads parent: dialoid-cjd; scopes: dialoid-b42 (host/theme/capture), dialoid-aja (Ascendancy/Builds/Breakdown), dialoid-q8a (Simulation/Log/Meter/Charts).
Base: remote main 9a5f95ccefa4394f804cf3a15b0c4095d7e6fbe9. Branch: codex/seven-windows-runtime. This is local runtime acceptance, not main delivery.

## Verified final source

- Root build: 0 warnings / 0 errors (`root-build.log`).
- Root original-order UI regression group: 44/44 PASS (`root-headless-ui-group.log`). Filter: windows|meter|log|charts|gamepad|builds|simulation|ascendancy. Original assertions preserved; filters select original step order.
- Root actual Vulkan/GPU checks: 4/4 PASS (`root-gpu-seven.log`), 40 PNG in `root-after/`. Includes all seven windows at 1280x720 and1600x900, narrow720 plus bottom scroll, Ascendancy selected/focus/start-disabled, stale simulation/snapshot, safe reset cancellation and valid world/HUD return.
- Strict graph checks: seven exact resource IDs, distinct approved images, six spokes, persistent detail selection, unchanged default Passive renderer; all expanded captions in bounds and nonoverlapping, every compact node visible in the actual graph ancestor scroll area, including expanded gamepad states.
- Runtime seven Berserker images are byte-identical to accepted originals. Seven backgrounds are the exact existing approved Venice masters. Core/data have no changes. Scoped diff whitespace check passed.
- Independent CODE, UI and ART reviews: APPROVE; see adjacent review files. Final open visual findings were cleared using root-after, not earlier interim captures.

## Fixes discovered by verification

- Dedicated approved background masters replaced older derivatives; expanded layout covers the HUD until restored/closed, preserving top navigation and side defaults.
- Layout containers respect narrow natural minima and scrolling. Reference tree labels avoid sockets and other labels; compact mode keeps all icons and the selected label, with detail below. Gamepad focus preserves the fitted overview; manual zoom/pan remain.
- Log follow mode ignores unmeasured page=0 range clamps instead of treating them as owner scrolling. Existing manual scroll stop/resume regression passes. Narrow header uses «Событие».
- Existing Meter test used a former direct-child path for Total; recursive selector preserves the original assertions. Graph visibility test now follows the actual scroll ancestor rather than the selector popup's internal scroll container.

## Viewing and limits

`index.html` is an offline screenshot viewer; `root-after/` contains real game frames, not interactive mockups. The final viewer was opened visibly in the in-app browser. A fresh visible game was launched from this exact worktree with --ui-window=ascendancy --ui-expanded --ui-reference=warrior_berserker_whirlwind.json, PID255660. Desktop control of Godot was previously refused; no desktop-screenshot claim is made. Game screenshots come from the actual GPU viewport.

Existing Charts side geometry at720 may be160px high because it reserves space above Log; the intentional side-window contract remains. Full dashboard uses «Развернуть». Individual icon readiness is closed for the seven Berserker IDs only; other ascendancy sets retain explicitly visible legacy-art debt. Gamepad checks use injected events, not a physical-device claim. No new generation/API charges, purchases, gameplay/Core/data changes or foreign-WIP edits in this runtime stage.
