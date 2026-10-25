# Handoff: migrate the implementation to the new prelude syntax

Working document, not canonical. `docs/language.md` is the source of truth; if this file disagrees with it, the spec wins. Replace this file when the work is done.

## Task

The spec was changed (this branch, `docs/language.md`): `#directive value` lines are gone, replaced by a `;`-terminated **prelude**. The F# implementation on `master` (v0.1: parser, checker, derivation, turtle, SVG, CLI) still implements the old syntax. Bring code, examples and tests in line with the spec. **No behavior of the pipeline beyond parsing/checking should change.**

Read first: `docs/language.md` §1, §2.2–§2.6, §3.1–§3.2, §4.2, §4.7, Appendix A (A.1–A.4). The spec is authoritative.

## New syntax in one screen

```
settings { derivations = 10; angle = 86; }   (* optional, at most one *)

let c = 1;                                   (* any number, anywhere in the prelude *)
let q = c - p;                               (* only earlier lets are visible *)

ignore: + - F;                               (* optional, at most one, non-empty *)
axiom: F(1,0);                               (* exactly one; string may be empty *)

F(x, 0) -> F(x*p,2) + …;                     (* rules come last *)
```

- `=` takes an expression, `:` takes a module string. Every statement ends with `;`; only the `settings` block ends with `}`. Newlines are plain whitespace everywhere: **delete all line-sensitivity** (the `InDirective` state, `endOfDirective`, `DirectiveNotAtLineStart`, `LastTokenLine`-style "directive must be first on its line" logic, as far as they exist only for directives).
- Prelude items may be interleaved in any order (a `let` between `settings` and `axiom:` is fine), but all must precede the first rule. A prelude item after a rule is an error ("declaration after rules").
- `settings` keys: `derivations angle step width taper`. **`seed` is removed from the language**; it is a CLI-only parameter. Settings values are constant expressions that may use earlier `let`s.
- `#` is reserved everywhere with no meaning (error).

## Reserved words and names (spec §2.6, §4.2)

- Fully reserved, never a module / `let` / parameter / function name: `settings let ignore axiom include module`, `as when else weight`, and the setting names `derivations angle step width taper`. Built-in function names stay reserved as before (`sin cos … max`). No built-in constants.
- One namespace, **no shadowing**: a name has exactly one role (module or turtle-predefined, `let` constant, formal parameter, built-in function, setting name, keyword). Same parameter name in different rules is fine. A `let` name may be defined only once; a `let` may reference only earlier `let`s (forward reference is an error).
- Parameter names in one head must be distinct (`_` excepted), as before.
- `include` and `module` are reserved but have no syntax yet: using them anywhere is an error (keep a "reserved, not supported yet" message).

## What to change (indicative; check each file)

- `src/RubberPlant.Core/Ast.fs`: `Item` cases (`Setting`/`Define`/`Ignore`/`Axiom`/`RuleItem`) probably stay close to the same; the settings block needs positions per entry. Drop `Seed` from `SettingKey`. Rename `Define` to `Let` if you like.
- `Parser.fs` (the bulk): replace the directive parser with `settings { … }`, `let`, `ignore:`, `axiom:` parsers; make `:` after `axiom`/`ignore` switch to module mode and `;` end it; remove newline sensitivity. The "missing `;`" detection previously relied on running into a `#` directive: it must now trigger on a prelude keyword or the next rule's arrow, and `#`-as-directive messages go away. Expression mode applies to settings values and `let` values.
- `Checker.fs`: prelude-count checks, declaration-after-rules, duplicate/unknown settings keys, reserved-name checks for `let`, parameters and module names (including the setting names), no-shadowing/one-namespace for the new roles, forward `let` references, empty `ignore:`. Drop the seed validation.
- `Model.fs` / `Derivation.fs` / `Pipeline.fs` / `Cli/Program.fs`: the seed comes from the host only (default `0`). `--seed` already exists; keep its range check (|seed| ≤ 2^53 − 1), but don't reference `Checker`'s settings for it. `Model.Settings.Seed` can stay as a run parameter or move to a run-options type, your call.
- `Diagnostics.fs`: retire `UnknownDirective`, `ReservedDirective`, `DirectiveNotAtLineStart`; add codes for the new errors (list below). Keep existing codes stable where the meaning is unchanged.
- `examples/*.rp`: convert all 17. `stochastic.rp` used `#seed 1`: the seed can no longer live in the file; update its comment, and make whatever test renders it pass `--seed 1` (or the equivalent API option) so its golden stays identical.
- `README.md`, `docs/README.md`: update if they mention directives.

## Errors that need a clear message and a test

Each of these gets at least one negative test (parser or checker, wherever it is detected), asserting the code and position:

1. Missing `axiom:`; a second `axiom:`.
2. A second `settings` block; a second `ignore:`.
3. Empty `ignore:` (`ignore: ;`). An empty `axiom: ;` is **legal**.
4. A prelude statement (`settings`, `let`, `ignore:`, `axiom:`) after the first rule: "declaration after rules".
5. Unknown settings key; a settings key set twice; `seed` as a settings key (unknown key; maybe hint that seed is CLI-only).
6. Each reserved word used as a module name, a `let` name and a parameter name: `settings let ignore axiom include module`, `as when else weight`, and each setting name (`angle` etc.). `include`/`module` anywhere else is an error too.
7. `let` named like a built-in function; parameter named like a built-in function; module named like a built-in function (existing behavior, re-check).
8. Duplicate `let`; forward `let` reference (`let a = b; let b = 1;`); `let` using itself.
9. Name clash across roles with both line numbers in the message: module vs `let`, module vs parameter, `let` vs parameter, module vs turtle-predefined name as `let`.
10. Same parameter name in two different rules is **accepted**; duplicate parameter within one head is an error.
11. `:` where not allowed (e.g. at the start of a statement, `axiom` without `:`, `ignore` without `:`); `=` missing in `let`/settings entry; `let x: 3;` and `axiom = F;` give a helpful error (use `=` for expressions, `:` for modules).
12. Missing `;` after `let`, `ignore:`, `axiom:` and after a settings entry, and missing `}`.
13. `#` anywhere (including at the start of a line, where it used to start a directive) is an error.
14. Interleaving is **accepted**: `let a = 1; settings { … } let b = 2; ignore: …; let c = 3; axiom: …;` followed by rules, and a `let` can use `a` and `b`, not forward ones.
15. Newlines/comments between any tokens are accepted: a `let` or `ignore:` spread over several lines, and several statements on one line.
16. Settings values can use earlier `let`s and built-in functions; they can't reference settings or parameters.
17. Reserved-word and no-shadowing checks happen after the whole file is parsed (modules are not declared), so the module-vs-`let` message works regardless of order.

Existing tests in `tests/RubberPlant.Core.Tests/{ParserTests,CheckerTests}.fs` and `tests/RubberPlant.Cli.Tests/CliTests.fs` need their sources converted; keep each test's intent. Remove tests that only exercised directive line-sensitivity, or convert them into the newline-is-whitespace tests above.

## Verification (do this in order)

1. **Before changing anything**, on a clean `master`-based checkout, build, run all three test projects, and snapshot canonical outputs for every example, for example:
   ```
   for f in examples/*.rp; do
     n=$(basename $f .rp)
     dotnet run --project src/RubberPlant.Cli -- derive $f -n 3 > /tmp/before/$n.derive.txt   # adjust -n per example, but be consistent
     dotnet run --project src/RubberPlant.Cli -- render $f -o /tmp/before/$n.svg
   done
   ```
   (`stochastic.rp` with `#seed 1` before; the same seed passed through `--seed 1` after.) The committed goldens in `tests/RubberPlant.Backends.Tests/golden/` are the other canonical reference.
2. After the migration, rerun every example and **diff against the snapshot: derived strings and SVGs must be byte-identical.** `git diff tests/RubberPlant.Backends.Tests/golden` must be empty: **do not regenerate goldens** (`RUBBERPLANT_UPDATE_GOLDEN`) to make a failure go away. A diff means the migration changed behavior; find out why.
3. All three test projects pass (`dotnet run --project tests/…`).
4. Every example under `examples/` passes `check`.

## Process notes

- Python: always `uv` (`uv run`, `uv add`), never pip/venv.
- Environment: Windows, Git Bash. .NET SDK 10.
- Don't change the language on your own. If the spec is ambiguous or you find a conflict (for example a rule that can't be parsed unambiguously), stop and ask, quoting the section.
- Open spec points you may hit: none known. Decided but not in code yet: `axiom: ;` legal, `ignore: ;` illegal, single `settings` block, `axiom:` last in the prelude is a convention, not enforced.
- Remote: `git@github.com:joce/RubberPlant.git`. The repo is public.
