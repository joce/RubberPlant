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
dotnet run --project src/RubberPlant.Cli -- check examples/fig1-31a.rp
```

The output file's extension picks the format: `.svg` draws a line drawing; `.obj`
(with its colors in a `.mtl` file next to it), `.gltf` and `.glb` write a 3D mesh.
glTF is the most portable: one file, colors included, and leaves visible from both
sides in any viewer. Run `rubberplant --help` for all options. Settings given on the command line
(`-n`, `--angle`, …) override the file's values. The random seed isn't part of
the file: set it with `--seed` (default 0).

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
```

Spaces (or tabs) separate the arguments. Double quotes keep spaces in an argument,
and `""` is an empty one. Backslashes are plain characters, so Windows paths need no
escaping; there's no way to put a double quote inside an argument.

Each command prints what it would print on its own (`wrote …`, the derived string,
diagnostics on stderr), then `exit <code>` on stdout, with the exit code it would
have had: 0 success, 1 error, 2 usage error. A script writes a line and reads stdout
up to the `exit` line; stderr is flushed before it. Usage errors print one line,
without the usage text. Input and output are UTF-8. At the end of the input, `batch`
exits with 0.

Rendering all 40 examples takes about 0.45 s in one batch, 2 s as separate native
runs, and 9 s through `dotnet`.

From Python:

```python
import subprocess
rp = subprocess.Popen(["out/aot/rubberplant", "batch"], stdin=subprocess.PIPE,
                      stdout=subprocess.PIPE, text=True, encoding="utf-8", bufsize=1)

def run(*args):
    rp.stdin.write(" ".join(f'"{a}"' for a in args) + "\n")
    rp.stdin.flush()
    out = []
    while not (line := rp.stdout.readline()).startswith("exit "):
        out.append(line.rstrip("\r\n"))
    return int(line.split()[1]), out

code, out = run("derive", "examples/dragon.rp", "-n", "2")
```

## Tests

Tests use [Expecto](https://github.com/haf/expecto) and run as executables:

```
dotnet run --project tests/RubberPlant.Core.Tests
dotnet run --project tests/RubberPlant.Backends.Tests
dotnet run --project tests/RubberPlant.Cli.Tests
```

Every `examples/*.rp` has a golden SVG in `tests/RubberPlant.Backends.Tests/golden`
(a missing golden is a test failure). `golden/seeds.txt` lists the examples that
need a non-default seed. After an intended rendering change, regenerate the
goldens with `RUBBERPLANT_UPDATE_GOLDEN=1` and review the diff. Small hand-written
mesh fixtures have full OBJ, MTL and glTF goldens in `golden/mesh`, and
`golden/mesh-digests.txt` holds the SHA-256 of every example's OBJ and glTF.

`scripts/check-goldens.sh` renders every example through the CLI and compares it
with its goldens and digests (`RUBBERPLANT_CLI=out/aot/rubberplant.exe` checks the
native executable instead). `scripts/validate-examples.sh` runs the Khronos glTF
Validator on every example's glTF (needs Node: `npm ci --prefix scripts/validate-gltf`
once). CI runs both, plus all tests, on Linux and Windows, and checks the native
executable against the goldens on Windows (`.github/workflows/ci.yml`).

## Layout

```
src/RubberPlant.Core       pure pipeline: parser, checker, derivation, turtle, geometry
src/RubberPlant.Backends   output backends (SVG, OBJ, glTF) and the shared mesh builder
src/RubberPlant.Cli        command-line tool; all I/O lives here
tests/                     Expecto test projects
examples/                  ABOP figures written in RubberPlant
docs/                      language spec, roadmap; reference material (see docs/README.md)
```

The pipeline:

```
source → Parser → AST → Checker → Model → Derivation (seeded) → derived string
       → interpretation (as rules) → drawing string → Turtle → Geometry → backend
```
