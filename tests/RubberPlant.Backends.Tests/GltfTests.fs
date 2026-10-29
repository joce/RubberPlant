module RubberPlant.Backends.Tests.GltfTests

open System
open System.Buffers.Binary
open System.Text
open System.Text.Json
open System.Text.RegularExpressions
open Expecto
open RubberPlant.Geometry
open RubberPlant.Backends
open RubberPlant.Backends.Mesh
open RubberPlant.Backends.Tests.MeshFixtures

let private ok (r: Result<'a, string>) =
    match r with
    | Ok x -> x
    | Error msg -> failtestf "render failed: %s" msg

let private dataPrefix = "data:application/octet-stream;base64,"

/// The buffer embedded in a .gltf.
let private embeddedBuffer (gltf: string) =
    use doc = JsonDocument.Parse gltf
    let uri = (doc.RootElement.GetProperty "buffers").[0].GetProperty("uri").GetString()
    Expect.stringStarts uri dataPrefix "a base64 data URI"
    Convert.FromBase64String(uri.Substring dataPrefix.Length)

/// The chunks of a .glb: (type, data), after checking the header.
let private chunks (glb: byte[]) =
    let u32 at = BinaryPrimitives.ReadUInt32LittleEndian(ReadOnlySpan(glb, at, 4))
    Expect.equal (u32 0) 0x46546C67u "magic glTF"
    Expect.equal (u32 4) 2u "version 2"
    Expect.equal (int (u32 8)) glb.Length "total length"

    let rec read at acc =
        if at >= glb.Length then
            List.rev acc
        else
            let length = int (u32 at)
            Expect.equal (length % 4) 0 "chunks are 4-byte aligned"
            read (at + 8 + length) ((u32 (at + 4), glb[at + 8 .. at + 8 + length - 1]) :: acc)

    read 12 []

let private emptyDocument =
    "{\n  \"asset\": {\n    \"version\": \"2.0\",\n    \"generator\": \"RubberPlant\"\n  },\n  \"scene\": 0,\n  \"scenes\": [\n    {\n      \"nodes\": [\n        0\n      ]\n    }\n  ],\n  \"nodes\": [\n    {}\n  ]\n}\n"

let private goldens =
    testList
        "goldens"
        [ for name, (options, geometry) in all ->
              test name { ObjTests.checkGolden (name + ".gltf") (Gltf.renderText options geometry |> ok) } ]

[<Tests>]
let tests =
    testList
        "glTF"
        [ goldens

          test "empty geometry: a scene with one empty node, nothing else" {
              Expect.equal (Gltf.renderText defaultMeshOptions [] |> ok) emptyDocument ""
          }

          test "top-level keys in the specified order" {
              let options, geometry = twoColors
              use doc = JsonDocument.Parse(Gltf.renderText options geometry |> ok)
              let keys = doc.RootElement.EnumerateObject() |> Seq.map _.Name |> List.ofSeq

              Expect.equal
                  keys
                  [ "asset"; "scene"; "scenes"; "nodes"; "meshes"; "materials"; "accessors"; "bufferViews"; "buffers" ]
                  ""
          }

          test "the buffer holds the mesh as float32 and uint32, and min/max match it" {
              let options, geometry = twoColors
              let mesh = build options geometry |> ok
              let gltf = Gltf.renderText options geometry |> ok
              let data = embeddedBuffer gltf
              use doc = JsonDocument.Parse gltf
              let root = doc.RootElement
              let f32 at = BinaryPrimitives.ReadSingleLittleEndian(ReadOnlySpan(data, at, 4))
              let mutable offset = 0

              for g, group in List.indexed mesh.Groups do
                  let view (i: int) = (root.GetProperty "bufferViews").[i]
                  let accessor (i: int) = (root.GetProperty "accessors").[i]
                  Expect.equal ((view (3 * g)).GetProperty("byteOffset").GetInt32()) offset "views are contiguous"
                  Expect.equal ((accessor (3 * g)).GetProperty("count").GetInt32()) group.Positions.Length ""
                  Expect.equal ((accessor (3 * g + 2)).GetProperty("count").GetInt32()) group.Triangles.Length ""

                  let positions =
                      Array.init group.Positions.Length (fun i ->
                          [| f32 (offset + 12 * i); f32 (offset + 12 * i + 4); f32 (offset + 12 * i + 8) |])

                  for i, p in Array.indexed group.Positions do
                      Expect.equal positions[i] [| float32 p.X; float32 p.Y; float32 p.Z |] "positions as float32"

                  let bound (name: string) (pick: float32[] -> float32) =
                      let json =
                          (accessor (3 * g)).GetProperty(name).EnumerateArray() |> Seq.map _.GetSingle() |> Array.ofSeq

                      Expect.equal json [| for c in 0..2 -> pick (positions |> Array.map (fun p -> p[c])) |] name

                  bound "min" Array.min
                  bound "max" Array.max
                  offset <- offset + 24 * group.Positions.Length

                  for i, index in Array.indexed group.Triangles do
                      Expect.equal (BinaryPrimitives.ReadUInt32LittleEndian(ReadOnlySpan(data, offset + 4 * i, 4))) (uint32 index) "indices"

                  offset <- offset + 4 * group.Triangles.Length

              Expect.equal offset data.Length "the whole buffer"
              Expect.equal ((root.GetProperty "buffers").[0].GetProperty("byteLength").GetInt32()) data.Length ""
          }

          test "materials: linear palette colors, double-sided" {
              let options, geometry = twoColors
              use doc = JsonDocument.Parse(Gltf.renderText options geometry |> ok)

              for i, entry in List.indexed [ 1; 2 ] do
                  let m = (doc.RootElement.GetProperty "materials").[i]
                  Expect.equal (m.GetProperty("name").GetString()) $"color%d{entry}" ""
                  Expect.isTrue (m.GetProperty("doubleSided").GetBoolean()) ""
                  let pbr = m.GetProperty "pbrMetallicRoughness"
                  let r, g, b = Palette.linear[entry]
                  let factor = pbr.GetProperty("baseColorFactor").EnumerateArray() |> Seq.map _.GetDouble() |> List.ofSeq
                  Expect.equal factor [ r; g; b; 1.0 ] "exact doubles"
                  Expect.equal (pbr.GetProperty("metallicFactor").GetDouble()) 0.0 ""
                  Expect.equal (pbr.GetProperty("roughnessFactor").GetDouble()) 1.0 ""
          }

          test "numbers: no -0, and base64 isn't escaped" {
              // A cylinder along Y has vertices with x = -0 after rounding to float32.
              let gltf = Gltf.renderText (fst oneCylinder) (snd oneCylinder) |> ok
              Expect.isFalse (Regex.IsMatch(gltf, @"[\s\[]-0[,\s\]]")) "no -0 in the JSON"
              Expect.isFalse (gltf.Contains @"\u") "no escapes"
              Expect.isTrue (gltf.EndsWith "}\n") "ends with a newline"
              Expect.isFalse (gltf.Contains "\r") "LF only"
          }

          test ".glb: the same document without the uri, and the buffer as the BIN chunk" {
              for _, (options, geometry) in all do
                  let gltf = Gltf.renderText options geometry |> ok
                  let glb = Gltf.renderBinary options geometry |> ok

                  match chunks glb with
                  | [ (0x4E4F534Au, json); (0x004E4942u, bin) ] ->
                      let expected = Regex.Replace(gltf, ",\n *\"uri\": \"[^\"]*\"", "")
                      Expect.equal (Encoding.UTF8.GetString(json).TrimEnd(' ')) expected "JSON chunk, padded with spaces"
                      Expect.equal bin (embeddedBuffer gltf) "BIN chunk"
                  | cs -> failtestf "expected JSON and BIN chunks, got %A" (cs |> List.map fst)
          }

          test ".glb of empty geometry: a JSON chunk only" {
              match Gltf.renderBinary defaultMeshOptions [] |> ok |> chunks with
              | [ (0x4E4F534Au, json) ] -> Expect.equal (Encoding.UTF8.GetString(json).TrimEnd(' ')) emptyDocument ""
              | cs -> failtestf "expected one JSON chunk, got %A" (cs |> List.map fst)
          }

          test "mesh errors are passed on" {
              let bad = { defaultMeshOptions with Sides = 2 }
              Expect.isError (Gltf.renderText bad []) ""
              Expect.isError (Gltf.renderBinary bad []) ""
          } ]
