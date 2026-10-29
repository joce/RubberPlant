/// The examples (examples/*.rp) and their run parameters, shared by the golden
/// tests of every backend.
module RubberPlant.Backends.Tests.Examples

open System
open System.IO
open Expecto
open RubberPlant

let examplesDir = Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "examples")
let goldenDir = Path.Combine(__SOURCE_DIRECTORY__, "golden")

/// Set RUBBERPLANT_UPDATE_GOLDEN=1 to rewrite the golden files.
let update = Environment.GetEnvironmentVariable "RUBBERPLANT_UPDATE_GOLDEN" = "1"

/// `golden/seeds.txt`: lines of `<example> <seed>`; examples not listed use seed 0.
/// scripts/check-goldens.sh reads the same file.
let private seeds =
    File.ReadAllLines(Path.Combine(goldenDir, "seeds.txt"))
    |> Array.filter (fun l -> l.Trim() <> "" && not (l.StartsWith "#"))
    |> Array.map (fun l ->
        match l.Split([| ' ' |], StringSplitOptions.RemoveEmptyEntries) with
        | [| name; seed |] -> name, int64 seed
        | _ -> failwithf "bad line in seeds.txt: %s" l)
    |> Map.ofArray

let seedOf name = seeds |> Map.tryFind name |> Option.defaultValue 0L

let names =
    Directory.GetFiles(examplesDir, "*.rp")
    |> Array.map Path.GetFileNameWithoutExtension
    |> Array.sort
    |> Array.toList

/// The geometry of an example, with its seed (a run parameter, §4.7).
let geometry (name: string) =
    let source = File.ReadAllText(Path.Combine(examplesDir, name + ".rp"))

    let withSeed (m: Model.Model) =
        { m with Settings = { m.Settings with Seed = seedOf name } }

    match Pipeline.compile source |> Result.bind (fst >> withSeed >> Pipeline.run) with
    | Ok output -> output.Geometry
    | Error ds -> failtestf "%s:\n%s" name (ds |> List.map (Diagnostic.format name) |> String.concat "\n")
