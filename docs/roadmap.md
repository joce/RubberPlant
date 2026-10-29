# Roadmap

**Current version: 0.1.** All of ABOP Chapter 1 is implemented.

Version numbers below 1.0 are notional: "0.4" means "all of ABOP, but not the
final product". What matters is the milestones and their order.

| Version | Scope | Status |
|---|---|---|
| 0.1 | ABOP Chapter 1 | ✅ released |
| 0.2 | 3D output (OBJ, glTF); ABOP Chapters 2–3 | in progress |
| 0.3 | ABOP Chapters 4–6 | |
| 0.4 | ABOP Chapters 7–8: **all of ABOP covered** | |
| 1.0 | L-Py / lpfg-level capability, including scripting | |

## 0.1: Chapter 1 ✅

- The language specification, [`language.md`](language.md).
- D0L-systems, turtle interpretation, branching (`[` `]`).
- Stochastic, context-sensitive and parametric L-systems, with the expression language.
- Interpretation (`as`) rules.
- A 3D turtle; backend-neutral geometry (segments, polygons).
- SVG backend, and the `rubberplant` CLI (`render`, `derive`, `check`).
- Every Chapter 1 figure as an example, each with a golden SVG checked in CI.

## 0.2: 3D output, Chapters 2–3

- ✅ Spec first: extend `language.md` for the turtle features of Chapters 2–3.
- ✅ Language: `consider:`, `tropism` and `elasticity` settings, `%` (cut), `empty`.
- ✅ Turtle: `$` (roll to horizontal), tropism, `G` and `.` (pulled forward from Ch 5),
  `{` starting an empty polygon.
- ✅ Checked against cpfg, the software that made ABOP's figures: `/` and `\` roll the way
  cpfg's do, tropism bends in cpfg's two steps, and SVG lines have straight ends. RubberPlant's
  3D geometry matches cpfg's for every Chapter 2 figure.
- ✅ The Chapter 2 figures as examples, with golden SVGs.
- ✅ `sin`, `cos` and `tan` give the same bits on every platform, so 3D output is reproducible.
- ✅ 3D geometry: segments are cylinders, as in ABOP's software, and polygons are triangulated
  ([`backends.md`](backends.md)).
- ✅ Mesh backends: OBJ and glTF (`.gltf`, `.glb`), checked with the Khronos glTF Validator in CI
  ([`backends.md`](backends.md)).
- The Chapter 3 figures as examples, with golden outputs.

## 0.3: Chapters 4–6

Phyllotaxis (Ch 4), models of plant organs and predefined surfaces such as
`~H` (Ch 5), and animation of plant development (Ch 6).

## 0.4: Chapters 7–8

Map L-systems and cellular layers (Ch 7), and fractal properties of plants
(Ch 8). With this, all of ABOP is covered.

## 1.0: lpfg-level capability

Feature parity with L-Py / lpfg, including scripting: an extension point where
expression evaluation can call host functions, in the spirit of lpfg's
`Head : { … produce …; }` rules. The scripting language (F# scripts, Lua,
Python, …) is still to be chosen. The lpfg manual (`lpfg/lpfgman.md`, see
[`README.md`](README.md)) is the reference.
