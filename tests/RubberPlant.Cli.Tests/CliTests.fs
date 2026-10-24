module RubberPlant.Cli.Tests.CliTests

open System.IO
open Expecto
open RubberPlant.Cli

/// Runs the CLI in a fresh temporary directory containing `koch.rp`.
let private withTempDir (f: string -> unit) =
    let dir = Path.Combine(Path.GetTempPath(), "rubberplant-cli-" + Path.GetRandomFileName())
    Directory.CreateDirectory dir |> ignore
    File.WriteAllText(Path.Combine(dir, "koch.rp"), "#derivations 1\n#axiom F-F-F-F\nF -> F-F+F+F F-F-F+F;\n")

    try
        f dir
    finally
        // Clear the read-only bit set by some tests before deleting.
        for file in Directory.GetFiles dir do
            File.SetAttributes(file, FileAttributes.Normal)

        Directory.Delete(dir, true)

let private run (args: string list) = Program.main (Array.ofList args)

[<Tests>]
let tests =
    testList
        "CLI"
        [ test "render writes an SVG" {
              withTempDir (fun dir ->
                  let out = Path.Combine(dir, "koch.svg")
                  Expect.equal (run [ "render"; Path.Combine(dir, "koch.rp"); "-o"; out ]) 0 "exit code"
                  Expect.stringContains (File.ReadAllText out) "<svg" "")
          }

          test "output to a missing directory is an error, not a crash" {
              withTempDir (fun dir ->
                  let out = Path.Combine(dir, "missing", "koch.svg")
                  Expect.equal (run [ "render"; Path.Combine(dir, "koch.rp"); "-o"; out ]) 1 "exit code")
          }

          test "output to a read-only file is an error, not a crash" {
              withTempDir (fun dir ->
                  let out = Path.Combine(dir, "koch.svg")
                  File.WriteAllText(out, "")
                  File.SetAttributes(out, FileAttributes.ReadOnly)
                  Expect.equal (run [ "render"; Path.Combine(dir, "koch.rp"); "-o"; out ]) 1 "exit code")
          }

          test "missing input file" {
              withTempDir (fun dir -> Expect.equal (run [ "check"; Path.Combine(dir, "nope.rp") ]) 1 "exit code")
          }

          test "--size must exceed twice the margin" {
              withTempDir (fun dir ->
                  let input = Path.Combine(dir, "koch.rp")
                  Expect.equal (run [ "render"; input; "--size"; "1" ]) 2 "size 1"
                  Expect.equal (run [ "render"; input; "--size"; "20" ]) 2 "size 20"
                  Expect.equal (run [ "render"; input; "--size"; "21"; "-o"; Path.Combine(dir, "x.svg") ]) 0 "size 21")
          }

          test "--seed range matches #seed" {
              withTempDir (fun dir ->
                  let input = Path.Combine(dir, "koch.rp")
                  Expect.equal (run [ "check"; input; "--seed"; "9007199254740991" ]) 0 "largest exact integer"
                  Expect.equal (run [ "check"; input; "--seed"; "9007199254740993" ]) 2 "beyond 2^53")
          }

          test "usage errors" {
              Expect.equal (run [ "frobnicate" ]) 2 "unknown command"
              Expect.equal (run [ "render" ]) 2 "missing input"
          } ]
