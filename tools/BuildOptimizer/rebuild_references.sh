#!/usr/bin/env bash
# Recipes that produced data/builds/ (Task 9). Re-run after a balance change, review the diff, then run the content tests:
#   dotnet test core/Dialoid.Core.Tests --filter "FullyQualifiedName~ReferenceBuildTests"
# Each run starts from the current file (local search keeps the build's idea); add --scratch-passives --scratch-trees
# to restart a build from nothing. Runs take 5–30 minutes each; they are independent and can run in parallel.
# Objective and constraints: see the header of Program.cs. Survival weight is 0 here because every level-60 reference
# survives long against «Атакующий босс»; once Task 10 tunes the boss, consider --survival-weight 0.1.
set -euo pipefail
cd "$(dirname "$0")/../.."
dotnet build tools/BuildOptimizer -c Release -v q
OPT="dotnet tools/BuildOptimizer/bin/Release/net8.0/BuildOptimizer.dll opt"
OUT=${OUT:-data/builds}

VENOM="dex_venom|dex_crippling|dex_venomous"
# Physical bleed berserker: no element conversions, no venom.
BAN_PHYS="$VENOM|searing_blade|burning_wounds|thunder_strike|fire_whirl|fanned|smoldering|ice_whirl|frostbite|ice_crumbs|thunder_hammer|static_discharge|storm_charge|burning_trail|embers|storm_edge|poisoned_blades|lethal_dose|int_flame_4|will_storm"
# Lightning thunderer: no fire, cold or poison conversions.
BAN_LIGHTNING_W="$VENOM|searing_blade|burning_wounds|fire_whirl|fanned|smoldering|ice_whirl|frostbite|ice_crumbs|burning_trail|embers|poisoned_blades|lethal_dose|int_flame_4"
# Fire warrior: no lightning, cold or poison conversions; Метательный топор notable is useless without the axe.
BAN_FIRE_W="$VENOM|thunder_strike|ice_whirl|frostbite|ice_crumbs|thunder_hammer|static_discharge|storm_charge|storm_edge|poisoned_blades|lethal_dose|will_storm|int_frost|int_bitter|int_frozen|asc:thunder_axe"
BAN_FIRE_S="$VENOM|frost_orb|storm_orb|ice_meteor|frost_shatter|will_storm|int_frost|int_bitter|int_frozen"
BAN_COLD_S="$VENOM|sun_orb|solar_corona|ember_spark|smolder|int_flame_4|int_ignite|int_inferno|storm_orb"
# Lightning sorcerer: no fire nodes at all.
FIRE_NODES="int_flame_3|int_flame_4|int_flame_5|int_flame_6|int_incineration|int_ignite|int_inferno|kindling|core_heat|fire_field|scorched_earth|sun_orb|solar_corona|ember_spark|smolder|arrival_flare|double_flare|fire_exit|molten_core|eternal_flame|lingering_heat|ring_of_fire|flame_mark|soul_flame|frost_orb"

o() { local name=$1; shift; $OPT --in "$OUT/$name.json" --out "$OUT/$name.json" --rounds 2 --lns "$@"; }

o warrior_berserker_whirlwind --asc berserker --skill "whirlwind^1.25" --type "physical>=0.6" --ban "$BAN_PHYS"
o warrior_thunderer_crush --asc thunderer --skill "crush^1.25" --type "lightning>=0.6" --ban "$BAN_LIGHTNING_W"
# «Огненный Громовержец»: Cleave reaches only ~1.2× the next skill (1.3 is infeasible; Berserker reaches 1.25 only with two
# zero-value notables) — see data/BALANCE_NOTES.md, «у Воителя нет асцендантской поддержки огня».
o warrior_fire_cleave_crit --asc thunderer --skill "cleave^1.15" --type "fire>=0.5" --ban "$BAN_FIRE_W"
o sorcerer_pyromancer_fireball --asc pyromancer --skill "fireball^1.25" --type "fire>=0.6" --ban "$BAN_FIRE_S"
o sorcerer_cryomancer_ice_lance --asc cryomancer --skill "ice_lance^1.3" --type "cold>=0.7" --ban "$BAN_COLD_S"
o sorcerer_spark_hybrid --type "lightning>=0.85" --slots --ban "$VENOM|$FIRE_NODES"

# Class ceilings for parity (spec 15.10): training DPS only, no theme, no keystones.
o parity/warrior_l60 --train-only --slots
o parity/sorcerer_l60 --train-only --slots
o parity/warrior_l20 --train-only --slots
o parity/sorcerer_l20 --train-only --slots
dotnet tools/BuildOptimizer/bin/Release/net8.0/BuildOptimizer.dll l1 --out-dir "$OUT/parity"

# Attribute-node swap variants (spec 15.9): regenerate from the sources after the sources change.
dotnet tools/BuildOptimizer/bin/Release/net8.0/BuildOptimizer.dll attrvar --in "$OUT/warrior_thunderer_crush.json" \
  --out "$OUT/attributes/warrior_no_attribute_nodes.json" --skill crush --type lightning --ban "$VENOM|keystone|int_flame_4"
dotnet tools/BuildOptimizer/bin/Release/net8.0/BuildOptimizer.dll attrvar --in "$OUT/sorcerer_pyromancer_fireball.json" \
  --out "$OUT/attributes/sorcerer_no_attribute_nodes.json" --skill fireball --type fire --ban "$VENOM|keystone|will_storm|int_frost|int_bitter|int_frozen"
