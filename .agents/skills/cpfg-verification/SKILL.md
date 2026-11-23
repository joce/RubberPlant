---
name: cpfg-verification
description: >-
  Verify RubberPlant derivations, 3D turtle geometry, and ABOP figures against
  cpfg (in WSL Ubuntu), the Python cpfg turtle emulator, and native-resolution
  ABOP PDF figures. Use when implementing ABOP chapters or turtle semantics,
  authoring or debugging examples/*.rp, or investigating discrepancies between
  printed ABOP formulas and book illustrations.
---

# ABOP & `cpfg` Verification Runbook

Use this runbook when cross-checking RubberPlant's L-system derivations, 3D geometry, or example figures against `cpfg` (the original software that produced ABOP's illustrations) and the ABOP PDF.

## 1. Boundaries & Local Reference Material

- **Never copy `cpfg`/`lpfg` (`vlab`) source code or copyrighted PDFs into the RubberPlant repository.**
- Local reference PDFs and extracted markdown live in git-ignored paths under `docs/` (`docs/abop.pdf`, `docs/abop/*.md`, `docs/lpfgman.pdf`, `docs/lpfg/lpfgman.md`). Fetch or extract them with:
  ```bash
  uv run docs/fetch_refs.py
  uv run docs/fetch_refs.py --png <book-page-numbers>
  ```
- The external `cpfg` workspace (built in WSL Ubuntu) lives outside the repo—conventionally at `~/refs/cpfg-work` (e.g., `C:/Users/<user>/refs/cpfg-work` on Windows).

## 2. Comparing Geometry & Derivations Against `cpfg`

Inside the external `cpfg-work` workspace:

- `REPORT.md` and `RESULTS-FOR-RUBBERPLANT.md` — prior Chapter 2–3 verification findings.
- `models/` — `cpfg` `.l` / `.v` model files.
- `build-scripts/` — scripts to build `cpfg`, render PostScript/PNG (`render.sh`), dump Rayshade `.ray` geometry (`dump.sh`), and compare segment-by-segment against RubberPlant (`ours-vs-cpfg-master.ps1`, `geomcmp.py`, `rp-segments.fsx`).
- `analysis/cpfgemu.py` — Python `cpfg` turtle emulator (verified against `cpfg` to `4.8e-6`).

Workflow for a new ABOP figure or feature:

1. Transcribe the book's L-system into a `cpfg` `.l` file (plus `.v` view file) in the external `cpfg-work` workspace.
2. Run `cpfg` in WSL Ubuntu to dump derived strings or `.ray` segment geometry.
3. Run RubberPlant on the corresponding `.rp` file and compare segment-by-segment (or string-by-string) using `geomcmp.py` / `rp-segments.fsx`.
4. Record any book-vs-`cpfg` discrepancies in `docs/abop-notes.md`.

## 3. Known `cpfg` Quirks & Semantic Mappings

Keep these differences in mind when translating between ABOP, `cpfg`, and RubberPlant:

- **Horizontal roll (`$`):** ABOP's `$` is `cpfg`'s `@v` (`cpfg`'s `$` ends a sub-L-system).
- **Tropism syntax:** Use `cpfg`'s **old** tropism directives (`tropism direction:`, `initial elasticity:`), not `tropism:`.
- **Preprocessor shim:** `cpfg`'s own `vlabcpp` preprocessor does not build cleanly; use the `gcc -E` shim (`build-scripts/vlabcpp-shim.sh`).
- **Roll frame convention:** `cpfg`'s frame starts with `U = (0, 0, -1)` and rolls `/` toward that `U`. RubberPlant keeps a right-handed frame (`U = (0, 0, 1)`) and rolls `/` toward `-U` (`docs/language.md` §5.1).
- **Tropism math:** `cpfg`'s old `bend()` applies two sequential rotations with sines `e * (T_hat · U)` then `e * (T_hat · L)` after each move (agreeing with ABOP's p. 61 errata that the formula gives the *sine* of the bend).

## 4. Comparing Visually Against ABOP PDF Figures

Full-page renders from `docs/fetch_refs.py --png` are only 144 DPI. For precise visual overlays:

1. Extract the embedded figure image from `docs/abop.pdf` at **native resolution** with `pymupdf` via `uv run python` (`page.get_images`, then `pymupdf.Pixmap`).
2. Overlay the RubberPlant render and the extracted book figure at the same scale and trunk width, aligned on the trunk base.

## 5. Authoring `examples/*.rp`

Follow these conventions for every ABOP example checked into `examples/`:

- **Faithful printed files vs. reconstructions:** Preserve the book's printed constants in the primary example file. When reproducing a figure requires going beyond the printed L-system, put the fitted version in a separate, clearly marked `*-figure.rp` file.
- **Mark all additions:** Every stand-in rule, added `as` interpretation rule, schema completion (e.g., ABOP §3.1), or reconstruction must be marked with a comment noting it is a RubberPlant addition.
- **State the stage:** Where the book shows multiple developmental stages, comment which stage the file renders.
- **Production order:** For each predecessor, list context-sensitive productions *above* context-free ones so the file derives identically under RubberPlant's ABOP §1.8 precedence and `cpfg`'s source-order rule.
- **Verify goldens:** After adding or editing an example, run the `verify-and-goldens` skill to generate and check its SVG golden, `mesh-digests.txt` entry, and glTF validity.
