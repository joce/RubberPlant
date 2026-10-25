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

status=0
for f in examples/*.rp; do
  name=$(basename "$f" .rp)

  if [ ! -f "$golden/$name.svg" ]; then
    echo "MISSING golden: $golden/$name.svg"; status=1; continue
  fi
  dotnet run --no-build -c Release --project src/RubberPlant.Cli -- render "$f" -o "$out/$name.svg" > /dev/null
  if cmp -s <(tr -d '\r' < "$out/$name.svg") <(tr -d '\r' < "$golden/$name.svg"); then
    echo "ok       $name"
  else
    echo "DIFFERS  $name"; status=1
    diff <(tr -d '\r' < "$golden/$name.svg") <(tr -d '\r' < "$out/$name.svg") | head -c 2000 || true
  fi
done

for g in "$golden"/*.svg; do
  [ -f "examples/$(basename "$g" .svg).rp" ] || { echo "ORPHAN golden: $g"; status=1; }
done

exit $status
