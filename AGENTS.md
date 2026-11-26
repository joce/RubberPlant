# RubberPlant Agent Guidelines

RubberPlant is an L-system modeling tool following *The Algorithmic Beauty of Plants* (ABOP), written in F# (`net10.0`) with FParsec and Expecto.

## Authoritative Documentation

Read the relevant docs before proposing or making changes:

- `docs/language.md` — L-system language specification (Appendix A is the authoritative grammar).
- `docs/backends.md` — 3D geometry, shared mesh builder, and output backends (SVG, OBJ, glTF).
- `docs/roadmap.md` — ABOP milestones and current version status.
- `docs/battle-plan.md` — Multi-phase roadmap from core hardening and 3D viewer through AI/ML workflows.
- `docs/abop-notes.md` — Book errata candidates, `cpfg` findings, and example-authoring notes.

## Working With the User

- Give honest technical opinions with a clear recommendation; expect careful review and pushback.
- Ask before proceeding when requirements or semantics are ambiguous, and keep the user informed as you go.
- **Spec first, code second.** Language and major architectural changes go into `docs/` (`docs/language.md`, `docs/backends.md`, or a phase spec) and are reviewed before writing code.
- **No backward compatibility to preserve.** There are no external users or clients yet; any earlier design decision can be revisited cleanly.
- **Semantics priority:** stay faithful to ABOP; where the book is silent, follow `cpfg` (which generated ABOP's figures), then `lpfg`. When ABOP's printed formulas and figures conflict, the *figures* win.

## Architecture & Code Invariants

- **Pure functional core, I/O at the edges.** `src/RubberPlant.Core` and `src/RubberPlant.Backends` must stay pure and deterministic; all I/O lives in `src/RubberPlant.Cli` (and future thin shells).
- **Language & API design.** Prefer one spelling per meaning, encode restrictions in the grammar, and keep `.rp` syntax readable.
- **Cross-platform determinism.** `sin`, `cos`, and `tan` are computed by RubberPlant so 3D geometry and mesh digests are bit-identical across Linux and Windows. Do not introduce platform-dependent math or non-determinism into core geometry generation.
- **Native AOT formatting rule.** In `src/`, printf-style formatting (`$"…%s{…}"`, `sprintf`, `failwithf`, etc.) may **only** format strings (e.g., `%s{string n}`), never numbers (`%d`, `%g`, `%f`, `%c`, `%x`). Formatting numbers that way can crash the Native AOT executable; a CLI test enforces this rule.
- **Compiler strictness.** Warnings are errors (`TreatWarningsAsErrors`, `WarnOn 3390;1182`, and `--reflectionfree` in `src/`). Keep `Core` and `Backends` Native AOT compatible.
- **Python.** Always use `uv` (`uv run python`, `uv run <script>`). Never use bare `python`, `pip`, or `venv`.
- **External references.** Copyrighted PDFs (`docs/abop.pdf`, `docs/lpfgman.pdf`) are git-ignored; `cpfg`/`lpfg` (`vlab`) source lives outside this repo. Never commit them or copy their source into RubberPlant.

## Git & PR Workflow

- **Branches:** `joce/<topic>`.
- **Commit and PR prefixes:** `feat:`, `change:`, `fix:`, `internal:`.
- **Line endings:** LF everywhere (`.editorconfig`, `.gitattributes`), including on Windows.
- **One PR per plan step**, each with tests (and updated goldens when output intentionally changes).
- **Independent review.** Before merging a PR, run an independent review pass (e.g., a fresh reviewer subagent or separate review session) covering spec alignment, adversarial numeric/complexity edge cases (non-dyadic float drift, `Double.MaxValue`/`Int32.MaxValue` overflow, quadratic degenerate inputs, unbounded allocations), tests, and repository invariants.
- **CI parity.** All tests, golden checks, the Windows Native AOT golden check, and the Khronos glTF Validator must pass on both Ubuntu and Windows, and the branch must be up to date with `master`.
- **GitHub PR review & comment triage.** After CI finishes and before merging any PR, inspect all GitHub reviews, top-level comments (`gh pr view <n> --json reviews,comments`), and inline diff comments (`gh api repos/:owner/:repo/pulls/<n>/comments`). Critically triage every comment (including bot/review-agent comments): fix real issues, push, and re-verify CI, and briefly note why any suggestion is declined before merging.
- **Ask before merging.** Squash-merge only (the squash commit takes the PR title and body). After merging, update `master` with `git pull --ff-only`.

## Workspace Skills

Use the repo's on-demand skills under `.agents/skills/` when relevant:

- `verify-and-goldens` — running Expecto suites, regenerating/checking SVG and mesh goldens, verifying the Native AOT build, and running the glTF validator.
- `cpfg-verification` — comparing derivations and 3D geometry against `cpfg`/WSL, extracting native-resolution ABOP PDF figures, and authoring ABOP example files.
