# Core hardening and morphology telemetry (BP.1)

Status: **spec draft** (Phase 1 of [`battle-plan.md`](battle-plan.md), targeting `v0.3` tooling).

This document specifies execution guardrails (module and primitive budgets) and the `rubberplant stats` morphology analyzer so RubberPlant can be driven safely and deterministically by automated agents, optimizers, and synthetic dataset generators.

---

## 1. Scope and architecture

| In BP.1 | Not in BP.1 |
|---|---|
| `--max-modules` and `--max-primitives` execution budgets with safe defaults | Wall-clock timeouts or OS memory-limit sandboxing |
| Pure derivation & geometry telemetry (`Stats.fs` in `RubberPlant.Core`) | Mesh-level statistics (triangle count, vertex count, surface area) |
| `rubberplant stats` CLI command (compact JSON + `--pretty`) and `batch` support | 3D viewer integration (Phase 2) |
| Cross-platform deterministic digest checks across all 40 examples | Visual or silhouette metrics (Phase 2 / Phase 4) |

Architecture stays pure-core / edge-I/O:

- **`Geometry.Primitive` does not change.** Backends (`Svg`, `Mesh`, `Obj`, `Gltf`) and their tests remain untouched.
- **Budgets live in a `Limits` record** separate from `Model.Settings`: budgets are execution guardrails, not `.rp` language settings, and never alter the output of a model that stays within budget.
- **Telemetry flows through lightweight pure sidecars** returned alongside `Derived` and `Geometry`, consumed by `Stats.analyze` and serialized by `Stats.toJson` using `System.Text.Json.Utf8JsonWriter` (reflection-free, Native AOT compatible).

```
Model + Limits
  ──► Derivation.derive   ──► Derived  + DerivationTrace
  ──► Derivation.interpret ──► Drawing
  ──► Turtle.interpret    ──► Geometry + TurtleTrace + Warnings
                                  │
                                  ▼
                            Stats.analyze ──► Stats ──► Stats.toJson (UTF-8 JSON)
```

---

## 2. Execution budgets (`Limits`)

### 2.1 Core types and defaults

```fsharp
type Limits =
    { /// Maximum number of symbols (`Sym`) in an L-string (axiom, derivation, interpretation).
      MaxModules: int option
      /// Maximum number of primitives (`Segment` + `Polygon`) emitted by the turtle.
      MaxPrimitives: int option }

module Limits =
    let defaults =
        { MaxModules = Some 1_000_000
          MaxPrimitives = Some 250_000 }

    let unlimited =
        { MaxModules = None
          MaxPrimitives = None }
```

The largest current ABOP Chapter 1–3 examples (`fig2-8b`, `fig2-8c`) produce roughly 15k–30k primitives, so the defaults provide generous (~10–50×) headroom for normal botanical models while stopping exponentially exploding grammars in milliseconds.

### 2.2 Counting and enforcement rules

- **Module budget (`MaxModules`):**
  - Counts **every symbol (`Sym`)** in an `LString` — `Mod`, `Push` (`[`), `Pop` (`]`), and `Cut` (`%`), i.e., `LString.Length`. Counting all symbols closes the loophole where bracket-only growth (`B -> [][]B`) could exhaust memory with few `Mod` symbols, and also bounds the maximum number of polygon vertices in `Turtle` (since every vertex requires at least one `F`, `f`, or `.` symbol in `drawing`).
  - Checked at three places:
    1. **Axiom:** before step 1 (even when `derivations = 0`).
    2. **Every derivation step:** checked incrementally inside `rewrite` *before* appending each symbol to the output buffer. A runaway rule aborts mid-step in O(1) without constructing an oversized array.
    3. **Interpretation (`as` rules):** checked incrementally inside `rewrite` *before* appending each symbol.
  - **Boundary condition:** a string of length `<= limit` succeeds; attempting to append symbol `limit + 1` fails immediately.
  - **Memory hardening in `Derivation.fs`:**
    - Cap the `ResizeArray<Sym>` initial capacity against `MaxModules` (and avoid `int32` overflow in `str.Length * 2`).
    - `Derivation.derive` iterates without retaining every intermediate step in memory (only `deriveSteps` keeps all `n + 1` strings).

- **Primitive budget (`MaxPrimitives`):**
  - Counts **every emitted `Primitive`** (`Segment` and `Polygon`) in `Turtle.interpret`.
  - Checked incrementally *before* adding each primitive to the output buffer.
  - **Boundary condition:** `<= limit` primitives succeeds; attempting to emit primitive `limit + 1` fails immediately.

### 2.3 Diagnostics

Two new evaluation diagnostic codes in `Diagnostics.fs`:

| Code | Name | When emitted |
|---|---|---|
| `RP0206` | `ModuleLimitExceeded` | Symbol count exceeds `MaxModules` in the axiom, a derivation step, or `as` interpretation. The message names the stage (e.g., `axiom`, `derivation step 14`, `interpretation`) and the limit. |
| `RP0207` | `PrimitiveLimitExceeded` | Emitted primitive count exceeds `MaxPrimitives` in `Turtle.interpret`. The message names the limit. |

Exceeding a budget returns `Error [ diag ]`, prints the diagnostic on `stderr`, and exits with code **`1`** (matching all other runtime/evaluation errors). Inside `rubberplant batch`, the command prints `exit 1` and leaves the batch process healthy for subsequent commands.

### 2.4 CLI options

| Option | Value domain | Default | Applies to |
|---|---|---|---|
| `--max-modules <n\|unlimited>` | integer ≥ 1, or `unlimited` | `1000000` | `render`, `derive`, `stats` |
| `--max-primitives <n\|unlimited>` | integer ≥ 1, or `unlimited` | `250000` | `render`, `stats` |

- `0` and negative integers are rejected with exit code **`2`** (usage error), preventing accidental confusion between `0` and `unlimited`.
- Following the CLI's strict option-scoping policy (`backends.md` §8):
  - Passing `--max-primitives` to `derive` is a usage error (exit code `2`).
  - Passing `--max-modules` or `--max-primitives` to `check` is a usage error (exit code `2`).

---

## 3. Morphology telemetry (`rubberplant stats`)

### 3.1 Core telemetry sidecars

To keep `Geometry.Primitive` clean and backend-neutral, `Derivation` and `Turtle` emit lightweight trace records alongside their outputs:

```fsharp
type RuleFiring =
    { /// 1-based line number of the production rule in the source file.
      Line: int
      /// Strict predecessor module name.
      Predecessor: string
      /// Strict predecessor parameter count.
      Arity: int
      /// Total times this production rule fired across all derivation steps.
      Count: int }

type DerivationTrace =
    { Steps: int
      FixedPointReached: bool
      TotalRuleFirings: int
      RuleFirings: RuleFiring list }

type TurtleTrace =
    { /// Bracket stack depth (`stack.Count`) for each emitted primitive,
      /// parallel to `Geometry`. For a `Segment`, depth when drawn;
      /// for a `Polygon`, depth when `{` was opened.
      PrimitiveDepths: int[] }
```

- Production rules in `RuleFirings` appear in **source order** (the order of `model.Productions`), including rules with `Count = 0` so dead or unmatched rules are immediately visible.
- `TotalRuleFirings` is the sum of `Count` across all production rules (it does not count `as` interpretation rule firings).
- `FixedPointReached` is `true` if and only if `Steps > 0` and the final derivation step produced an `LString` structurally equal to the previous step (short-circuiting to `true` when zero rules fired and zero `%` cuts occurred on that step; `false` when `Steps = 0`).

### 3.2 `Stats` model and metric definitions (`Stats.fs`)

`Stats.analyze` is a pure function over `(Derived, Drawing, DerivationTrace, Geometry, TurtleTrace)` returning `Result<Stats, Diagnostic list>`.

#### `derivation` section

| Field | JSON type | Definition |
|---|---|---|
| `steps` | `int` | `model.Settings.Derivations` |
| `derived_modules` | `int` | `Derived.Length` (total `Sym` count, matching `--max-modules`) |
| `drawing_modules` | `int` | `Drawing.Length` (total `Sym` count after `as` interpretation) |
| `max_bracket_depth` | `int` | Maximum `[` nesting depth reached anywhere in `Derived` (`0` if unbranched) |
| `fixed_point_reached` | `bool` | `true` iff `steps > 0` and the final step left the L-string unchanged |
| `total_rule_firings` | `int` | Total production rule firings across all derivation steps |
| `rule_firings` | `array` | One `{ "line", "predecessor", "arity", "count" }` object per production rule in source order (including `count: 0`) |

#### `geometry` section

Geometry points are all `p0` and `p1` of `Segment` primitives and all vertices of `Polygon` primitives (identical to `Svg.fs` and `Mesh.fs`). World coordinates are right-handed with `+Y` up, `+X` right, `+Z` toward the front viewer, starting at `(0, 0, 0)`.

| Field | JSON type | Definition |
|---|---|---|
| `bbox` | `object \| null` | `{ "min": [x, y, z], "max": [x, y, z], "extent": [dx, dy, dz] }` over all geometry points, where `extent = max - min`; `null` if there are no points |
| `height` | `number \| null` | `bbox.extent[1]` (`max.y - min.y`); `null` if `bbox` is `null` |
| `crown_width` | `number \| null` | `max(bbox.extent[0], bbox.extent[2])` (equals 2D width when `extent.z = 0`); `null` if `bbox` is `null` |
| `aspect_ratio` | `number \| null` | `height / crown_width`; `null` if `bbox` is `null` or `crown_width = 0` |
| `segment_count` | `int` | Number of `Segment` primitives |
| `polygon_count` | `int` | Number of `Polygon` primitives |
| `total_segment_length` | `number` | Sum of Euclidean lengths of all segments (`0` if no segments), computed with scale-safe power-of-two scaling (as in `Mesh.fs`) |
| `width_min` | `number \| null` | Minimum `width` across all segments; `null` if `segment_count = 0` |
| `width_max` | `number \| null` | Maximum `width` across all segments; `null` if `segment_count = 0` |
| `center_of_mass` | `[x, y, z] \| null` | Length-weighted average of segment midpoints: $\frac{\sum \ell_i \cdot \frac{p_{0,i} + p_{1,i}}{2}}{\sum \ell_i}$. If `total_segment_length = 0` (e.g., polygon-only geometry or all zero-length segments), falls back to the unweighted mean of all geometry points. `null` if there are no points |
| `lean` | `[x, z] \| null` | Height-normalized horizontal displacement of `center_of_mass` from the origin: `[com.x / height, com.z / height]`. `null` if `center_of_mass` is `null` or `height = 0` |
| `branch_order` | `array` | Dense array for `order = 0 .. maxOrder` of `{ "order", "segment_count", "total_length", "polygon_count" }`; `[]` if there are no primitives |

### 3.3 Numeric safety and determinism

- **Finite-value guard:** `Turtle` already ensures every emitted coordinate is finite (`RP0205`), but derived quantities (`extent = max - min`, `total_segment_length`, `center_of_mass`, `aspect_ratio`, `lean`) can still overflow `double` on extreme inputs (e.g., `F(1e308)`). If any computed floating-point stat is non-finite (`Infinity` or `NaN`), `Stats.analyze` returns `Error [ Diagnostic.error Codes.InvalidGeometry None ... ]` (`RP0205`, exit code `1`) rather than failing during JSON serialization.
- **Scale-safe distance and averaging:** segment lengths use power-of-two scaling (matching `Mesh.fs`) so very large or very small finite segments do not overflow or underflow intermediate squares. Midpoints are computed as `p0 + 0.5 * (p1 - p0)` (when finite) to avoid `(p0 + p1) / 2` overflow.
- **JSON number formatting:** serialized via `Utf8JsonWriter`, using .NET's shortest round-trip representation (identical to `Gltf.fs`), with `-0.0` normalized to `0.0`.
- **Cross-platform determinism:** because RubberPlant computes `sin`, `cos`, and `tan` identically across platforms and `.NET` uses deterministic Ryu float formatting, `Stats.toJson` produces bit-identical UTF-8 output on Linux, Windows, and Windows Native AOT.

### 3.4 Example JSON output (`--pretty`)

```json
{
  "derivation": {
    "steps": 5,
    "derived_modules": 1234,
    "drawing_modules": 1234,
    "max_bracket_depth": 4,
    "fixed_point_reached": false,
    "total_rule_firings": 980,
    "rule_firings": [
      {
        "line": 4,
        "predecessor": "A",
        "arity": 1,
        "count": 420
      }
    ]
  },
  "geometry": {
    "bbox": {
      "min": [-4.2, 0, -3.1],
      "max": [4.2, 12.5, 3.1],
      "extent": [8.4, 12.5, 6.2]
    },
    "height": 12.5,
    "crown_width": 8.4,
    "aspect_ratio": 1.4880952380952381,
    "segment_count": 320,
    "polygon_count": 128,
    "total_segment_length": 410.2,
    "width_min": 0.5,
    "width_max": 10,
    "center_of_mass": [0.1, 6.2, -0.05],
    "lean": [0.008, -0.004],
    "branch_order": [
      {
        "order": 0,
        "segment_count": 12,
        "total_length": 15,
        "polygon_count": 0
      }
    ]
  }
}
```

---

## 4. CLI and `batch` protocol

```
rubberplant stats <file.rp> [--pretty] [options]
```

- Prints **compact single-line JSON** to `stdout` by default (followed by a single `\n`), so every `stats` call in `rubberplant batch` produces exactly one line of output before `exit 0`.
- `--pretty` prints 2-space indented JSON with LF newlines (`\n`). Passing `--pretty` to `render`, `derive`, or `check` is a usage error (exit code `2`).
- No `-o` flag (matching `derive`: output goes to `stdout`, warnings and errors go to `stderr` so `stdout` stays valid JSON).
- Accepts all setting override flags (`-n`, `--angle`, `--step`, `--width`, `--taper`, `--tropism`, `--elasticity`, `--seed`) plus `--max-modules` and `--max-primitives`.
- Works identically inside `rubberplant batch`:
  - `stats examples/fig2-8b.rp` prints one JSON line, then `exit 0`.
  - `stats exploding.rp --max-modules 1000` prints the `RP0206` diagnostic on `stderr` and `exit 1` on `stdout`, leaving `batch` ready for the next line.

---

## 5. Testing and CI parity

### PR 1 — Spec and execution budgets (`joce/bp1-budgets`)

- `docs/core-hardening.md` and `docs/README.md` link.
- Unit tests in `RubberPlant.Core.Tests`:
  - Module limit hit on axiom, mid-step during exponential derivation (`F -> FF` at `derivations = 40`), bracket-only explosion (`B -> [][]`), and `as` interpretation (`RP0206`).
  - Primitive limit hit on segments and polygons (`RP0207`).
  - Exact boundary checks (`count = limit` succeeds, `count = limit + 1` fails).
  - `Limits.unlimited` bypasses limits.
- CLI and `batch` tests in `RubberPlant.Cli.Tests`:
  - `--max-modules` / `--max-primitives` parsing (`unlimited`, valid ints, `0`, negative, non-int).
  - Strict command scoping (`--max-primitives` rejected on `derive`; both rejected on `check`).
  - `batch` recovery: an exploding grammar fails fast with `exit 1` and the very next valid command succeeds with `exit 0`.

### PR 2 — `rubberplant stats` and cross-platform determinism (`joce/bp1-stats`)

- Unit tests in `RubberPlant.Core.Tests`:
  - Derivation telemetry: `derived_modules` vs `drawing_modules` (with `as` rules), `max_bracket_depth`, `fixed_point_reached` (reached via no matching rules, reached via identity rule `A -> A`, not reached, and `derivations = 0`), per-rule firing counts including 0-count dead rules in source order.
  - Geometry telemetry: 2D and 3D bounding boxes, `crown_width`, `aspect_ratio`, `width_min`/`width_max`, length-weighted `center_of_mass`, polygon-only fallback, zero-length segment fallback, `lean` normalization, horizontal `height = 0` (`lean = null`), empty geometry (all nullable fields `null`, `branch_order = []`), dense `branch_order` distribution across nested `[...]` and `{...}`, extreme-coordinate overflow guard (`RP0205`), `-0` normalization.
- CLI and `batch` tests in `RubberPlant.Cli.Tests`:
  - `stats` compact output (single line, valid JSON) and `--pretty` output (indented JSON).
  - `--pretty` rejected on `render`/`derive`/`check`.
  - Warnings go to `stderr`, leaving `stdout` pure JSON.
  - `stats` inside `batch`, including budget failure recovery.
- **40-example determinism golden check:**
  - `tests/RubberPlant.Backends.Tests/golden/stats-digests.txt` records the SHA-256 of compact `rubberplant stats` JSON for every `examples/*.rp` (with `golden/seeds.txt`).
  - Verified by Expecto tests and `scripts/check-goldens.sh` on Ubuntu, Windows, and Windows Native AOT in CI.
