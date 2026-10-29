module RubberPlant.Backends.Tests.ObjTests

open System
open System.IO
open Expecto
open RubberPlant.Geometry
open RubberPlant.Backends
open RubberPlant.Backends.Mesh
open RubberPlant.Backends.Tests.MeshFixtures

let private meshGoldenDir = Path.Combine(__SOURCE_DIRECTORY__, "golden", "mesh")

/// Set RUBBERPLANT_UPDATE_GOLDEN=1 to rewrite the golden files.
let private update = Environment.GetEnvironmentVariable "RUBBERPLANT_UPDATE_GOLDEN" = "1"

let private ok (r: Result<Obj.ObjOutput, string>) =
    match r with
    | Ok o -> o
    | Error msg -> failtestf "render failed: %s" msg

/// Compares text with golden/mesh/<file>, or rewrites it when updating.
let checkGolden (file: string) (actual: string) =
    let path = Path.Combine(meshGoldenDir, file)

    if update then
        Directory.CreateDirectory meshGoldenDir |> ignore
        File.WriteAllText(path, actual)
    elif not (File.Exists path) then
        failtestf "golden/mesh/%s is missing (set RUBBERPLANT_UPDATE_GOLDEN=1 to create it, then review it)" file
    elif actual <> File.ReadAllText(path).Replace("\r\n", "\n") then
        failtestf "%s differs from golden/mesh/%s (set RUBBERPLANT_UPDATE_GOLDEN=1 to accept)" file file

let private goldens =
    testList
        "goldens"
        [ for name, (options, geometry) in all ->
              test name {
                  let out = Obj.render options (name + ".mtl") geometry |> ok
                  checkGolden (name + ".obj") out.Obj
                  checkGolden (name + ".mtl") out.Mtl
              } ]

let private lines (s: string) =
    s.Split('\n') |> Array.filter (fun l -> l <> "")

[<Tests>]
let tests =
    testList
        "OBJ"
        [ goldens

          test "numbers: the exact value to 6 decimals, halves to even, no exponent, no -0" {
              Expect.equal (Obj.num 0.0) "0" ""
              Expect.equal (Obj.num -0.0) "0" ""
              Expect.equal (Obj.num 1.0) "1" ""
              Expect.equal (Obj.num -2.5) "-2.5" ""
              Expect.equal (Obj.num 0.0078125) "0.007812" "an exact tie rounds to even"
              Expect.equal (Obj.num 0.0234375) "0.023438" "an exact tie rounds to even"
              Expect.equal (Obj.num -0.0078125) "-0.007812" ""
              // Found in review: "0.######" rounded to 15 significant digits first.
              Expect.equal (Obj.num (Math.BitDecrement 0.0078125)) "0.007812" "just below a tie"
              Expect.equal (Obj.num (Math.BitIncrement 0.0078125)) "0.007813" "just above a tie"
              Expect.equal (Obj.num -(Math.BitDecrement 0.0078125)) "-0.007812" ""
              Expect.equal (Obj.num -(Math.BitIncrement 0.0078125)) "-0.007813" ""
              Expect.equal (Obj.num 9007199254740991.0) "9007199254740991" "a large exact integer"
              Expect.equal (Obj.num 123456789.1234567) "123456789.123457" ""
              Expect.equal (Obj.num 1e-7) "0" ""
              Expect.equal (Obj.num -4e-7) "0" "rounds to -0, written 0"
              Expect.equal (Obj.num 1e20) "100000000000000000000" "no exponent"
              Expect.equal (Obj.num (0.1 + 0.2)) "0.3" ""
          }

          test "layout: header, positions, normals, then faces per material" {
              let options, geometry = twoColors
              let out = Obj.render options "two.mtl" geometry |> ok
              let ls = lines out.Obj
              Expect.equal ls[0] "# RubberPlant" ""
              Expect.equal ls[1] "mtllib two.mtl" ""
              let kinds = ls |> Array.skip 2 |> Array.map (fun l -> l.Split(' ')[0])
              // v…, vn…, usemtl, f…, usemtl, f…: each kind in one block.
              let blocks = kinds |> Array.fold (fun acc k -> if List.tryHead acc = Some k then acc else k :: acc) [] |> List.rev
              Expect.equal blocks [ "v"; "vn"; "usemtl"; "f"; "usemtl"; "f" ] ""
              Expect.isTrue (out.Obj.EndsWith "\n") "ends with a newline"
              Expect.isFalse (out.Obj.Contains "\r") "LF only"
              Expect.isFalse (out.Obj.Contains "\n\n") "no blank lines"
          }

          test "faces use global 1-based indices, vertex and normal alike" {
              // Green triangle (3 vertices, entry 1) first, then brown cylinders
              // (48 vertices, entry 2) numbered from 4.
              let options, geometry = twoColors
              let out = Obj.render options "two.mtl" geometry |> ok
              let faces = lines out.Obj |> Array.filter (fun l -> l.StartsWith "f ")
              Expect.equal faces[0] "f 1//1 2//2 3//3" "the triangle"
              Expect.equal faces.Length (1 + 48) ""

              let indices =
                  faces
                  |> Array.collect (fun f -> f.Split(' ') |> Array.skip 1)
                  |> Array.map (fun v ->
                      match v.Split("//") with
                      | [| a; b |] when a = b -> int a
                      | _ -> failtestf "bad face vertex %s" v)

              Expect.equal (Array.min indices) 1 ""
              Expect.equal (Array.max indices) 51 ""
              Expect.equal (Array.min indices[3..]) 4 "the second group starts after the first"
          }

          test "MTL: one material per group, sRGB / 255, separated by blank lines" {
              let options, geometry = twoColors
              let out = Obj.render options "two.mtl" geometry |> ok

              Expect.equal
                  out.Mtl
                  "newmtl color1\nKd 0.180392 0.490196 0.196078\nillum 1\n\nnewmtl color2\nKd 0.427451 0.298039 0.254902\nillum 1\n"
                  ""
          }

          test "empty geometry: header only, empty MTL" {
              let out = Obj.render defaultMeshOptions "empty.mtl" [] |> ok
              Expect.equal out.Obj "# RubberPlant\nmtllib empty.mtl\n" ""
              Expect.equal out.Mtl "" ""
          }

          test "mesh errors are passed on" {
              match Obj.render { defaultMeshOptions with Sides = 2 } "x.mtl" [] with
              | Error msg -> Expect.stringContains msg "sides" ""
              | Ok _ -> failtest "expected an error"
          } ]
