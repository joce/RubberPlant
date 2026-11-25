#!/usr/bin/env bash
# Headless smoke test for `rubberplant-viewer` (used by CI on the Windows Native
# AOT binary under Mesa, and locally against either AOT or managed builds).
# Set RUBBERPLANT_VIEWER=<executable> to test a specific binary.
set -euo pipefail

cd "$(dirname "$0")/.."
raw_out=$(mktemp -d)
trap 'rm -rf "$raw_out"' EXIT

if command -v cygpath >/dev/null 2>&1; then
  out=$(cygpath -m "$raw_out")
else
  out="$raw_out"
fi

if [ -n "${RUBBERPLANT_VIEWER:-}" ]; then
  viewer() { "$RUBBERPLANT_VIEWER" "$@"; }
else
  dotnet build src/RubberPlant.Viewer -c Release --nologo -v q
  viewer() { dotnet src/RubberPlant.Viewer/bin/Release/net10.0/rubberplant-viewer.dll "$@"; }
fi

check_png() {
  local file="$1"
  local label="$2"
  if [ ! -s "$file" ]; then
    echo "FAIL: $label missing or empty: $file" >&2
    exit 1
  fi
  local sig
  sig=$(od -An -tx1 -N8 "$file" | tr -s ' \t\r\n' ' ' | sed 's/^ //; s/ $//')
  if [ "$sig" != "89 50 4e 47 0d 0a 1a 0a" ]; then
    echo "FAIL: $label bad PNG signature: '$sig'" >&2
    exit 1
  fi
  echo "ok       $label ($sig)"
}

sha() { sha256sum | cut -d' ' -f1; }

expected_stats=$(tr -d '\r' < tests/RubberPlant.Backends.Tests/golden/stats-digests.txt | awk '$1 == "fig2-6a" { print $2 }')
if [ -z "$expected_stats" ]; then
  echo "FAIL: fig2-6a missing from stats-digests.txt" >&2
  exit 1
fi

# 1. One-shot contact-sheet with --stats and --stats-out
viewer contact-sheet examples/fig2-6a.rp -o "$out/sheet.png" --size 256 --stats --stats-out "$out/stats.json" > "$raw_out/stdout.json"
check_png "$out/sheet.png" "one-shot contact-sheet"

if ! cmp -s "$raw_out/stdout.json" "$out/stats.json"; then
  echo "FAIL: --stats stdout differs from --stats-out file" >&2
  exit 1
fi

actual_stats=$(sha < "$raw_out/stdout.json")
if [ "$actual_stats" != "$expected_stats" ]; then
  echo "FAIL: --stats SHA-256 mismatch: expected $expected_stats, got $actual_stats" >&2
  exit 1
fi
echo "ok       one-shot --stats matches golden ($actual_stats)"

# 2. Stateful batch session including odd-width snapshot, silhouette, budget recovery, and stats
set +e
viewer batch > "$raw_out/batch.out" 2> "$raw_out/batch.err" <<EOF
load examples/fig2-6a.rp
snapshot "$out/snap.png" --size 301x199
silhouette "$out/mask.png" --size 128
load examples/fig2-6a.rp --max-modules 5
stats
quit
EOF
batch_rc=$?
set -e

if [ "$batch_rc" -ne 0 ]; then
  echo "FAIL: batch exited with $batch_rc" >&2
  cat "$raw_out/batch.err" >&2
  exit 1
fi

check_png "$out/snap.png" "batch odd-width snapshot"
check_png "$out/mask.png" "batch silhouette"

if ! grep -q "RP0206" "$raw_out/batch.err"; then
  echo "FAIL: expected RP0206 diagnostic in batch stderr" >&2
  cat "$raw_out/batch.err" >&2
  exit 1
fi

mapfile -t batch_lines < <(tr -d '\r' < "$raw_out/batch.out")
if [ "${#batch_lines[@]}" -ne 9 ]; then
  echo "FAIL: expected 9 lines on batch stdout, got ${#batch_lines[@]}" >&2
  printf '%s\n' "${batch_lines[@]}" >&2
  exit 1
fi

[ "${batch_lines[0]}" = "exit 0" ] || { echo "FAIL line 0: ${batch_lines[0]}" >&2; exit 1; }
[ "${batch_lines[1]}" = "wrote $out/snap.png" ] || { echo "FAIL line 1: ${batch_lines[1]}" >&2; exit 1; }
[ "${batch_lines[2]}" = "exit 0" ] || { echo "FAIL line 2: ${batch_lines[2]}" >&2; exit 1; }
[ "${batch_lines[3]}" = "wrote $out/mask.png" ] || { echo "FAIL line 3: ${batch_lines[3]}" >&2; exit 1; }
[ "${batch_lines[4]}" = "exit 0" ] || { echo "FAIL line 4: ${batch_lines[4]}" >&2; exit 1; }
[ "${batch_lines[5]}" = "exit 1" ] || { echo "FAIL line 5: ${batch_lines[5]}" >&2; exit 1; }
recovered_stats=$(printf '%s\n' "${batch_lines[6]}" | sha)
[ "$recovered_stats" = "$expected_stats" ] || { echo "FAIL recovered stats digest: $recovered_stats" >&2; exit 1; }
[ "${batch_lines[7]}" = "exit 0" ] || { echo "FAIL line 7: ${batch_lines[7]}" >&2; exit 1; }
[ "${batch_lines[8]}" = "exit 0" ] || { echo "FAIL line 8: ${batch_lines[8]}" >&2; exit 1; }
echo "ok       batch protocol, PNG outputs, and budget recovery"

# 3. Interactive view mode with --watch and live stdin bridge (snapshot, stats, quit)
viewer view examples/fig2-6a.rp --watch --size 320x240 > "$raw_out/view.out" 2> "$raw_out/view.err" <<EOF
snapshot "$out/view-snap.png" --size 128
stats
quit
EOF

check_png "$out/view-snap.png" "interactive view snapshot"
mapfile -t view_lines < <(tr -d '\r' < "$raw_out/view.out")
if [ "${#view_lines[@]}" -ne 5 ]; then
  echo "FAIL: expected 5 lines on view stdout, got ${#view_lines[@]}" >&2
  printf '%s\n' "${view_lines[@]}" >&2
  exit 1
fi
[ "${view_lines[0]}" = "wrote $out/view-snap.png" ] || { echo "FAIL view line 0: ${view_lines[0]}" >&2; exit 1; }
[ "${view_lines[1]}" = "exit 0" ] || { echo "FAIL view line 1: ${view_lines[1]}" >&2; exit 1; }
view_stats=$(printf '%s\n' "${view_lines[2]}" | sha)
[ "$view_stats" = "$expected_stats" ] || { echo "FAIL view stats digest: $view_stats" >&2; exit 1; }
[ "${view_lines[3]}" = "exit 0" ] || { echo "FAIL view line 3: ${view_lines[3]}" >&2; exit 1; }
[ "${view_lines[4]}" = "exit 0" ] || { echo "FAIL view line 4: ${view_lines[4]}" >&2; exit 1; }
echo "ok       interactive view live stdin bridge, snapshot, and stats"
