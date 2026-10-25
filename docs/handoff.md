# Handoff: RubberPlant, from design to implementation

Written at the end of the design phase, for the agent starting implementation.

## 1. What RubberPlant is

An L-system modeling tool, rewritten from scratch. The old C#/ANTLR code was deleted; it survives only in git history before commit `31c1b3c`, and nothing from it should be reused.

**Roadmap** (proposed; the user agreed to the overall direction). The version numbers are notional: "0.5" just means "all of ABOP, but not the final product", and the real number could be anything below 1.0. The milestones and their order are what matter:
- **0.1**: Chapter 1 basics: D0L-systems, turtle, brackets, the language spec, SVG output.
- **0.2**: stochastic, context-sensitive and parametric L-systems, plus the expression language.
- **0.3**: 3D turtle, glTF/OBJ export, Chapters 2–3.
- **0.4**: Chapters 4–6 (phyllotaxis, organ surfaces, animation).
- **0.5**: Chapters 7–8 (map L-systems, fractals). **All of ABOP covered.**
- **1.0**: L-Py / lpfg-level capability, including **scripting**.

The spec (`docs/language.md`) already covers all of Chapter 1, including stochastic, context-sensitive and parametric systems. The 0.1/0.2 split above is only an implementation-order suggestion.

## 2. Working with the user

- **Spec first, code second.** Language decisions are made in `docs/language.md` and reviewed by the user before implementation. Don't silently change the language. Propose changes, explain the trade-offs, and wait for a decision.
- The user reviews carefully and pushes back. Give an honest opinion with a recommendation, not just a list of options.
- Preferences shown during design:
  - **Functional architecture**: a pure core, with I/O only at the edges.
  - **One spelling per meaning** (no aliases such as `&` / `&&`).
  - **Restrictions belong in the grammar** when possible, not in prose checks.
  - C-like leanings for operators, F#-style for comments.
  - Prefers readable, explicit syntax over terse book notation, while keeping book examples easy to transcribe.
- **Python: always use `uv`** (`uv run`, `uv add`), never pip or venv. This is from the user's global AGENTS.md.
- Environment: Windows, Git Bash shell. .NET SDKs **10.0.401** and 9.0.318 are installed. uv 0.13.
- Remote: `git@github.com:joce/RubberPlant.git`, branch `master`. **The repo is public.**

## 3. Repository state

```
.gitignore          PDFs + extracted reference text are ignored
docs/README.md      how to fetch the reference material
docs/fetch_refs.py  downloads + extracts ABOP and the lpfg manual (uv script)
docs/language.md    THE language spec (draft v0.1, Chapter 1)
docs/handoff.md     this file
```

There's no code yet.

**Reference material** (git-ignored, copyrighted, freely downloadable):
```
uv run docs/fetch_refs.py               # download if missing + extract
uv run docs/fetch_refs.py --png 24 27   # render ABOP book pages to PNG (figures, formulas)
```
- `docs/abop/*.md`: ABOP text, one file per chapter, with `<!-- pdf p.N / book p.M -->` markers (book page = pdf page − 12). Search with grep, then read only the relevant part.
- `docs/abop/pages/*.png`: rendered pages. Use them for figures and math, since the extracted text loses subscripts and mangles formulas.
- `docs/lpfg/lpfgman.md`: the lpfg (L+C) manual, a useful design reference for 1.0.
- ABOP **Appendix B** (`docs/abop/11-appendix-b.md`, near the end) has the book's full turtle symbol table.

## 4. The language spec

`docs/language.md` is the source of truth. **Appendix A is the authoritative grammar**; §2–§3 explain it, and §4–§5 define the semantics. Read it fully before writing the parser.

Key decisions, already settled with the user (don't relitigate without reason):

| Topic | Decision |
|---|---|
| Tokenization | Names are maximal runs `(Letter\|_)(Letter\|Digit\|_)*`; `FF` is **one** name, `F F` is two. Punctuation symbols are 1 char and self-delimiting. No module declarations. |
| Brackets | `[` `]` are structural; the grammar guarantees balance. No brackets in left contexts or `#ignore`. |
| Directives | `#name value`, line-based, no `:`/`=`/`;` (`#angle 86`, `#define h (p*q)^0.5`, `#axiom F(1,0)`). `#define` is a numeric constant, **not** a macro. |
| Rules | End with `;`, free layout. `Head -> succ;` (production) and `Head as succ;` (interpretation, applied once before drawing, never fed back). |
| Branches | `Head : …` opens a branch list: `when expr -> …` guards, `else -> …`, `weight expr -> …` for stochastic. A rule never mixes `->` and `as`. `else` is only allowed in a weighted rule if every branch is guarded. Interpretation rules have no weights. |
| Patterns | Formal parameters can be names, number literals or `_`: `B(0) -> C;` |
| Modules | Identity = name + parameter count (`A`, `A(1)`, `A(2,3)` all differ). The turtle only looks at the name and reads the first parameter. |
| Namespace | One namespace: a module name can't also be a define, parameter or built-in function. |
| Expressions | C-style: `== != < <= > >= && \|\| !`, `^` for power (right-assoc). `=`, `&`, `\|` are reserved. All values are 64-bit floats; true = 1, false = 0. |
| Angles | **Degrees** everywhere (turtle and trig functions). |
| Comments | `(* … *)` only, and they nest. No line comments. |
| Reserved | Rule keywords `as when else weight`. In rules, `#` and `` ` `` are errors. `#module` and `#include` are reserved directives. |
| Derivation | Parallel rewrite. Rules with context are tried before rules without; then source order; then a branch is chosen in the first rule that fires; if nothing fires, fall through; otherwise the module is copied unchanged. The seed is explicit. |
| Turtle | Initial H = +Y, L = −X, U = +Z. `!` multiplies width by (1 − `#taper`/100), default 30. `!(w)` sets the width. |

Temporary decisions, possibly revisited later: module declarations dropped; no namespaces or multiple L-systems per file; the backtick and `#` reserved inside rules; the `taper` name and its default. `~H` (predefined surfaces) is deferred to Chapter 5.

## 5. Architecture (agreed direction)

- **Language: F#.** The user wanted something functional with good parsing support and eventual scripting. Haskell was considered; Lisps were rejected.
- **Parser: FParsec** (parser combinators). The two lexer modes and the line-based directives fit combinators naturally. ANTLR was rejected (its lexer can't easily switch modes).
- **A pure pipeline**, with I/O only in the backends:
  ```
  source → tokens/AST → checked model → derivation (explicit RNG seed)
         → derived string → interpretation (as rules) → turtle → geometry → backend
  ```
- **Geometry is a backend-neutral data type** (segments and polygons for Chapter 1; later capsules/generalized cylinders and surfaces). The user specifically wants **capsules** rather than plain cylinders when we get to 3D.
- **Backends are replaceable**: plain functions from geometry to output. SVG first (2D projection; it fits the drawing to the viewport), then OBJ/glTF.
- **The turtle is 3D from day one**: 2D is just rotation about U.
- **Scripting (1.0)**: keep an extension point where expression evaluation can call host functions. The lpfg model (`Head : { … produce …; }`) is the reference. The choice of scripting language (F# scripts, Lua, Python via pythonnet, …) is still open.

## 6. Not decided yet (ask the user)

- Solution and project layout, project names, target framework (net10.0 is likely).
- Test framework (Expecto, or xUnit + FsCheck, …).
- CLI shape (arguments, output paths). The spec says "a CLI flag overrides the file value" for settings.
- File extension: the spec says `.rp`, but it was never explicitly confirmed.
- Error-reporting format (positions, messages). The spec promises a few specific messages: missing `;`, `=` → "use `==`", namespace clashes with both line numbers, unknown directive.

## 7. Suggested first steps

1. Propose a solution layout and test framework to the user. Something like `src/RubberPlant.Core` (AST, parser, checker, derivation, turtle, geometry), `src/RubberPlant.Backends` (SVG), `src/RubberPlant.Cli`, `tests/…`.
2. AST types that mirror Appendix A.2.
3. Lexer/parser with FParsec, following Appendix A. Test with every example in the spec, including the §6 worked examples, and with negative tests for each reserved or illegal construct.
4. Checker for the Appendix A.3 constraints.
5. Derivation: D0L first, then contexts (§4.5 has the bracket-aware algorithm and the book's worked example), guards, weights with a seeded RNG, patterns.
6. Interpretation (`as`), turtle (§5), geometry, SVG backend.
7. Golden tests against book figures. Render the PNG pages (`--png`) to compare visually. Good first targets: Koch island (Fig 1.6), Fig 1.24a–f, dragon (1.10a), Hilbert 3D (1.19), Fig 1.31 (context-sensitive).
