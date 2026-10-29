#!/usr/bin/env bash
# Renders every examples/*.rp with the CLI and compares:
# - the SVG, byte for byte, with tests/RubberPlant.Backends.Tests/golden/<name>.svg;
# - the SHA-256 of the OBJ (+ MTL) and of the .gltf with golden/mesh-digests.txt.
# The goldens are the source of truth. Fails on any difference, on an example without a golden or
# digest, or a golden or digest without an example.
# Never rewrites goldens: to accept an intended change, run the Backends tests with
# RUBBERPLANT_UPDATE_GOLDEN=1 and review the diff.
set -euo pipefail

cd "$(dirname "$0")/.."
golden=tests/RubberPlant.Backends.Tests/golden
out=$(mktemp -d)
trap 'rm -rf "$out"' EXIT

dotnet build src/RubberPlant.Cli -c Release --nologo -v q
cli() { dotnet src/RubberPlant.Cli/bin/Release/net10.0/rubberplant.dll "$@"; }

# golden/seeds.txt: `<example> <seed>` lines (run parameters, §4.7); others use seed 0.
seed_of() { tr -d '\r' < "$golden/seeds.txt" | awk -v n="$1" '$1 !~ /^#/ && $1 == n { print $2 }'; }
# golden/mesh-digests.txt: `<example> <sha256 of obj+mtl> <sha256 of gltf>` lines.
digests_of() { tr -d '\r' < "$golden/mesh-digests.txt" | awk -v n="$1" '$1 !~ /^#/ && $1 == n { print $2, $3 }'; }
sha() { sha256sum | cut -d' ' -f1; }

status=0
for f in examples/*.rp; do
  name=$(basename "$f" .rp)
  args=()
  seed=$(seed_of "$name")
  [ -n "$seed" ] && args+=(--seed "$seed")

  if [ ! -f "$golden/$name.svg" ]; then
    echo "MISSING golden: $golden/$name.svg"; status=1; continue
  fi
  cli render "$f" "${args[@]}" -o "$out/$name.svg" > /dev/null
  # Exact bytes: no newline normalization (goldens are LF via .gitattributes).
  if cmp -s "$out/$name.svg" "$golden/$name.svg"; then
    echo "ok       $name.svg"
  else
    echo "DIFFERS  $name.svg"; status=1
    cmp "$golden/$name.svg" "$out/$name.svg" || true
    diff "$golden/$name.svg" "$out/$name.svg" | head -c 2000 || true
  fi

  expected=$(digests_of "$name")
  if [ -z "$expected" ]; then
    echo "MISSING digests: $name in $golden/mesh-digests.txt"; status=1; continue
  fi
  cli render "$f" "${args[@]}" -o "$out/$name.obj" > /dev/null
  cli render "$f" "${args[@]}" -o "$out/$name.gltf" > /dev/null
  actual="$(cat "$out/$name.obj" "$out/$name.mtl" | sha) $(sha < "$out/$name.gltf")"
  if [ "$actual" = "$expected" ]; then
    echo "ok       $name meshes"
  else
    echo "DIFFERS  $name meshes: expected $expected, got $actual"; status=1
  fi
done

for g in "$golden"/*.svg; do
  [ -f "examples/$(basename "$g" .svg).rp" ] || { echo "ORPHAN golden: $g"; status=1; }
done
for name in $(tr -d '\r' < "$golden/mesh-digests.txt" | awk '$1 !~ /^#/ && NF { print $1 }'); do
  [ -f "examples/$name.rp" ] || { echo "ORPHAN digests: $name"; status=1; }
done

exit $status
