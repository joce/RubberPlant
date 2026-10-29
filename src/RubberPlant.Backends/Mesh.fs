/// Mesh construction (backends.md §4): turns geometry into triangles once, so
/// every mesh backend (OBJ, glTF) writes exactly the same mesh. Segments become
/// open cylinders, polygons flat two-sided faces. Pure and deterministic.
module RubberPlant.Backends.Mesh

open System
open System.Collections.Generic
open RubberPlant
open RubberPlant.Geometry

type MeshOptions =
    { /// Facets around a cylinder; at least 3.
      Sides: int
      /// World units per width unit; None means "auto" (§4.4).
      WidthScale: float option }

let defaultMeshOptions = { Sides = 12; WidthScale = None }

type MeshGroup =
    { /// Palette entry, 0-7 (§4.5).
      Color: int
      Positions: Vec3[]
      /// Unit length, one per position.
      Normals: Vec3[]
      /// 3 indices per triangle, counter-clockwise seen from outside.
      Triangles: int[] }

/// Groups in ascending Color; colors with no triangles are omitted.
type Mesh = { Groups: MeshGroup list }

// Scale-safe vector arithmetic (§4). Squaring the components of a finite vector
// can overflow (1e200) or underflow (1e-200); scaling by a power of two first
// can't, and it's exact, so the results are otherwise the same bits as the
// direct computation.

/// v scaled by 2^-e, where 2^e <= max |component| < 2^(e+1); and e. v must not be 0.
let private scaled (v: Vec3) =
    let m = max (abs v.X) (max (abs v.Y) (abs v.Z))
    let e = Math.ILogB m
    Vec3.create (Math.ScaleB(v.X, -e)) (Math.ScaleB(v.Y, -e)) (Math.ScaleB(v.Z, -e)), e

/// True when the direct computation is safe: the largest component squared is a
/// normal number, so nothing overflows, and whatever underflows is far below its
/// last bit. Then the scaled computation is unnecessary; skipping it is faster.
let private isSafe (v: Vec3) =
    let m = max (abs v.X) (max (abs v.Y) (abs v.Z))
    m > 1e-150 && m < 1e150

/// v / |v|, without overflow or underflow. v must not be 0.
let private normalize (v: Vec3) =
    if isSafe v then
        (1.0 / Vec3.length v) * v
    else
        let w, _ = scaled v
        (1.0 / Vec3.length w) * w

let private isFinite (v: Vec3) = Vec3.isFinite v

/// Vertices and triangles being collected for one palette entry.
type private GroupBuilder() =
    member val Positions = ResizeArray<Vec3>()
    member val Normals = ResizeArray<Vec3>()
    member val Triangles = ResizeArray<int>()

// ---------------------------------------------------------------- width scale

let private points (geometry: Geometry) =
    geometry
    |> Seq.collect (function
        | Segment(p0, p1, _, _) -> [ p0; p1 ]
        | Polygon(vs, _) -> vs)

/// The width scale k (§4.4).
let private widthScale (options: MeshOptions) (geometry: Geometry) : Result<float, string> =
    match options.WidthScale with
    | Some k when Double.IsFinite k && k > 0.0 -> Ok k
    | Some k -> Error $"the width scale must be a finite number greater than 0, got %g{k}"
    | None ->
        // Match the SVG that the default options would draw: same extent, same
        // margins, half the widest line reserved on each side.
        let svg = Svg.defaultOptions

        let widest =
            geometry
            |> List.fold
                (fun acc p ->
                    match p with
                    | Segment(_, _, w, _) -> max acc w
                    | Polygon _ -> acc)
                0.0

        let denominator = svg.Size - 2.0 * (svg.Margin + widest / 2.0)

        if not (denominator > 0.0) then
            Error
                $"the widest line (%g{widest}) is too wide for the automatic width scale (it wouldn't fit in a %g{svg.Size} px SVG); use --width-scale"
        else
            let pts = points geometry |> Array.ofSeq

            let extent (coord: Vec3 -> float) =
                if pts.Length = 0 then
                    0.0
                else
                    (pts |> Array.maxBy coord |> coord) - (pts |> Array.minBy coord |> coord)

            let e = max (extent _.X) (extent _.Y)
            Ok(if e = 0.0 then 1.0 else e / denominator)

// ------------------------------------------------------------------ cylinders

/// (cos, sin) of 360° j / s, for j = 0 … s − 1.
let private ringDirections (sides: int) =
    Array.init sides (fun j ->
        let struct (s, c) = Builtins.sinCosDeg (360.0 * float j / float sides)
        struct (c, s))

let private xAxis = Vec3.create 1.0 0.0 0.0
let private yAxis = Vec3.create 0.0 1.0 0.0
let private zAxis = Vec3.create 0.0 0.0 1.0

/// An open cylinder of radius r from p0 to p1 (§4.1).
let private addCylinder (g: GroupBuilder) (ring: struct (float * float)[]) (p0: Vec3) (p1: Vec3) (r: float) =
    if r > 0.0 && p1 <> p0 then
        let n = normalize (p1 - p0)
        // The world axis least aligned with n (X, Y, then Z on ties).
        let ax, ay, az = abs n.X, abs n.Y, abs n.Z

        let a =
            if ax <= ay && ax <= az then xAxis
            elif ay <= az then yAxis
            else zAxis

        let e1 = normalize (Vec3.cross n a)
        let e2 = Vec3.cross n e1
        let s = ring.Length
        let first = g.Positions.Count

        for centre in [ p0; p1 ] do
            for struct (c, sn) in ring do
                let d = c * e1 + sn * e2
                g.Positions.Add(centre + r * d)
                g.Normals.Add d

        for j in 0 .. s - 1 do
            let j' = (j + 1) % s
            g.Triangles.AddRange [ first + j; first + j'; first + s + j' ]
            g.Triangles.AddRange [ first + j; first + s + j'; first + s + j ]

// ------------------------------------------------------------------- polygons

/// Step 1 of §4.3: drop consecutive duplicates, then the last vertex while it
/// equals the first.
let private loopOf (vertices: Vec3 list) =
    let loop = ResizeArray<Vec3>()

    for v in vertices do
        if loop.Count = 0 || loop[loop.Count - 1] <> v then
            loop.Add v

    while loop.Count > 1 && loop[loop.Count - 1] = loop[0] do
        loop.RemoveAt(loop.Count - 1)

    loop.ToArray()

/// The Newell normal of a loop (twice its area, along its normal).
let private newell (loop: Vec3[]) =
    let mutable x, y, z = 0.0, 0.0, 0.0

    for i in 0 .. loop.Length - 1 do
        let a = loop[i]
        let b = loop[(i + 1) % loop.Length]
        x <- x + (a.Y - b.Y) * (a.Z + b.Z)
        y <- y + (a.Z - b.Z) * (a.X + b.X)
        z <- z + (a.X - b.X) * (a.Y + b.Y)

    Vec3.create x y z

/// Twice the signed area of the 2D triangle (a, b, c): positive when it turns
/// counter-clockwise.
let private orient (a: struct (float * float)) (b: struct (float * float)) (c: struct (float * float)) =
    let struct (au, av) = a
    let struct (bu, bv) = b
    let struct (cu, cv) = c
    (bu - au) * (cv - av) - (bv - av) * (cu - au)

/// Triangulates a loop projected to 2D (counter-clockwise), step 5 of §4.3.
/// Returns triangles as indices into the loop.
let private earClip (pts: struct (float * float)[]) : ResizeArray<int> =
    let tris = ResizeArray<int>()
    let rem = ResizeArray<int>(seq { 0 .. pts.Length - 1 })

    let isEar (i: int) =
        let n = rem.Count
        let p, v, q = rem[(i + n - 1) % n], rem[i], rem[(i + 1) % n]
        let pp, pv, pq = pts[p], pts[v], pts[q]

        orient pp pv pq > 0.0
        && rem
           |> Seq.forall (fun m ->
               if m = p || m = v || m = q then
                   true
               else
                   let x = pts[m]

                   // Vertices at the same position as a corner don't count, so a
                   // loop that touches itself at a vertex can still be clipped.
                   if x = pp || x = pv || x = pq then
                       true
                   else
                       // Inside the triangle or on its edges blocks the ear.
                       not (orient pp pv x >= 0.0 && orient pv pq x >= 0.0 && orient pq pp x >= 0.0))

    // The last triangle and the fan: zero-area ones are dropped, and clockwise
    // ones (only in self-intersecting loops) turned counter-clockwise.
    let emitIfNotFlat a b c =
        let area = orient pts[a] pts[b] pts[c]

        if area > 0.0 then tris.AddRange [ a; b; c ]
        elif area < 0.0 then tris.AddRange [ a; c; b ]

    let mutable fanned = false

    while not fanned && rem.Count > 3 do
        match Seq.tryFind isEar (seq { 0 .. rem.Count - 1 }) with
        | Some i ->
            let n = rem.Count
            tris.AddRange [ rem[(i + n - 1) % n]; rem[i]; rem[(i + 1) % n] ]
            rem.RemoveAt i
        | None ->
            // A self-intersecting or degenerate loop: a fan from the first vertex.
            for i in 1 .. rem.Count - 2 do
                emitIfNotFlat rem[0] rem[i] rem[i + 1]

            fanned <- true

    if not fanned then
        emitIfNotFlat rem[0] rem[1] rem[2]

    tris

/// A flat polygon (§4.3).
let private addPolygon (g: GroupBuilder) (vertices: Vec3 list) =
    let loop = loopOf vertices

    if loop.Length >= 3 then
        // Work on the loop scaled by a power of two (exact), so that the Newell
        // normal's products neither overflow nor underflow.
        let m = loop |> Array.map (fun v -> max (abs v.X) (max (abs v.Y) (abs v.Z))) |> Array.max
        let exponent = Math.ILogB m

        let work =
            loop
            |> Array.map (fun v -> Vec3.create (Math.ScaleB(v.X, -exponent)) (Math.ScaleB(v.Y, -exponent)) (Math.ScaleB(v.Z, -exponent)))

        let n = newell work

        let extent (coord: Vec3 -> float) =
            (work |> Array.maxBy coord |> coord) - (work |> Array.minBy coord |> coord)

        let e = max (extent _.X) (max (extent _.Y) (extent _.Z))

        if n <> Vec3.zero && Vec3.length n >= 1e-12 * e * e then
            // Drop the axis of N's largest component (X, Y, then Z on ties); keep
            // the other two in cyclic order, swapped if that component is negative.
            let ax, ay, az = abs n.X, abs n.Y, abs n.Z

            let project: Vec3 -> struct (float * float) =
                if ax >= ay && ax >= az then
                    if n.X >= 0.0 then (fun p -> struct (p.Y, p.Z)) else (fun p -> struct (p.Z, p.Y))
                elif ay >= az then
                    if n.Y >= 0.0 then (fun p -> struct (p.Z, p.X)) else (fun p -> struct (p.X, p.Z))
                else if n.Z >= 0.0 then
                    (fun p -> struct (p.X, p.Y))
                else
                    (fun p -> struct (p.Y, p.X))

            let tris = earClip (Array.map project work)

            if tris.Count > 0 then
                let first = g.Positions.Count
                let normal = normalize n

                for v in loop do
                    g.Positions.Add v
                    g.Normals.Add normal

                for t in tris do
                    g.Triangles.Add(first + t)

// ---------------------------------------------------------------------- build

/// Builds the mesh of some geometry (backends.md §4). Fails on invalid options,
/// or when the automatic width scale is impossible (the widest line wouldn't fit
/// in the default SVG).
let build (options: MeshOptions) (geometry: Geometry) : Result<Mesh, string> =
    if options.Sides < 3 then
        Error $"a cylinder needs at least 3 sides, got %d{options.Sides}"
    else
        widthScale options geometry
        |> Result.map (fun k ->
            let ring = ringDirections options.Sides
            let groups = Dictionary<int, GroupBuilder>()

            let groupOf color =
                let entry = Palette.entry color

                match groups.TryGetValue entry with
                | true, g -> g
                | false, _ ->
                    let g = GroupBuilder()
                    groups[entry] <- g
                    g

            for prim in geometry do
                match prim with
                | Segment(p0, p1, w, color) -> addCylinder (groupOf color) ring p0 p1 (k * w / 2.0)
                | Polygon(vs, color) -> addPolygon (groupOf color) vs

            { Groups =
                groups
                |> Seq.filter (fun kv -> kv.Value.Triangles.Count > 0)
                |> Seq.sortBy _.Key
                |> Seq.map (fun kv ->
                    { Color = kv.Key
                      Positions = kv.Value.Positions.ToArray()
                      Normals = kv.Value.Normals.ToArray()
                      Triangles = kv.Value.Triangles.ToArray() })
                |> List.ofSeq })
        |> Result.bind (fun mesh ->
            let finite (g: MeshGroup) =
                Array.forall isFinite g.Positions && Array.forall isFinite g.Normals

            if List.forall finite mesh.Groups then
                Ok mesh
            else
                Error "the mesh has coordinates too large to represent; use a smaller --width-scale or smaller steps")
