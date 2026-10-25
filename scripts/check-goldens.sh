#!/usr/bin/env bash
# Renders every examples/*.rp with the CLI and compares the SVG byte for byte with
# tests/RubberPlant.Backends.Tests/golden/<name>.svg (the source of truth).
# Fails on any difference, on an example without a golden, or a golden without an example.
# Never rewrites goldens: to accept an intended change, run the Backends tests with
# RUBBERPLANT_UPDATE_GOLDEN=1 and review the diff.
set -euo pipefail

cd "$(dirname "$0")/.."
golden=tests/RubberPlant.Backends.Tests/golden
out=$(mktemp -d)
trap 'rm -rf "$out"' EXIT

dotnet build src/RubberPlant.Cli -c Release --nologo -v q

# golden/seeds.txt: `<example> <seed>` lines (run parameters, §4.7); others use seed 0.
seed_of() { tr -d '\r' < "$golden/seeds.txt" | awk -v n="$1" '$1 !~ /^#/ && $1 == n { print $2 }'; }

status=0
for f in examples/*.rp; do
  name=$(basename "$f" .rp)
  args=()
  seed=$(seed_of "$name")
  [ -n "$seed" ] && args+=(--seed "$seed")

  if [ ! -f "$golden/$name.svg" ]; then
    echo "MISSING golden: $golden/$name.svg"; status=1; continue
  fi
  dotnet run --no-build -c Release --project src/RubberPlant.Cli -- render "$f" "${args[@]}" -o "$out/$name.svg" > /dev/null
  # Exact bytes: no newline normalization (goldens are LF via .gitattributes).
  if cmp -s "$out/$name.svg" "$golden/$name.svg"; then
    echo "ok       $name"
  else
    echo "DIFFERS  $name"; status=1
    cmp "$golden/$name.svg" "$out/$name.svg" || true
    diff "$golden/$name.svg" "$out/$name.svg" | head -c 2000 || true
  fi
done

for g in "$golden"/*.svg; do
  [ -f "examples/$(basename "$g" .svg).rp" ] || { echo "ORPHAN golden: $g"; status=1; }
done

exit $status
