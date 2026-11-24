/// Stats JSON digests for every example (core-hardening.md §5): verifies
/// cross-platform determinism across Linux, Windows, and Windows Native AOT.
module RubberPlant.Backends.Tests.StatsDigestTests

open System
open System.IO
open System.Security.Cryptography
open System.Text
open Expecto
open RubberPlant

let private path = Path.Combine(Examples.goldenDir, "stats-digests.txt")

let private header =
    [ "# <example> <sha256 of compact `rubberplant stats` JSON>"
      "# Default options and the seeds of seeds.txt (docs/core-hardening.md §5). scripts/check-goldens.sh"
      "# checks the CLI against them. Regenerate with RUBBERPLANT_UPDATE_GOLDEN=1 and review the diff." ]

let private sha (bytes: byte[]) =
    Convert.ToHexString(SHA256.HashData bytes).ToLowerInvariant()

let private utf8 (s: string) = UTF8Encoding(false).GetBytes s

let private digestLine (name: string) =
    let output = Examples.run name

    let stats =
        match Stats.analyze output.Derived output.Drawing output.DerivationTrace output.Geometry output.TurtleTrace with
        | Ok s -> s
        | Error ds -> failtestf "%s:\n%s" name (ds |> List.map (Diagnostic.format name) |> String.concat "\n")

    let json = Stats.toJson false stats
    $"%s{name} %s{sha (utf8 json)}"

[<Tests>]
let tests =
    test "stats digests of every example" {
        let actual = Examples.names |> List.map digestLine

        if Examples.update then
            File.WriteAllText(path, String.concat "\n" (header @ actual) + "\n")
        elif not (File.Exists path) then
            failtest "golden/stats-digests.txt is missing (set RUBBERPLANT_UPDATE_GOLDEN=1 to create it)"
        else
            let expected =
                File.ReadAllLines path |> Array.filter (fun l -> l.Trim() <> "" && not (l.StartsWith "#")) |> List.ofArray

            let byName (lines: string list) =
                lines |> List.map (fun l -> l.Split(' ')[0], l) |> Map.ofList

            let e, a = byName expected, byName actual

            let problems =
                [ for name in Set.union (set e.Keys) (set a.Keys) do
                      match e.TryFind name, a.TryFind name with
                      | Some x, Some y when x = y -> ()
                      | Some _, Some y -> yield $"differs: %s{y}"
                      | None, Some _ -> yield $"missing from stats-digests.txt: %s{name}"
                      | Some _, None -> yield $"no example for: %s{name}"
                      | None, None -> () ]

            if not problems.IsEmpty then
                failtestf
                    "%s\n(set RUBBERPLANT_UPDATE_GOLDEN=1 to accept)"
                    (String.concat "\n" problems)
    }
