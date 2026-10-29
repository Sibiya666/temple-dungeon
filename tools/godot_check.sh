#!/usr/bin/env bash
# Build and headless check of the Godot debug scene (spec §3.2):
#   dotnet build -> godot --headless --import -> godot --headless --quit (or -- --selftest),
# then the log is checked. Every step runs under a timeout.
#
# Usage: tools/godot_check.sh [--selftest] [--expect-load-failure]
#   --selftest             run the scene's self-test instead of a plain start
#   --expect-load-failure  the scene must report a data load failure (marker DIALOID_LOAD_FAILED:)
#                          and the log must still have no ERROR lines; used for the missing-data check
#
# Exit codes:
#   0        success
#   1        build or import failed, ERROR / SCRIPT ERROR line in the Godot log,
#            or --expect-load-failure given but the scene loaded its data
#   2        Godot not found or bad arguments
#   3        the scene could not load the game data (marker DIALOID_LOAD_FAILED: in the log)
#   other    the self-test's own exit code (checked before the log)
#   124/142  a step hit its time limit (timeout/gtimeout: 124, perl alarm: 142)
set -u

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
GAME="$ROOT/game"
MARKER="DIALOID_LOAD_FAILED:"

SELFTEST=0
EXPECT_LOAD_FAILURE=0
for arg in "$@"; do
  case "$arg" in
    --selftest) SELFTEST=1 ;;
    --expect-load-failure) EXPECT_LOAD_FAILURE=1 ;;
    *) echo "Неизвестный аргумент: $arg (допустимы --selftest и --expect-load-failure)" >&2; exit 2 ;;
  esac
done

# Godot binary: $GODOT, else the macOS app, else godot in PATH.
if [ -z "${GODOT:-}" ]; then
  if [ -x /Applications/Godot_mono.app/Contents/MacOS/Godot ]; then
    GODOT=/Applications/Godot_mono.app/Contents/MacOS/Godot
  elif command -v godot >/dev/null 2>&1; then
    GODOT="$(command -v godot)"
  fi
fi
if [ -z "${GODOT:-}" ] || ! command -v "$GODOT" >/dev/null 2>&1; then
  echo "Установите Godot 4 .NET: brew install --cask godot-mono (или укажите путь в переменной GODOT)" >&2
  exit 2
fi

# run_limited SECONDS cmd args...: timeout, gtimeout, or a perl alarm (macOS has no timeout).
run_limited() {
  local secs="$1"; shift
  if command -v timeout >/dev/null 2>&1; then
    timeout "$secs" "$@"
  elif command -v gtimeout >/dev/null 2>&1; then
    gtimeout "$secs" "$@"
  else
    perl -e 'alarm shift; exec @ARGV or die "exec: $!"' "$secs" "$@"
  fi
}

is_timeout() { [ "$1" -eq 124 ] || [ "$1" -eq 142 ]; }

# fail_step CODE MESSAGE: a timeout keeps its code, any other failure is 1.
fail_step() {
  local code="$1"; shift
  echo "$* (код $code)" >&2
  if is_timeout "$code"; then exit "$code"; fi
  exit 1
}

LOG="$(mktemp "${TMPDIR:-/tmp}/godot_check.XXXXXX")"
trap 'rm -f "$LOG"' EXIT

step() { echo "== $*"; }

step "dotnet build"
run_limited 600 dotnet build "$GAME/Dialoid.Game.csproj" -nologo -v quiet
code=$?
[ $code -eq 0 ] || fail_step $code "Сборка не прошла"

step "godot --import"
run_limited 120 "$GODOT" --headless --path "$GAME" --import >>"$LOG" 2>&1
code=$?
if [ $code -ne 0 ]; then
  cat "$LOG"; fail_step $code "godot --import завершился с ошибкой"
fi

if [ $SELFTEST -eq 1 ]; then
  step "godot -- --selftest"
  run_limited 300 "$GODOT" --headless --path "$GAME" -- --selftest >>"$LOG" 2>&1
else
  step "godot --quit"
  run_limited 60 "$GODOT" --headless --path "$GAME" --quit >>"$LOG" 2>&1
fi
code=$?

cat "$LOG"

# The Godot run's own code (self-test result or timeout) wins over the log checks.
if [ $code -ne 0 ]; then
  echo "Godot завершился с кодом $code" >&2
  exit "$code"
fi

# Godot colors its output: strip ANSI codes, then match only Godot's own error lines.
CLEAN="$(sed $'s/\x1b\\[[0-9;]*m//g' "$LOG")"
if printf '%s\n' "$CLEAN" | grep -qE '^(SCRIPT )?ERROR:'; then
  echo "В логе Godot есть ERROR" >&2
  exit 1
fi

if printf '%s\n' "$CLEAN" | grep -q "^$MARKER"; then
  if [ $EXPECT_LOAD_FAILURE -eq 1 ]; then
    echo "OK (ожидаемая ошибка загрузки данных)"
    exit 0
  fi
  echo "Сцена не загрузила данные игры" >&2
  exit 3
fi
if [ $EXPECT_LOAD_FAILURE -eq 1 ]; then
  echo "Ожидалась ошибка загрузки данных, но сцена загрузила их" >&2
  exit 1
fi
echo "OK"
