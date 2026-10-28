#!/usr/bin/env bash
# Recipes that produced data/builds/ (Tasks 9–10). Re-run after a balance change, review the diff, then run the content tests:
#   dotnet test core/Dialoid.Core.Tests --filter "FullyQualifiedName~ReferenceBuildTests|FullyQualifiedName~BalanceAcceptanceTests"
# and `dotnet tools/BuildOptimizer/bin/Release/net8.0/BuildOptimizer.dll suite --all` for every acceptance metric in one table.
# Each run starts from the current file (local search keeps the build's idea); add --scratch-passives --scratch-trees
# to restart a build from nothing. Runs take 5–30 minutes each. Run the script alone and sequentially (the default): the
# keystone, attribute and level-20 steps read the reference files other runs rewrite. PARALLEL=1 runs the independent
# optimizer jobs concurrently (each level-20 run starts after its level-60 run) and fails if any background job fails.
# Objective and constraints: see the header of Program.cs. Since Task 10 calibrated «Атакующий босс» (spec 11.1), every
# reference build must survive at least 20 s (--min-survival 20, the lower bound of the 20–40 s corridor). The parity
# builds are the class ceilings among such viable builds at levels 20 and 60 (training DPS only, no theme); level 1 has no
# survival corridor (spec 11.1 calibrates levels 20 and 60) and stays an exhaustive one-skill search.
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
# Lightning sorcerer: no fire nodes and no Fireball-fork nodes (frost_orb is the cold Fireball fork), so only the lightning path stays.
FIREBALL_FORK_AND_FIRE_NODES="int_flame_3|int_flame_4|int_flame_5|int_flame_6|int_incineration|int_ignite|int_inferno|kindling|core_heat|fire_field|scorched_earth|sun_orb|solar_corona|ember_spark|smolder|arrival_flare|double_flare|fire_exit|molten_core|eternal_flame|lingering_heat|ring_of_fire|flame_mark|soul_flame|frost_orb"

BO="dotnet tools/BuildOptimizer/bin/Release/net8.0/BuildOptimizer.dll"
o() { local name=$1; shift; $OPT --in "$OUT/$name.json" --out "$OUT/$name.json" --rounds 2 --lns "$@"; }
PIDS=()
p() { if [ -n "${PARALLEL:-}" ]; then "$@" & PIDS+=($!); else "$@"; fi; }
# Waits for every background job and fails if any failed (a bare `wait` under set -e hides background failures).
waitall() { local pid rc=0; for pid in ${PIDS[@]+"${PIDS[@]}"}; do wait "$pid" || rc=1; done; PIDS=(); return $rc; }

# Level-60 reference builds (spec 12) and their level-20 versions (data/builds/l20/: the «эталоны уровня 20» of the boss
# calibration, spec 11.1, and of the loot check). A missing level-20 file is seeded from the level-60 build (`tolevel`).
# --asc-floor at level 60: every taken ascendancy notable passes the floor of ReferenceBuildTests (≥ 3% objective or ≥ 8% more
# damage taken in 20 s; --asc-exempt lists the off-theme fillers of ReferenceBuildTests.OffThemeNotables).
# The level-20 run starts after the level-60 run (it may be seeded from the level-60 file).
ref_seq() {
  local name=$1 exempt=$2; shift 2
  o "$name" --min-survival 20 --asc-floor ${exempt:+--asc-exempt "$exempt"} "$@"
  [ -f "$OUT/l20/$name.json" ] || $BO tolevel --in "$OUT/$name.json" --out "$OUT/l20/$name.json" --level 20
  o "l20/$name" --min-survival 20 "$@"
}
ref() { p ref_seq "$1" "" "${@:2}"; }
mkdir -p "$OUT/l20"
ref warrior_berserker_whirlwind --asc berserker --skill "whirlwind^1.25" --type "physical>=0.6" --ban "$BAN_PHYS"
ref warrior_thunderer_crush --asc thunderer --skill "crush^1.25" --type "lightning>=0.6" --ban "$BAN_LIGHTNING_W"
# «Огненный Громовержец»: Cleave reaches only ~1.2× the next skill (1.3 is infeasible; Berserker reaches 1.25 only with two
# zero-value notables) — see data/BALANCE_NOTES.md, «у Воителя нет асцендантской поддержки огня». At level 20 even 1.0 is
# out of reach (Cleave 27–29% against the fire Whirlwind's 36–40%; a penalized search wanders), so the level-20 version asks
# Cleave for a quarter of the damage instead.
p o warrior_fire_cleave_crit --min-survival 20 --asc-floor --asc thunderer --skill "cleave^1.15" --type "fire>=0.5" --ban "$BAN_FIRE_W"
p o l20/warrior_fire_cleave_crit --min-survival 20 --asc thunderer --skill "cleave>=0.25" --type "fire>=0.5" --ban "$BAN_FIRE_W"
ref sorcerer_pyromancer_fireball --asc pyromancer --skill "fireball^1.25" --type "fire>=0.6" --ban "$BAN_FIRE_S"
ref sorcerer_cryomancer_ice_lance --asc cryomancer --skill "ice_lance^1.3" --type "cold>=0.7" --ban "$BAN_COLD_S"
# «Грозовой Криомант»: lightning ≥ 60% (was 75% in Task 9): at 75% Ice Lance only filled the fifth slot, and only three of the
# six Cryomancer notables (Раскол, Хрупкость, Ледник) do anything for a lightning build, so the ascendancy floor failed.
p ref_seq sorcerer_spark_hybrid "glacier" --asc cryomancer --type "lightning>=0.6" --slots --ban "$VENOM|$FIREBALL_FORK_AND_FIRE_NODES"

# Class ceilings for parity (spec 15.10, «лучший эталонный билд» = the strongest viable build; BALANCE_NOTES): training DPS
# only, no theme, no keystones, survival ≥ 20 s. Drop --min-survival to see the unconstrained (glass-cannon) ceilings.
p o parity/warrior_l60 --train-only --slots --min-survival 20
p o parity/sorcerer_l60 --train-only --slots --min-survival 20
p o parity/warrior_l20 --train-only --slots --min-survival 20
p o parity/sorcerer_l20 --train-only --slots --min-survival 20
$BO l1 --out-dir "$OUT/parity"
waitall

# Keystone test builds (spec 15.6, BalanceAcceptanceTests): a reference build plus the shortest path to the keystone, the
# points taken from its cheapest passive leaves (`keystone`); the keystone stays a leaf. --attr sends every attribute choice
# to one attribute (Воля for Воля титана, Ловкость for Танец клинков, Сила for Каменная кожа).
K="$OUT/keystones"; mkdir -p "$K"
$BO keystone --in sorcerer_spark_hybrid --ks blood_pact --out "$K/blood_pact_fit.json"
$BO keystone --in warrior_berserker_whirlwind --ks blood_pact --out "$K/blood_pact_unfit.json"
$BO keystone --in sorcerer_pyromancer_fireball --ks titan_will --attr will --out "$K/titan_will_fit.json"
$BO keystone --in sorcerer_pyromancer_fireball --ks titan_will --out "$K/titan_will_unfit.json"
# Неотвратимость fits a hit build without crit and without ailments: the lightning Ball Lightning sorcerer re-searched with
# every crit node (passives and skill trees) banned and the keystone pinned (the thunderer's shock and Разряд zone lose to −30%
# ailment chance; it gained only ×1.07 at the spec's +18%).
CRIT="int_insight|int_arcane_precision|int_piercing_thought|dex_assassin|dex_volley_2|str_butcher|str_brute_5|str_ruthless_strike|str_brute_6|str_brute_7|precise_strike|edge|heavy_blow|crushing_ram|true_lunge|finishing_blow|keen_throw|headshot|ancestral_fury|bright_flash|dazzle|backlash|overheat|blast_wave|point|ice_crown|cold_precision|bright_discharge|flash|falling_star|comet_fragment"
OTHER_KS="keystone_blood|keystone_titan|keystone_avatar|keystone_blade|keystone_stone"
$BO keystone --in sorcerer_spark_hybrid --ks inevitability --out "$K/inevitability_fit.json" --ban "$VENOM|$FIREBALL_FORK_AND_FIRE_NODES|$CRIT|$OTHER_KS"
o keystones/inevitability_fit --type "lightning>=0.6" --slots --min-survival 20 --allow-keystones \
  --pin keystone_inevitability --ban "$VENOM|$FIREBALL_FORK_AND_FIRE_NODES|$CRIT|$OTHER_KS"
$BO keystone --in warrior_fire_cleave_crit --ks inevitability --out "$K/inevitability_unfit.json"
# Аватар пламени: the lightning/cold hybrid becomes a pure fire hit build, re-searched around the keystone (pinned; it
# then takes fire passives, which work on the converted damage only with the keystone: ×1.09 without the re-search);
# the ignite Pyromancer pays.
$BO keystone --in sorcerer_spark_hybrid --ks avatar_of_fire --out "$K/avatar_of_fire_fit.json"
o keystones/avatar_of_fire_fit --slots --min-survival 20 --allow-keystones --pin keystone_avatar_of_fire \
  --ban "$VENOM|keystone_blood|keystone_titan|keystone_inevitability|keystone_blade|keystone_stone"
$BO keystone --in sorcerer_pyromancer_fireball --ks avatar_of_fire --out "$K/avatar_of_fire_unfit.json"
$BO keystone --in sorcerer_pyromancer_fireball --ks blade_dance --attr dex --out "$K/blade_dance_fit.json"
$BO keystone --in warrior_thunderer_crush --ks blade_dance --out "$K/blade_dance_unfit.json"
$BO keystone --in warrior_berserker_whirlwind --ks stone_skin --attr str --out "$K/stone_skin_fit.json"
# Каменная кожа costs an Intelligence or Dexterity build its resistances and evasion: the Pyromancer with every attribute choice
# in Ловкость loses one boss attack (35 → 31.5 s). Survival moves in whole boss attacks (3.5 s), so the plain Intelligence
# builds do not show it: the Pyromancer stays at 35 s, and the Cryomancers (both, since Ледяная броня +550) are armor builds
# that gain.
$BO keystone --in sorcerer_pyromancer_fireball --ks stone_skin --attr dex --out "$K/stone_skin_unfit.json"

# Attribute-node swap variants (spec 15.9): regenerate from the sources after the sources change.
$BO attrvar --in "$OUT/warrior_thunderer_crush.json" \
  --out "$OUT/attributes/warrior_no_attribute_nodes.json" --skill crush --type lightning --ban "$VENOM|keystone|int_flame_4"
$BO attrvar --in "$OUT/sorcerer_pyromancer_fireball.json" \
  --out "$OUT/attributes/sorcerer_no_attribute_nodes.json" --skill fireball --type fire --ban "$VENOM|keystone|will_storm|int_frost|int_bitter|int_frozen"
