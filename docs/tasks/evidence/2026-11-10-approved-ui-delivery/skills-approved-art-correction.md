# Approved Skills artwork dependency correction

The exact-main screenshot comparison after PR4 found that five historically approved baseline skill PNGs existed only in the original local branch. The approved Skills reference used them, while remote main still had their previous versions. This correction includes only cleave/battle_cry/fireball/ice_lance/spark PNGs and their original production/provenance pack.

All five PNGs match dc510c759b22ba7d60bf19f57aa0f58c91ca1b5e byte-for-byte and match the accepted SHA in adoption/runtime-hashes.json. The original Venice reference is unchanged. No new generation, mechanics, C#, data, node icons or unrelated branch content is included. ART_DIRECTION carries only the historical owner approval paragraph.

Fresh import and graphical Skills7/7 passed on the corrected candidate; root compared720catalog directly with the accepted TSA screenshot. Independent dependency audit and asset review: APPROVE. The entire Core/data and executable C# tree is identical to exact main c109c4021b584785a3a7785ab765ba9ef84c2a8c, whose post-merge verification passed Core1004/1004, Sandbox144/144, Tower18/18, Skills7/7, Gamepad15/15 and build0/0.

Final exact remote main SHA is recorded in the related Beads task delivery notes after merge.
