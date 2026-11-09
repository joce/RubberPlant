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

          test "render writes a .gltf and a .glb" {
              withTempDir (fun dir ->
                  let input = Path.Combine(dir, "koch.rp")
                  let gltf, glb = Path.Combine(dir, "tree.gltf"), Path.Combine(dir, "tree.glb")
                  Expect.equal (run [ "render"; input; "-o"; gltf ]) 0 ".gltf"
                  Expect.stringStarts (File.ReadAllText gltf) "{\n  \"asset\"" "JSON"
                  Expect.equal (run [ "render"; input; "-o"; glb; "--sides"; "6"; "--width-scale"; "0.01" ]) 0 ".glb"
                  Expect.equal (File.ReadAllBytes(glb)[0..3]) "glTF"B "binary glTF"
                  Expect.isFalse (File.Exists(Path.Combine(dir, "tree.mtl"))) "no MTL"
                  Expect.equal (run [ "render"; input; "--format"; "glb" ]) 0 "--format glb"
                  Expect.isTrue (File.Exists(Path.Combine(dir, "koch.glb"))) "koch.glb"
                  Expect.equal (run [ "render"; input; "-o"; glb; "--size"; "100" ]) 2 "--size with glTF")
          }

          test "glTF of coordinates beyond float32 is an error, and keeps existing output" {
              withTempDir (fun dir ->
                  let input = Path.Combine(dir, "huge.rp")
                  File.WriteAllText(input, "axiom: F(1e39);")

                  for ext in [ "gltf"; "glb" ] do
                      let out = Path.Combine(dir, "huge." + ext)
                      File.WriteAllText(out, "previous")
                      Expect.equal (run [ "render"; input; "-o"; out ]) 1 ext
                      Expect.equal (File.ReadAllText out) "previous" "existing output kept"

                  Expect.equal (run [ "render"; input; "-o"; Path.Combine(dir, "huge.obj") ]) 0 "OBJ is fine")
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

/// Runs `f` with stdout and stderr captured; returns its result and both outputs' lines.
let private captured (f: unit -> 'a) =
    let out, err = new StringWriter(), new StringWriter()
    let oldOut, oldErr = System.Console.Out, System.Console.Error
    System.Console.SetOut out
    System.Console.SetError err

    try
        let r = f ()
        let lines (w: StringWriter) = w.ToString().Split([| "\r\n"; "\n" |], System.StringSplitOptions.RemoveEmptyEntries)
        r, lines out, lines err
    finally
        System.Console.SetOut oldOut
        System.Console.SetError oldErr

let private batch (lines: string list) =
    captured (fun () -> Program.batch (new StringReader(String.concat "\n" lines)))

// Sequenced: these swap the console's writers, which the parallel tests also write to.
[<Tests>]
let batchTests =
    testSequenced
    <| testList
        "CLI batch"
        [ test "runs each line as a command and ends each with its exit code" {
              withTempDir (fun dir ->
                  // A space in the path: quoted.
                  let sub = Directory.CreateDirectory(Path.Combine(dir, "a b")).FullName
                  let input = Path.Combine(sub, "koch.rp")
                  File.Copy(Path.Combine(dir, "koch.rp"), input)
                  let svg = Path.Combine(sub, "koch.svg")
                  let missing = Path.Combine(sub, "missing.rp")

                  let code, out, err =
                      batch
                          [ $"render \"%s{input}\" -o \"%s{svg}\""
                            $"check \"%s{missing}\""
                            $"derive\t\"%s{input}\"  -n 0"
                            "render --bogus"
                            "check \"unclosed.rp" ]

                  Expect.equal code 0 "batch's own exit code"
                  Expect.equal out [| $"wrote %s{svg}"; "exit 0"; "exit 1"; "F-F-F-F"; "exit 0"; "exit 2"; "exit 2" |] "stdout"
                  Expect.isTrue (File.Exists svg) "the SVG is written"
                  Expect.equal err.Length 3 "one error each for the missing file, the bad option and the quote"
                  Expect.equal err[1] "rubberplant: --bogus expects a value" "usage error, without the usage text"
                  Expect.equal err[2] "rubberplant: unclosed double quote" "")
          }

          test "splitting a line into arguments" {
              let split line =
                  match Program.splitLine line with
                  | Ok args -> args
                  | Error msg -> failwith msg

              Expect.equal (split "render  tree.rp\t-o out.svg ") [ "render"; "tree.rp"; "-o"; "out.svg" ] "spaces and tabs"
              Expect.equal (split @"check ""C:\my trees\a.rp""") [ "check"; @"C:\my trees\a.rp" ] "quotes; backslashes as typed"
              Expect.equal (split @"render a.rp -o """"") [ "render"; "a.rp"; "-o"; "" ] "empty argument"
              Expect.equal (split @"render my"" ""tree.rp") [ "render"; "my tree.rp" ] "quotes inside an argument"
              Expect.equal (split "   ") [] "blank line"
              Expect.isError (Program.splitLine @"check ""a.rp") "unclosed quote"
          }

          test "a blank line is a usage error" {
              let code, out, err = batch [ "   " ]
              Expect.equal (code, out) (0, [| "exit 2" |]) "usage error"
              Expect.equal err[0] "rubberplant: missing command" ""
          }

          test "a batch can't run inside a batch" {
              let code, out, err = batch [ "batch" ]
              Expect.equal (code, out) (0, [| "exit 2" |]) "usage error"
              Expect.stringContains err[0] "can't run inside a batch" ""
          }

          test "batch takes no arguments" {
              let code, _, err = captured (fun () -> Program.main [| "batch"; "x" |])
              Expect.equal code 2 "usage error"
              Expect.stringContains err[0] "batch takes no arguments" ""
          } ]

// Native AOT: F# printf-style formatting of a value type (`%d`, `%g`, `%c`, `%x`, …) builds its
// formatter by reflection, and the native executable may lack the code for it ("missing native
// code" at run time). In src, only `%s` is allowed; format numbers with `string` first.
[<Tests>]
let aotFormatTests =
    test "src uses printf-style specifiers for strings only" {
        let src = Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "src")
        let specifier = System.Text.RegularExpressions.Regex @"%[-+0#]*[0-9]*(\.[0-9]+)?[a-rt-zA-Z]"

        let files = Directory.GetFiles(src, "*.fs", SearchOption.AllDirectories)

        let offenders =
            [ for file in files do
                  for i, line in Array.indexed (File.ReadAllLines file) do
                      if specifier.IsMatch line && not (line.TrimStart().StartsWith "//") then
                          $"%s{file}(%d{i + 1}): %s{line.Trim()}" ]

        Expect.isNonEmpty files "src found"
        Expect.isEmpty offenders "use %s{string x} instead"
    }
