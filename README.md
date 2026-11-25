# RubberPlant

[![CI](https://github.com/joce/RubberPlant/actions/workflows/ci.yml/badge.svg?branch=master)](https://github.com/joce/RubberPlant/actions/workflows/ci.yml)
[![Version](https://img.shields.io/github/v/release/joce/RubberPlant?label=version)](https://github.com/joce/RubberPlant/releases/latest)

An L-system modeling tool, following *The Algorithmic Beauty of Plants* (ABOP).
The language is specified in [`docs/language.md`](docs/language.md).

Version 0.2 implements ABOP Chapters 1–3, with SVG, OBJ and glTF output. See
[`docs/roadmap.md`](docs/roadmap.md) for what comes next.

## Build and run

Requires the .NET 10 SDK. On Windows, build from a Developer Command Prompt for
Visual Studio (with the C++ tools): Native AOT links with them.

```
dotnet build
dotnet run --project src/RubberPlant.Cli -- render examples/koch-island.rp -o koch.svg
dotnet run --project src/RubberPlant.Cli -- render examples/fig2-8b.rp -o tree.glb
dotnet run --project src/RubberPlant.Cli -- derive examples/dragon.rp -n 3
dotnet run --project src/RubberPlant.Cli -- stats examples/fig2-8b.rp --pretty
dotnet run --project src/RubberPlant.Cli -- check examples/fig1-31a.rp
```

The output file's extension picks the format: `.svg` draws a line drawing; `.obj`
(with its colors in a `.mtl` file next to it), `.gltf` and `.glb` write a 3D mesh.
glTF is the most portable: one file, colors included, and leaves visible from both
sides in any viewer. Run `rubberplant --help` for all options. Settings given on the command line
(`-n`, `--angle`, …) override the file's values. The random seed isn't part of
the file: set it with `--seed` (default 0). `stats` prints deterministic
derivation and 3D geometry morphology statistics as compact single-line JSON
(add `--pretty` for indented output). Execution budgets (`--max-modules`,
default 1,000,000; `--max-primitives`, default 250,000) stop runaway grammars
cleanly and can be adjusted or set to `unlimited` ([`docs/core-hardening.md`](docs/core-hardening.md)).

### Native executable

`dotnet run` starts the .NET runtime and compiles the code on every run: about 200 ms,
far more than the work itself for most models. For scripts and quick iteration, publish
the native (Native AOT) executable, which runs in about 35 ms:

```
dotnet publish src/RubberPlant.Cli -c Release -r win-x64 -o out/aot
out/aot/rubberplant render examples/dragon.rp -o dragon.svg
```

It gives the same output as `dotnet run`: CI checks it against the goldens. Only
`win-x64` is checked so far.

Native AOT has one rule for code in `src`: printf-style formatting (`$"…%d{n}"`,
`sprintf "%g"`) may only format strings, as in `%s{string n}`. Formatting a number
that way can crash the native executable; a CLI test enforces the rule.

### Batch mode

A script that runs RubberPlant hundreds of times still pays the startup on every
run. `rubberplant batch` starts once and runs commands read from stdin, one per
line, written as you'd type them after `rubberplant`:

```
render "my trees\tree.rp" -o tree.svg --seed 3
derive tree.rp -n 5
stats tree.rp
```

Spaces (or tabs) separate the arguments. Double quotes keep spaces in an argument,
and `""` is an empty one. Backslashes are plain characters, so Windows paths need no
escaping; there's no way to put a double quote inside an argument.

Each command prints what it would print on its own (`wrote …`, the derived string,
diagnostics on stderr), then `exit <code>` on stdout, with the exit code it would
have had: 0 success, 1 error, 2 usage error. A script writes a line and reads stdout
up to the line that is exactly `exit <code>`; stderr is flushed before it. Match the
whole line: a derived string can start with `exit ` (a module named `exit`), but never
has a digit after a space, so no other output line has that form. Usage errors print
one line, without the usage text. Input and output are UTF-8. At the end of the input,
`batch` exits with 0.

Rendering all 40 examples takes about 0.45 s in one batch, 2 s as separate native
runs, and 9 s through `dotnet`.

From Python:

```python
import re, subprocess
rp = subprocess.Popen(["out/aot/rubberplant", "batch"], stdin=subprocess.PIPE,
                      stdout=subprocess.PIPE, text=True, encoding="utf-8", bufsize=1)

def run(*args):
    rp.stdin.write(" ".join(f'"{a}"' for a in args) + "\n")
    rp.stdin.flush()
    out = []
    while line := rp.stdout.readline():
        line = line.rstrip("\r\n")
        if re.fullmatch(r"exit \d+", line):
            return int(line[5:]), out
        out.append(line)
    raise EOFError("rubberplant batch stopped")

code, out = run("derive", "examples/dragon.rp", "-n", "2")
```

## 3D Viewer (`rubberplant-viewer`)

`RubberPlant.Viewer` renders `.rp` models directly on the GPU (OpenGL 3.3 Core) with no OBJ/glTF round-trip ([`docs/viewer.md`](docs/viewer.md)):

```
dotnet run --project src/RubberPlant.Viewer -- examples/fig2-8b.rp
dotnet run --project src/RubberPlant.Viewer -- contact-sheet examples/fig2-8b.rp -o sheet.png --stats
dotnet run --project src/RubberPlant.Viewer -- snapshot examples/fig2-8b.rp -o snap.png --camera 35,25
dotnet run --project src/RubberPlant.Viewer -- silhouette examples/fig2-8b.rp -o mask.png --size 512
```

- **Interactive `view` window** (`rubberplant-viewer view [file.rp] [--watch]` or `<file.rp>` shorthand):
  - **Mouse:** left-drag to orbit, right-drag / middle-drag / `Shift` + left-drag to pan, scroll wheel to zoom.
  - **Shortcuts:** `1` Front, `2` Side +90°, `3` 3/4 Iso, `4` Top-Down, `F` fit, `W` wireframe, `G` ground grid, `R` reload from disk, `Esc` / `Q` quit.
  - **Auto-reload (`--watch`):** opt-in file watcher that automatically reloads the `.rp` file when saved on disk (keeping your camera and preserving the previous valid scene on errors).
  - **Live stdin bridge:** while the window is open, send `batch` commands (`load`, `reload`, `camera`, `fit`, `wireframe on|off`, `grid on|off`, `snapshot`, `contact-sheet`, `silhouette`, `stats`, `quit`) over stdin with `exit <code>` framing.
- **Headless & `batch` modes:** `contact-sheet` (2×2 shared-scale PNG), `snapshot`, `silhouette` (binary `{0, 255}` 8-bit grayscale mask), `--stats` / `--stats-out` (byte-identical to `rubberplant stats`), and stateful `batch` over stdin/stdout.
- **Native AOT executable:** publish alongside `glfw3.dll` (Windows) or `libglfw.so.3` (Linux):

```
dotnet publish src/RubberPlant.Viewer -c Release -r win-x64 -o out/aot-viewer
out/aot-viewer/rubberplant-viewer examples/fig2-8b.rp
```

## Tests

Tests use [Expecto](https://github.com/haf/expecto) and run as executables:

```
dotnet run --project tests/RubberPlant.Core.Tests
dotnet run --project tests/RubberPlant.Backends.Tests
dotnet run --project tests/RubberPlant.Cli.Tests
dotnet run --project tests/RubberPlant.Viewer.Tests
```

Every `examples/*.rp` has a golden SVG in `tests/RubberPlant.Backends.Tests/golden`
(a missing golden is a test failure). `golden/seeds.txt` lists the examples that
need a non-default seed. After an intended rendering change, regenerate the
goldens with `RUBBERPLANT_UPDATE_GOLDEN=1` and review the diff. Small hand-written
mesh fixtures have full OBJ, MTL and glTF goldens in `golden/mesh`,
`golden/mesh-digests.txt` holds the SHA-256 of every example's OBJ and glTF, and
`golden/stats-digests.txt` holds the SHA-256 of every example's compact `stats` JSON.

`scripts/check-goldens.sh` renders every example through the CLI and compares it
with its goldens and digests (`RUBBERPLANT_CLI=out/aot/rubberplant.exe` checks the
native executable instead). `scripts/check-viewer-aot.sh` smoke-tests `rubberplant-viewer`
headless and interactive stdin rendering (`RUBBERPLANT_VIEWER=out/aot-viewer/rubberplant-viewer.exe`
checks the native executable). `scripts/validate-examples.sh` runs the Khronos glTF
Validator on every example's glTF (needs Node: `npm ci --prefix scripts/validate-gltf`
once). CI runs all of these, plus all tests (under Xvfb + Mesa on Linux and Mesa on Windows),
on Linux and Windows, and checks both native executables on Windows (`.github/workflows/ci.yml`).

## Layout

```
src/RubberPlant.Core       pure pipeline: parser, checker, derivation, turtle, geometry, stats
src/RubberPlant.Backends   output backends (SVG, OBJ, glTF) and the shared mesh builder
src/RubberPlant.Cli        command-line tool; all I/O lives here
src/RubberPlant.Viewer     direct-from-.rp OpenGL 3.3 Core 3D viewer (window + headless + batch)
tests/                     Expecto test projects
examples/                  ABOP figures written in RubberPlant
docs/                      language spec, roadmap; reference material (see docs/README.md)
```

The pipeline:

```
source → Parser → AST → Checker → Model → Derivation (seeded) → derived string
       → interpretation (as rules) → drawing string → Turtle → Geometry → backend
```
