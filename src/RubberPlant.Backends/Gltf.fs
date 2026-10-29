/// glTF backend (backends.md §6): pure functions from geometry to a .gltf (JSON
/// text with the buffer embedded as a base64 data URI) or a .glb (binary). Both
/// come from the same document, built on Mesh.build, so they hold exactly the
/// OBJ backend's mesh.
module RubberPlant.Backends.Gltf

open System
open System.Buffers.Binary
open System.IO
open System.Text
open System.Text.Encodings.Web
open System.Text.Json
open RubberPlant.Geometry
open RubberPlant.Backends.Mesh

let private jsonOptions =
    JsonWriterOptions(
        Indented = true,
        IndentSize = 2,
        NewLine = "\n",
        // The default encoder escapes '+' (in base64) as \u002B; the strings
        // written here are plain ASCII, so the relaxed encoder is safe.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    )

let private floatComponent = 5126
let private uintComponent = 5125
let private arrayBuffer = 34962
let private elementArrayBuffer = 34963

/// A float32 for the JSON, with -0 written as 0.
let private f32 (x: float32) = if x = 0.0f then 0.0f else x

/// The buffer: for each group, its positions, normals and indices, little-endian.
let private buffer (mesh: Mesh) =
    let size =
        mesh.Groups
        |> List.sumBy (fun g -> 24 * g.Positions.Length + 4 * g.Triangles.Length)

    let bytes = Array.zeroCreate<byte> size
    let mutable at = 0

    let vec3 (v: Vec3) =
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(at), float32 v.X)
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(at + 4), float32 v.Y)
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(at + 8), float32 v.Z)
        at <- at + 12

    for g in mesh.Groups do
        Array.iter vec3 g.Positions
        Array.iter vec3 g.Normals

        for i in g.Triangles do
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), uint32 i)
            at <- at + 4

    bytes

/// The mesh, if every position and normal fits in float32 (§6): glTF forbids
/// infinite accessor data, and double coordinates can exceed float32's range.
let private meshFor (options: MeshOptions) (geometry: Geometry) : Result<Mesh, string> =
    let fits (v: Vec3) =
        Single.IsFinite(float32 v.X) && Single.IsFinite(float32 v.Y) && Single.IsFinite(float32 v.Z)

    build options geometry
    |> Result.bind (fun mesh ->
        if mesh.Groups |> List.forall (fun g -> Array.forall fits g.Positions && Array.forall fits g.Normals) then
            Ok mesh
        else
            Error
                "the mesh has coordinates too large for glTF (beyond float32's ±3.4e38); use smaller steps, or OBJ")

/// The JSON document (§6), ending with a newline. `uri` is the buffer's data URI
/// (.gltf), or None (.glb, where the buffer is the BIN chunk).
let private document (mesh: Mesh) (bufferLength: int) (uri: string option) : byte[] =
    use stream = new MemoryStream()

    (use w = new Utf8JsonWriter(stream, jsonOptions)

     let intArray (name: string) (values: int list) =
         w.WriteStartArray name
         values |> List.iter w.WriteNumberValue
         w.WriteEndArray()

     w.WriteStartObject()
     w.WriteStartObject "asset"
     w.WriteString("version", "2.0")
     w.WriteString("generator", "RubberPlant")
     w.WriteEndObject()
     w.WriteNumber("scene", 0)
     w.WriteStartArray "scenes"
     w.WriteStartObject()
     intArray "nodes" [ 0 ]
     w.WriteEndObject()
     w.WriteEndArray()
     w.WriteStartArray "nodes"
     w.WriteStartObject()

     if not mesh.Groups.IsEmpty then
         w.WriteNumber("mesh", 0)

     w.WriteEndObject()
     w.WriteEndArray()

     if not mesh.Groups.IsEmpty then
         let groups = List.indexed mesh.Groups

         w.WriteStartArray "meshes"
         w.WriteStartObject()
         w.WriteStartArray "primitives"

         for g, _ in groups do
             w.WriteStartObject()
             w.WriteStartObject "attributes"
             w.WriteNumber("POSITION", 3 * g)
             w.WriteNumber("NORMAL", 3 * g + 1)
             w.WriteEndObject()
             w.WriteNumber("indices", 3 * g + 2)
             w.WriteNumber("material", g)
             w.WriteNumber("mode", 4)
             w.WriteEndObject()

         w.WriteEndArray()
         w.WriteEndObject()
         w.WriteEndArray()

         w.WriteStartArray "materials"

         for _, group in groups do
             let r, gr, b = Palette.linear[group.Color]
             w.WriteStartObject()
             w.WriteString("name", Obj.materialName group.Color)
             w.WriteStartObject "pbrMetallicRoughness"
             w.WriteStartArray "baseColorFactor"
             w.WriteNumberValue r
             w.WriteNumberValue gr
             w.WriteNumberValue b
             w.WriteNumberValue 1
             w.WriteEndArray()
             w.WriteNumber("metallicFactor", 0)
             w.WriteNumber("roughnessFactor", 1)
             w.WriteEndObject()
             w.WriteBoolean("doubleSided", true)
             w.WriteEndObject()

         w.WriteEndArray()

         w.WriteStartArray "accessors"

         for g, group in groups do
             let accessor (view: int) (componentType: int) (count: int) (kind: string) =
                 w.WriteStartObject()
                 w.WriteNumber("bufferView", view)
                 w.WriteNumber("componentType", componentType)
                 w.WriteNumber("count", count)
                 w.WriteString("type", kind)

             // min and max of the float32 positions, so they match the data exactly.
             let ps = group.Positions |> Array.map (fun p -> [| float32 p.X; float32 p.Y; float32 p.Z |])
             accessor (3 * g) floatComponent group.Positions.Length "VEC3"
             w.WriteStartArray "min"

             for c in 0..2 do
                 w.WriteNumberValue(f32 (ps |> Array.map (fun p -> p[c]) |> Array.min))

             w.WriteEndArray()
             w.WriteStartArray "max"

             for c in 0..2 do
                 w.WriteNumberValue(f32 (ps |> Array.map (fun p -> p[c]) |> Array.max))

             w.WriteEndArray()
             w.WriteEndObject()
             accessor (3 * g + 1) floatComponent group.Normals.Length "VEC3"
             w.WriteEndObject()
             accessor (3 * g + 2) uintComponent group.Triangles.Length "SCALAR"
             w.WriteEndObject()

         w.WriteEndArray()

         w.WriteStartArray "bufferViews"
         let mutable offset = 0

         for _, group in groups do
             for length, target in
                 [ 12 * group.Positions.Length, arrayBuffer
                   12 * group.Normals.Length, arrayBuffer
                   4 * group.Triangles.Length, elementArrayBuffer ] do
                 w.WriteStartObject()
                 w.WriteNumber("buffer", 0)
                 w.WriteNumber("byteOffset", offset)
                 w.WriteNumber("byteLength", length)
                 w.WriteNumber("target", target)
                 w.WriteEndObject()
                 offset <- offset + length

         w.WriteEndArray()

         w.WriteStartArray "buffers"
         w.WriteStartObject()
         w.WriteNumber("byteLength", bufferLength)
         uri |> Option.iter (fun u -> w.WriteString("uri", u))
         w.WriteEndObject()
         w.WriteEndArray()

     w.WriteEndObject())

    stream.WriteByte(byte '\n')
    stream.ToArray()

/// Renders geometry as a .gltf: JSON text with the buffer embedded as a base64
/// data URI. Fails when the mesh can't be built (§4) or doesn't fit in float32 (§6).
let renderText (options: MeshOptions) (geometry: Geometry) : Result<string, string> =
    meshFor options geometry
    |> Result.map (fun mesh ->
        let data = buffer mesh
        let uri = "data:application/octet-stream;base64," + Convert.ToBase64String data
        Encoding.UTF8.GetString(document mesh data.Length (Some uri)))

/// Pads to a multiple of 4 bytes.
let private padded (pad: byte) (bytes: byte[]) =
    let extra = (4 - bytes.Length % 4) % 4
    Array.append bytes (Array.create extra pad)

/// Renders geometry as a .glb: header, JSON chunk, BIN chunk (§6). Fails when the
/// mesh can't be built (§4) or doesn't fit in float32 (§6).
let renderBinary (options: MeshOptions) (geometry: Geometry) : Result<byte[], string> =
    meshFor options geometry
    |> Result.map (fun mesh ->
        let data = buffer mesh
        let json = padded (byte ' ') (document mesh data.Length None)
        let bin = padded 0uy data

        let chunks =
            [ yield 0x4E4F534Au, json // "JSON"
              if not mesh.Groups.IsEmpty then
                  yield 0x004E4942u, bin ] // "BIN\0"

        let total = 12 + (chunks |> List.sumBy (fun (_, c) -> 8 + c.Length))
        let out = Array.zeroCreate<byte> total
        BinaryPrimitives.WriteUInt32LittleEndian(out.AsSpan(0), 0x46546C67u) // "glTF"
        BinaryPrimitives.WriteUInt32LittleEndian(out.AsSpan(4), 2u)
        BinaryPrimitives.WriteUInt32LittleEndian(out.AsSpan(8), uint32 total)
        let mutable at = 12

        for kind, chunk in chunks do
            BinaryPrimitives.WriteUInt32LittleEndian(out.AsSpan(at), uint32 chunk.Length)
            BinaryPrimitives.WriteUInt32LittleEndian(out.AsSpan(at + 4), kind)
            chunk.CopyTo(out, at + 8)
            at <- at + 8 + chunk.Length

        out)
