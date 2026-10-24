# RubberPlant

An L-system modeling tool, following *The Algorithmic Beauty of Plants* (ABOP).
The language is specified in [`docs/language.md`](docs/language.md).

## Build and run

Requires the .NET 10 SDK.

```
dotnet build
dotnet run --project src/RubberPlant.Cli -- render examples/koch-island.rp -o koch.svg
dotnet run --project src/RubberPlant.Cli -- derive examples/dragon.rp -n 3
dotnet run --project src/RubberPlant.Cli -- check examples/fig1-31a.rp
```

Run `rubberplant --help` for all options. Settings given on the command line
(`-n`, `--angle`, `--seed`, …) override the file's values.

## Tests

Tests use [Expecto](https://github.com/haf/expecto) and run as executables:

```
dotnet run --project tests/RubberPlant.Core.Tests
dotnet run --project tests/RubberPlant.Backends.Tests
```

The SVG backend has golden-file tests (`tests/RubberPlant.Backends.Tests/golden`).
After an intended rendering change, regenerate them with
`RUBBERPLANT_UPDATE_GOLDEN=1` and review the diff.

## Layout

```
src/RubberPlant.Core       pure pipeline: parser, checker, derivation, turtle, geometry
src/RubberPlant.Backends   output backends (SVG)
src/RubberPlant.Cli        command-line tool; all I/O lives here
tests/                     Expecto test projects
examples/                  ABOP figures written in RubberPlant
docs/                      language spec; reference material (see docs/README.md)
```

The pipeline:

```
source → Parser → AST → Checker → Model → Derivation (seeded) → derived string
       → interpretation (as rules) → drawing string → Turtle → Geometry → backend
```
