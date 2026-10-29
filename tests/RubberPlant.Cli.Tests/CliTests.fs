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
          }

          test "render writes an OBJ and its MTL" {
              withTempDir (fun dir ->
                  let out = Path.Combine(dir, "tree.obj")
                  Expect.equal (run [ "render"; Path.Combine(dir, "koch.rp"); "-o"; out ]) 0 "exit code"
                  let obj = File.ReadAllText out
                  Expect.stringStarts obj "# RubberPlant\nmtllib tree.mtl\n" "refers to its MTL by file name"
                  Expect.stringContains (File.ReadAllText(Path.Combine(dir, "tree.mtl"))) "newmtl color0" "")
          }

          test "the format comes from the extension, case-insensitive" {
              withTempDir (fun dir ->
                  let out = Path.Combine(dir, "TREE.OBJ")
                  Expect.equal (run [ "render"; Path.Combine(dir, "koch.rp"); "-o"; out ]) 0 "exit code"
                  Expect.stringStarts (File.ReadAllText out) "# RubberPlant" "an OBJ"
                  Expect.isTrue (File.Exists(Path.Combine(dir, "TREE.mtl"))) "MTL next to it")
          }

          test "--format without -o names the output after the input" {
              withTempDir (fun dir ->
                  let input = Path.Combine(dir, "koch.rp")
                  Expect.equal (run [ "render"; input; "--format"; "obj" ]) 0 "obj"
                  Expect.isTrue (File.Exists(Path.Combine(dir, "koch.obj"))) "koch.obj"
                  Expect.isTrue (File.Exists(Path.Combine(dir, "koch.mtl"))) "koch.mtl"
                  Expect.equal (run [ "render"; input ]) 0 "svg by default"
                  Expect.isTrue (File.Exists(Path.Combine(dir, "koch.svg"))) "koch.svg")
          }

          test "format errors are usage errors, found before any work" {
              withTempDir (fun dir ->
                  let input = Path.Combine(dir, "koch.rp")
                  let out ext = Path.Combine(dir, "out." + ext)
                  Expect.equal (run [ "render"; input; "-o"; out "png" ]) 2 "unknown extension"
                  Expect.equal (run [ "render"; input; "-o"; Path.Combine(dir, "out") ]) 2 "no extension"
                  Expect.equal (run [ "render"; input; "-o"; out "svg"; "--format"; "obj" ]) 2 "-o and --format disagree"
                  Expect.equal (run [ "render"; input; "-o"; out "obj"; "--format"; "obj" ]) 0 "they agree"
                  Expect.equal (run [ "render"; input; "--format"; "png" ]) 2 "unknown format"
                  Expect.equal (run [ "render"; input; "-o"; out "gltf" ]) 2 "glTF isn't available yet"
                  Expect.isFalse (File.Exists(out "png")) "nothing written")
          }

          test "options that don't apply to the format are usage errors" {
              withTempDir (fun dir ->
                  let input = Path.Combine(dir, "koch.rp")
                  let out ext = Path.Combine(dir, "out." + ext)
                  Expect.equal (run [ "render"; input; "-o"; out "obj"; "--size"; "100" ]) 2 "--size with OBJ"
                  Expect.equal (run [ "render"; input; "-o"; out "svg"; "--sides"; "6" ]) 2 "--sides with SVG"
                  Expect.equal (run [ "render"; input; "--width-scale"; "auto" ]) 2 "--width-scale with SVG (default)")
          }

          test "options that don't apply to the command are usage errors" {
              withTempDir (fun dir ->
                  let input = Path.Combine(dir, "koch.rp")

                  for opt, value in [ "-o", "x.svg"; "--format", "obj"; "--size", "100"; "--sides", "6"; "--width-scale", "1" ] do
                      Expect.equal (run [ "derive"; input; opt; value ]) 2 $"derive %s{opt}"
                      Expect.equal (run [ "check"; input; opt; value ]) 2 $"check %s{opt}"

                  Expect.equal (run [ "render"; input; "--drawing" ]) 2 "render --drawing"
                  Expect.equal (run [ "check"; input; "--drawing" ]) 2 "check --drawing"
                  Expect.equal (run [ "derive"; input; "--drawing" ]) 0 "derive --drawing")
          }

          test "--sides and --width-scale values" {
              withTempDir (fun dir ->
                  let input = Path.Combine(dir, "koch.rp")
                  let out = Path.Combine(dir, "out.obj")

                  for bad in [ "2"; "0"; "-3"; "3.5"; "x" ] do
                      Expect.equal (run [ "render"; input; "-o"; out; "--sides"; bad ]) 2 $"--sides %s{bad}"

                  for bad in [ "0"; "-1"; "Infinity"; "NaN"; "x" ] do
                      Expect.equal (run [ "render"; input; "-o"; out; "--width-scale"; bad ]) 2 $"--width-scale %s{bad}"

                  Expect.equal (run [ "render"; input; "-o"; out; "--sides"; "3"; "--width-scale"; "auto" ]) 0 "auto"
                  let triangles = File.ReadAllLines out |> Array.filter (fun l -> l.StartsWith "f ") |> Array.length
                  Expect.equal triangles (32 * 2 * 3) "32 segments, 6 triangles each"
                  Expect.equal (run [ "render"; input; "-o"; out; "--width-scale"; "0.01" ]) 0 "explicit")
          }

          test "an existing OBJ/MTL pair survives a failed render" {
              // Found in review: the OBJ used to be replaced before the MTL failed.
              withTempDir (fun dir ->
                  let input = Path.Combine(dir, "tree.rp")
                  let obj, mtl = Path.Combine(dir, "tree.obj"), Path.Combine(dir, "tree.mtl")
                  File.WriteAllText(input, "axiom: F;")
                  Expect.equal (run [ "render"; input; "-o"; obj ]) 0 "first render"
                  let oldObj, oldMtl = File.ReadAllText obj, File.ReadAllText mtl
                  File.SetAttributes(mtl, FileAttributes.ReadOnly)
                  File.WriteAllText(input, "axiom: '(1) F;")
                  Expect.equal (run [ "render"; input; "-o"; obj ]) 1 "the MTL can't be written"
                  Expect.equal (File.ReadAllText obj) oldObj "OBJ unchanged"
                  Expect.equal (File.ReadAllText mtl) oldMtl "MTL unchanged")
          }

          test "no OBJ is left without its MTL" {
              withTempDir (fun dir ->
                  let obj = Path.Combine(dir, "tree.obj")
                  let mtlDir = Directory.CreateDirectory(Path.Combine(dir, "tree.mtl"))
                  Expect.equal (run [ "render"; Path.Combine(dir, "koch.rp"); "-o"; obj ]) 1 "a directory is in the way"
                  Expect.isFalse (File.Exists obj) "no new OBJ"
                  mtlDir.Delete())
          }

          test "a mesh that can't be built is an error" {
              withTempDir (fun dir ->
                  let input = Path.Combine(dir, "wide.rp")
                  File.WriteAllText(input, "settings { width = 800; }\naxiom: F;\n")
                  let out = Path.Combine(dir, "wide.obj")
                  Expect.equal (run [ "render"; input; "-o"; out ]) 1 "auto width scale impossible"
                  Expect.isFalse (File.Exists out) "nothing written"
                  Expect.equal (run [ "render"; input; "-o"; out; "--width-scale"; "0.001" ]) 0 "explicit is fine")
          } ]
