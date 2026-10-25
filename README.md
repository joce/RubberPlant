# RubberPlant

[![CI](https://github.com/joce/RubberPlant/actions/workflows/ci.yml/badge.svg?branch=master)](https://github.com/joce/RubberPlant/actions/workflows/ci.yml)
[![Version 0.1](https://img.shields.io/badge/version-0.1-blue)](docs/roadmap.md)

An L-system modeling tool, following *The Algorithmic Beauty of Plants* (ABOP).
The language is specified in [`docs/language.md`](docs/language.md).

Version 0.1 implements all of ABOP Chapter 1. See [`docs/roadmap.md`](docs/roadmap.md)
for what comes next.

## Build and run

Requires the .NET 10 SDK.

```
dotnet build
dotnet run --project src/RubberPlant.Cli -- render examples/koch-island.rp -o koch.svg
dotnet run --project src/RubberPlant.Cli -- derive examples/dragon.rp -n 3
dotnet run --project src/RubberPlant.Cli -- check examples/fig1-31a.rp
```

Run `rubberplant --help` for all options. Settings given on the command line
(`-n`, `--angle`, …) override the file's values. The random seed isn't part of
the file: set it with `--seed` (default 0).

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
goldens with `RUBBERPLANT_UPDATE_GOLDEN=1` and review the diff.

`scripts/check-goldens.sh` renders every example through the CLI and compares it
byte for byte with its golden; CI runs it, plus all tests, on Linux and Windows
(`.github/workflows/ci.yml`).

## Layout

```
src/RubberPlant.Core       pure pipeline: parser, checker, derivation, turtle, geometry
src/RubberPlant.Backends   output backends (SVG)
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
