#!/usr/bin/env bash
# Renders every examples/*.rp as .gltf and .glb, and the mesh fixtures' golden .gltf files, and
# runs the Khronos glTF Validator on them (docs/backends.md §9). Fails if any has an error.
# Needs Node: run `npm ci --prefix scripts/validate-gltf` once first.
set -euo pipefail

cd "$(dirname "$0")/.."
golden=tests/RubberPlant.Backends.Tests/golden
out=$(mktemp -d)
trap 'rm -rf "$out"' EXIT

dotnet build src/RubberPlant.Cli -c Release --nologo -v q
cli() { dotnet src/RubberPlant.Cli/bin/Release/net10.0/rubberplant.dll "$@"; }
seed_of() { tr -d '\r' < "$golden/seeds.txt" | awk -v n="$1" '$1 !~ /^#/ && $1 == n { print $2 }'; }

for f in examples/*.rp; do
  name=$(basename "$f" .rp)
  args=()
  seed=$(seed_of "$name")
  [ -n "$seed" ] && args+=(--seed "$seed")
  cli render "$f" "${args[@]}" -o "$out/$name.gltf" > /dev/null
  cli render "$f" "${args[@]}" -o "$out/$name.glb" > /dev/null
done

node scripts/validate-gltf/validate.mjs "$out" "$golden/mesh"
