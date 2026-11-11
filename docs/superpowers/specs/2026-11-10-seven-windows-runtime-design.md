# Seven approved windows in Godot

Owner approved execution after the seven-window gallery and corrected Ascendancy tree. Tracking: Beads dialoid-cjd. Base: remote main 9a5f95ccefa4394f804cf3a15b0c4095d7e6fbe9.

Port Ascendancy, Builds, Breakdown, Simulation, Log, Meter and Charts from docs/art/ui-styleguide/2026-11-10-seven-windows/. Use existing controls and live session data; preserve commands, revisions, undo, history, filtering, segment selection, reset confirmation, clipboard and input routing. Never render demo numbers in production.

Tower05 defines architecture behind transparent working surfaces, quiet cold metal, ivory text, restrained serif hierarchy, independent selection and keyboard/gamepad focus. Use RoomHudTheme and WindowBackdrop. No changes to Tower, Character, Passives or Skills appearance/geometry.

Ruling: keep the existing side-window defaults and saved rectangles, and add an explicit expand/restore control for these seven windows. Expanded mode presents the approved wide compositions within the real host; restoring returns to the prior side rectangle. The existing owner rule of one-third side width remains intact. Panels adapt between stacked narrow and wide split layouts. No content wrappers breaking IPadPanel.

GPU review ruling: expanded workspaces retain the top navigation but extend to the bottom viewport margin, covering the combat HUD while open. This is explicit expanded mode only; world/HUD return is captured separately. Runtime artwork for the seven windows must use the exact seven approved Venice masters, not the older WindowPanels-v1 derivatives. Capture Berserker with the new icons and selected blood_feast detail as the primary Ascendancy scenario.

Ascendancy keeps existing start-to-notables graph and commands. Berserker receives all seven exact-ID PNGs from the accepted artifact, including repeated selected portrait, cut sockets, distinct taken/available/unavailable/selected/focus. Other ascendancies retain existing art with honest documented debt; do not claim their icon sets complete.

Acceptance: build without warnings/errors, existing targeted regressions, meaningful expanded/restore and icon mapping checks, actual GPU screenshots at1280x720 and1600x900 of all seven plus representative focus/disabled/stale/reset states and return to world/HUD. Review by independent UI, art and code reviewers. No new generation/costs, combat/data redesign, or unrelated WIP edits. Local completion is not main delivery.
