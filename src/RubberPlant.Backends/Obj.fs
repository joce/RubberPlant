/// OBJ backend (backends.md §5): a pure function from geometry to the text of an
/// .obj file and its companion .mtl file, which colors the faces. The mesh comes
/// from Mesh.build, so it is exactly the glTF backend's mesh.
module RubberPlant.Backends.Obj

open System.Globalization
open System.Text
open RubberPlant.Geometry
open RubberPlant.Backends.Mesh

type ObjOutput = { Obj: string; Mtl: string }

/// The exact binary value rounded to 6 decimals, halves to even, without
/// exponent, trailing zeros or "-0" (§5). .NET's "F6" is exact (custom formats
/// such as "0.######" round to 15 significant digits first).
let num (v: float) =
    let s = v.ToString("F6", CultureInfo.InvariantCulture)
    let s = if s.Contains '.' then s.TrimEnd('0').TrimEnd('.') else s
    if s = "-0" then "0" else s

let private vec (prefix: string) (v: Vec3) = $"%s{prefix} %s{num v.X} %s{num v.Y} %s{num v.Z}"

/// The material name of a palette entry.
let materialName (entry: int) = $"color%d{entry}"

let private obj (mtlFileName: string) (mesh: Mesh) =
    let sb = StringBuilder()
    let line (s: string) = sb.Append(s).Append('\n') |> ignore

    line "# RubberPlant"
    line $"mtllib %s{mtlFileName}"

    for g in mesh.Groups do
        for p in g.Positions do
            line (vec "v" p)

    for g in mesh.Groups do
        for n in g.Normals do
            line (vec "vn" n)

    // Face indices are 1-based and global: each group's vertices follow the
    // previous groups'. A vertex and its normal share an index.
    let mutable offset = 1

    for g in mesh.Groups do
        line $"usemtl %s{materialName g.Color}"

        for t in 0 .. g.Triangles.Length / 3 - 1 do
            let a, b, c =
                g.Triangles[3 * t] + offset, g.Triangles[3 * t + 1] + offset, g.Triangles[3 * t + 2] + offset

            line $"f %d{a}//%d{a} %d{b}//%d{b} %d{c}//%d{c}"

        offset <- offset + g.Positions.Length

    sb.ToString()

let private mtl (mesh: Mesh) =
    mesh.Groups
    |> List.map (fun g ->
        let r, gr, b = Palette.rgb g.Color
        let k (c: byte) = num (float c / 255.0)
        $"newmtl %s{materialName g.Color}\nKd %s{k r} %s{k gr} %s{k b}\nillum 1\n")
    |> String.concat "\n"

/// Renders geometry as OBJ and MTL text. `mtlFileName` is the bare file name the
/// OBJ refers to (`mtllib`); the function does no I/O. Fails when the mesh can't
/// be built (§4).
let render (options: MeshOptions) (mtlFileName: string) (geometry: Geometry) : Result<ObjOutput, string> =
    build options geometry
    |> Result.map (fun mesh -> { Obj = obj mtlFileName mesh; Mtl = mtl mesh })
