---
name: verify-and-goldens
description: >-
  Run RubberPlant's Expecto test suites, regenerate or verify SVG and mesh
  goldens, validate the Windows Native AOT build, and run the Khronos glTF
  Validator. Use when testing changes, adding or updating examples/*.rp,
  modifying rendering or mesh output, or checking CI parity before pushing a PR.
---

# Verify & Goldens Runbook

Use this runbook to run RubberPlant's full verification pipeline and match `.github/workflows/ci.yml`.

## 1. Build and Expecto Test Suites

Build in Release mode (warnings are errors) and run all three Expecto test executables:

```bash
dotnet build -c Release --nologo
dotnet run --no-build -c Release --project tests/RubberPlant.Core.Tests
dotnet run --no-build -c Release --project tests/RubberPlant.Backends.Tests
dotnet run --no-build -c Release --project tests/RubberPlant.Cli.Tests
```

Note: `RubberPlant.Cli.Tests` enforces the Native AOT `%s`-only printf formatting rule across `src/`.

## 2. Golden Files Layout

Goldens live under `tests/RubberPlant.Backends.Tests/golden/`:

- `<example>.svg` — exact byte-for-byte SVG golden for every `examples/<example>.rp` (LF line endings).
- `seeds.txt` — `<example> <seed>` entries for examples that require a non-default seed (all others use seed `0`).
- `mesh/` — full OBJ, MTL, and glTF goldens for small hand-written mesh fixtures.
- `mesh-digests.txt` — SHA-256 digests of every example's `OBJ+MTL` bytes and `.gltf` bytes.

Missing or orphan goldens/digests are test failures.

## 3. Regenerating Goldens (Intended Output Changes Only)

When an output change is intentional, regenerate all goldens and `mesh-digests.txt` via `RubberPlant.Backends.Tests`, then review `git diff` carefully before keeping them.

**PowerShell (`pwsh`):**

```powershell
$env:RUBBERPLANT_UPDATE_GOLDEN = "1"
try {
    dotnet run --project tests/RubberPlant.Backends.Tests
} finally {
    Remove-Item Env:RUBBERPLANT_UPDATE_GOLDEN -ErrorAction SilentlyContinue
}
```

**Bash:**

```bash
RUBBERPLANT_UPDATE_GOLDEN=1 dotnet run --project tests/RubberPlant.Backends.Tests
```

If adding a new `examples/<name>.rp` that needs a non-zero seed, add `<name> <seed>` to `tests/RubberPlant.Backends.Tests/golden/seeds.txt` *before* regenerating goldens.

## 4. CLI Golden Check

Compare every `examples/*.rp` rendered via the CLI against the committed SVGs and `mesh-digests.txt`:

```bash
bash scripts/check-goldens.sh
```

`scripts/check-goldens.sh` never modifies goldens; it exits non-zero on any byte mismatch, missing golden, or orphan file.

## 5. Native AOT Build & Golden Check (Windows)

On Windows (requires Visual Studio C++ build tools / Developer Command Prompt):

```bash
dotnet publish src/RubberPlant.Cli -c Release -r win-x64 --nologo -o out/aot
RUBBERPLANT_CLI=out/aot/rubberplant.exe bash scripts/check-goldens.sh
```

The Native AOT output must match the managed build's goldens byte-for-byte.

## 6. Khronos glTF Validator

Validate every example's `.gltf` and `.glb` plus `golden/mesh/*.gltf` (requires Node):

```bash
npm ci --prefix scripts/validate-gltf --no-audit --no-fund   # once
bash scripts/validate-examples.sh
```

## 7. Final CI Sanity Check

CI verifies that running tests never mutates or leaves untracked files in the golden directory:

```bash
git status --porcelain --untracked-files=all -- tests/RubberPlant.Backends.Tests/golden
```

Expect empty output (unless you intentionally updated goldens for the current PR and have reviewed every diff).
