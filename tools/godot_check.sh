#!/usr/bin/env bash
# Build and headless check of the Godot debug scene (spec §3.2):
#   dotnet build -> godot --headless --import -> godot --headless --quit (or -- --selftest),
# then the log is scanned for ERROR. Every godot call runs under a timeout.
# Usage: tools/godot_check.sh [--selftest]
set -u

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
GAME="$ROOT/game"

SELFTEST=0
for arg in "$@"; do
  case "$arg" in
    --selftest) SELFTEST=1 ;;
    *) echo "Неизвестный аргумент: $arg (допустим только --selftest)" >&2; exit 2 ;;
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
# On expiry the exit code is 124 (timeout/gtimeout) or 142 (perl SIGALRM).
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

LOG="$(mktemp "${TMPDIR:-/tmp}/godot_check.XXXXXX")"
trap 'rm -f "$LOG"' EXIT

step() { echo "== $*"; }

step "dotnet build"
dotnet build "$GAME/Dialoid.Game.csproj" -nologo -v quiet || { echo "Сборка не прошла" >&2; exit 1; }

step "godot --import"
run_limited 120 "$GODOT" --headless --path "$GAME" --import >>"$LOG" 2>&1
code=$?
if [ $code -ne 0 ]; then
  cat "$LOG"; echo "godot --import завершился с кодом $code" >&2; exit 1
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

if grep -qE "ERROR|SCRIPT ERROR" "$LOG"; then
  echo "В логе Godot есть ERROR" >&2
  exit 1
fi
if [ $code -ne 0 ]; then
  echo "Godot завершился с кодом $code" >&2
  exit "$code"
fi
echo "OK"
