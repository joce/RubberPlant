module RubberPlant.Cli.Tests.CliTests

open System.IO
open Expecto
open RubberPlant.Cli

/// Runs the CLI in a fresh temporary directory containing `koch.rp`.
let private withTempDir (f: string -> unit) =
    let dir = Path.Combine(Path.GetTempPath(), "rubberplant-cli-" + Path.GetRandomFileName())
    Directory.CreateDirectory dir |> ignore
    File.WriteAllText(Path.Combine(dir, "koch.rp"), "settings { derivations = 1; }\naxiom: F-F-F-F;\nF -> F-F+F+F F-F-F+F;\n")

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
                  // With width-1 lines, 10.5 px is reserved on each side: 21 leaves no room, 22 does.
                  Expect.equal (run [ "render"; input; "--size"; "21"; "-o"; Path.Combine(dir, "x.svg") ]) 1 "size 21"
                  Expect.equal (run [ "render"; input; "--size"; "22"; "-o"; Path.Combine(dir, "x.svg") ]) 0 "size 22")
          }

          test "--seed range: integers a float represents exactly" {
              withTempDir (fun dir ->
                  let input = Path.Combine(dir, "koch.rp")
                  Expect.equal (run [ "check"; input; "--seed"; "9007199254740991" ]) 0 "largest exact integer"
                  Expect.equal (run [ "check"; input; "--seed"; "9007199254740993" ]) 2 "beyond 2^53")
          }

          test "--width must be non-negative" {
              withTempDir (fun dir ->
                  let input = Path.Combine(dir, "koch.rp")
                  Expect.equal (run [ "render"; input; "--width"; "-1" ]) 2 "negative"
                  Expect.equal (run [ "render"; input; "--width"; "0"; "-o"; Path.Combine(dir, "x.svg") ]) 0 "zero is fine")
          }

          test "strokes too wide for the image are an error" {
              withTempDir (fun dir ->
                  let input = Path.Combine(dir, "wide.rp")
                  File.WriteAllText(input, "settings { width = 800; }\naxiom: F;\n")
                  let out = Path.Combine(dir, "wide.svg")
                  Expect.equal (run [ "render"; input; "-o"; out ]) 1 "exit code"
                  Expect.isFalse (File.Exists out) "nothing written")
          }

          test "--tropism and --elasticity override the settings" {
              withTempDir (fun dir ->
                  let input = Path.Combine(dir, "koch.rp")
                  let plain = Path.Combine(dir, "plain.svg")
                  let bent = Path.Combine(dir, "bent.svg")
                  let gravity = Path.Combine(dir, "gravity.svg")
                  Expect.equal (run [ "render"; input; "-o"; plain ]) 0 "plain"

                  Expect.equal
                      (run [ "render"; input; "--tropism"; "1, 0,0"; "--elasticity"; "0.3"; "-o"; bent ])
                      0
                      "bent"

                  Expect.equal (run [ "render"; input; "--tropism"; "-1e-3,0,0"; "-o"; gravity ]) 0 "elasticity stays 0"
                  Expect.notEqual (File.ReadAllText bent) (File.ReadAllText plain) "tropism bends the drawing"
                  Expect.equal (File.ReadAllText gravity) (File.ReadAllText plain) "no bend without elasticity")
          }

          test "--tropism and --elasticity values" {
              withTempDir (fun dir ->
                  let input = Path.Combine(dir, "koch.rp")

                  for bad in [ "1,0"; "1,0,0,0"; "0,0,0"; "a,0,0"; "1,,0"; "1,0,Infinity" ] do
                      Expect.equal (run [ "check"; input; "--tropism"; bad ]) 2 bad

                  for bad in [ "x"; "Infinity"; "NaN" ] do
                      Expect.equal (run [ "check"; input; "--elasticity"; bad ]) 2 bad

                  Expect.equal (run [ "check"; input; "--elasticity"; "-0.5" ]) 0 "negative bends away")
          }

          test "usage errors" {
              Expect.equal (run [ "frobnicate" ]) 2 "unknown command"
              Expect.equal (run [ "render" ]) 2 "missing input"
          } ]
