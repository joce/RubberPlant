module RubberPlant.Backends.Tests.MeshTests

open System
open Expecto
open RubberPlant.Geometry
open RubberPlant.Backends
open RubberPlant.Backends.Mesh
open RubberPlant.Backends.Tests.MeshFixtures

let private ok (r: Result<Mesh, string>) =
    match r with
    | Ok m -> m
    | Error msg -> failtestf "build failed: %s" msg

let private failsWith (fragment: string) (r: Result<Mesh, string>) =
    match r with
    | Error msg -> Expect.stringContains msg fragment "error message"
    | Ok m -> failtestf "expected an error, got %A" m

let private explicitScale k = { defaultMeshOptions with WidthScale = Some k }

let private seg (p0: Vec3) (p1: Vec3) w c = Segment(p0, p1, w, c)

let private single (m: Mesh) =
    match m.Groups with
    | [ g ] -> g
    | gs -> failtestf "expected one group, got %d" gs.Length

let private triangles (g: MeshGroup) =
    [ for t in 0 .. g.Triangles.Length / 3 - 1 ->
          g.Positions[g.Triangles[3 * t]], g.Positions[g.Triangles[3 * t + 1]], g.Positions[g.Triangles[3 * t + 2]] ]

/// The (unnormalized) normal of a triangle, from its winding.
let private faceNormal (a: Vec3, b: Vec3, c: Vec3) = Vec3.cross (b - a) (c - a)

let private area t = Vec3.length (faceNormal t) / 2.0

let private close (a: float) (b: float) msg = Expect.floatClose Accuracy.high a b msg

let private closeVec (a: Vec3) (b: Vec3) msg =
    close a.X b.X msg
    close a.Y b.Y msg
    close a.Z b.Z msg

/// Distance from p to the line through p0 and p1, and the foot of the perpendicular.
let private toAxis (p0: Vec3) (p1: Vec3) (p: Vec3) =
    let n = (1.0 / Vec3.length (p1 - p0)) * (p1 - p0)
    let foot = p0 + Vec3.dot (p - p0) n * n
    Vec3.length (p - foot), foot

let private cylinderTests =
    testList
        "cylinders"
        [ test "24 vertices and 24 triangles with the defaults" {
              let g = build (explicitScale 0.01) [ seg (v 0.0 0.0 0.0) (v 1.0 2.0 3.0) 10.0 0 ] |> ok |> single
              Expect.equal g.Positions.Length 24 "vertices"
              Expect.equal g.Normals.Length 24 "normals"
              Expect.equal g.Triangles.Length (3 * 24) "triangles"
          }

          test "2s vertices and 2s triangles for s sides" {
              for s in [ 3; 4; 7; 32 ] do
                  let g =
                      build { Sides = s; WidthScale = Some 1.0 } [ seg (v 0.0 0.0 0.0) (v 0.0 0.0 1.0) 1.0 0 ]
                      |> ok
                      |> single

                  Expect.equal g.Positions.Length (2 * s) $"%d{s} sides: vertices"
                  Expect.equal g.Triangles.Length (3 * 2 * s) $"%d{s} sides: triangles"
          }

          test "vertices lie on the tube, with unit normals pointing away from the axis" {
              let p0, p1 = v 0.3 -0.2 0.1, v 1.0 2.0 -1.5
              let g = build (explicitScale 0.01) [ seg p0 p1 10.0 0 ] |> ok |> single

              for i in 0 .. g.Positions.Length - 1 do
                  let dist, foot = toAxis p0 p1 g.Positions[i]
                  close dist 0.05 "radius = k w / 2"
                  close (Vec3.length g.Normals[i]) 1.0 "unit normal"
                  closeVec g.Normals[i] ((1.0 / dist) * (g.Positions[i] - foot)) "normal is radial"
          }

          test "rings at p0 then p1" {
              let p0, p1 = v 0.0 0.0 0.0, v 0.0 0.0 2.0
              let g = build (explicitScale 1.0) [ seg p0 p1 1.0 0 ] |> ok |> single

              for j in 0..11 do
                  close g.Positions[j].Z 0.0 $"ring 0, vertex %d{j}"
                  close g.Positions[12 + j].Z 2.0 $"ring 1, vertex %d{j}"
                  closeVec (g.Positions[12 + j] - g.Positions[j]) (p1 - p0) "same direction j on both rings"
          }

          test "outward winding: every face normal points away from the axis" {
              for p0, p1 in [ v 0.0 0.0 0.0, v 0.0 1.0 0.0
                              v 0.0 0.0 0.0, v 0.0 -1.0 0.0
                              v 1.0 1.0 1.0, v -2.0 0.5 3.0
                              v 0.0 0.0 0.0, v 0.0 0.0 -1.0 ] do
                  let g = build (explicitScale 0.1) [ seg p0 p1 1.0 0 ] |> ok |> single

                  for a, b, c as t in triangles g do
                      let centroid = (1.0 / 3.0) * (a + b + c)
                      let _, foot = toAxis p0 p1 centroid
                      Expect.isGreaterThan (Vec3.dot (faceNormal t) (centroid - foot)) 0.0 "outward"
          }

          test "the cross-section basis starts from the least aligned axis, X first on ties" {
              // Along Y: X and Z tie (both perpendicular), so a = X and
              // e1 = Y × X = −Z: vertex 0 is p0 + r (0, 0, −1).
              let g = build (explicitScale 1.0) [ seg (v 0.0 0.0 0.0) (v 0.0 1.0 0.0) 2.0 0 ] |> ok |> single
              Expect.equal g.Positions[0] (v 0.0 0.0 -1.0) "vertex 0"
              Expect.equal g.Normals[0] (v 0.0 0.0 -1.0) "normal 0"
              // e2 = n × e1 = Y × −Z = −X: vertex 3 (90°) is p0 + r (−1, 0, 0).
              closeVec g.Positions[3] (v -1.0 0.0 0.0) "vertex 3"
          }

          test "zero-length and zero-width segments produce nothing" {
              let m =
                  build
                      (explicitScale 1.0)
                      [ seg (v 1.0 1.0 1.0) (v 1.0 1.0 1.0) 3.0 0; seg (v 0.0 0.0 0.0) (v 0.0 1.0 0.0) 0.0 0 ]
                  |> ok

              Expect.isEmpty m.Groups "no triangles, no group"
          }

          test "a cylinder's vertices are its own, never shared" {
              let g =
                  build
                      (explicitScale 0.1)
                      [ seg (v 0.0 0.0 0.0) (v 0.0 1.0 0.0) 1.0 0; seg (v 0.0 1.0 0.0) (v 0.0 2.0 0.0) 1.0 0 ]
                  |> ok
                  |> single

              Expect.equal g.Positions.Length 48 "2 × 24"
              Expect.equal (g.Triangles |> Array.take 72 |> Array.max) 23 "first cylinder"
              Expect.equal (g.Triangles |> Array.skip 72 |> Array.min) 24 "second cylinder"
          } ]

let private polygonTests =
    let poly vs = build defaultMeshOptions [ Polygon(vs, 0) ]

    let checkCcw (g: MeshGroup) =
        let n = g.Normals[0]

        for t in triangles g do
            Expect.isGreaterThan (Vec3.dot (faceNormal t) n) 0.0 "counter-clockwise around N"

        for normal in g.Normals do
            Expect.equal normal n "flat shading: one normal"

        close (Vec3.length n) 1.0 "unit normal"

    let totalArea g = triangles g |> List.sumBy area

    testList
        "polygons"
        [ test "a triangle" {
              let g = poly [ v 0.0 0.0 0.0; v 1.0 0.0 0.0; v 0.0 1.0 0.0 ] |> ok |> single
              Expect.equal g.Positions.Length 3 ""
              Expect.equal g.Triangles [| 0; 1; 2 |] ""
              Expect.equal g.Normals[0] (v 0.0 0.0 1.0) ""
          }

          test "a clockwise square faces the other way" {
              let g =
                  poly [ v 0.0 0.0 0.0; v 0.0 1.0 0.0; v 1.0 1.0 0.0; v 1.0 0.0 0.0 ] |> ok |> single

              Expect.equal g.Normals[0] (v 0.0 0.0 -1.0) ""
              Expect.equal (g.Triangles.Length / 3) 2 ""
              checkCcw g
              close (totalArea g) 1.0 "area"
          }

          test "ear clipping of a concave L shape" {
              // L shape: area 3, a reflex vertex at (1, 1).
              let l =
                  [ v 0.0 0.0 0.0; v 2.0 0.0 0.0; v 2.0 1.0 0.0; v 1.0 1.0 0.0; v 1.0 2.0 0.0; v 0.0 2.0 0.0 ]

              let g = poly l |> ok |> single
              Expect.equal g.Positions.Length 6 "every vertex"
              Expect.equal (g.Triangles.Length / 3) 4 "n − 2 triangles"
              checkCcw g
              close (totalArea g) 3.0 "the triangles cover the L exactly"
              // Pinned order: scans restart from the first remaining vertex.
              Expect.equal g.Triangles [| 0; 1; 2; 0; 2; 3; 5; 0; 3; 3; 4; 5 |] "triangles"
          }

          test "concave polygons in every orientation" {
              let l = [ 0.0, 0.0; 2.0, 0.0; 2.0, 1.0; 1.0, 1.0; 1.0, 2.0; 0.0, 2.0 ]

              let planes: (float * float -> Vec3) list =
                  [ fun (a, b) -> v a b 0.0
                    fun (a, b) -> v b a 0.0
                    fun (a, b) -> v 0.0 a b
                    fun (a, b) -> v 0.0 b a
                    fun (a, b) -> v b 0.0 a
                    fun (a, b) -> v a 0.0 b
                    fun (a, b) -> v a (0.3 * a + 0.2 * b) b ]

              for plane in planes do
                  let g = poly (l |> List.map plane) |> ok |> single
                  Expect.equal (g.Triangles.Length / 3) 4 "n − 2 triangles"
                  checkCcw g
                  // The L's area projected on its plane: 3 × the plane's area factor.
                  let expected = 3.0 * Vec3.length (Vec3.cross (plane (1.0, 0.0) - plane (0.0, 0.0)) (plane (0.0, 1.0) - plane (0.0, 0.0)))
                  close (totalArea g) expected "area"
          }

          test "collinear vertices along the edges give no flat triangles" {
              // A triangle drawn with F F: midpoints on every edge.
              let g =
                  poly
                      [ v 0.0 0.0 0.0; v 1.0 0.0 0.0; v 2.0 0.0 0.0; v 1.5 1.0 0.0; v 1.0 2.0 0.0; v 0.5 1.0 0.0 ]
                  |> ok
                  |> single

              for t in triangles g do
                  Expect.isGreaterThan (area t) 0.0 "no zero-area triangle"

              checkCcw g
              close (totalArea g) 2.0 "area"
          }

          test "duplicates are dropped, and a closing vertex too" {
              let g =
                  poly
                      [ v 0.0 0.0 0.0
                        v 0.0 0.0 0.0
                        v 1.0 0.0 0.0
                        v 1.0 1.0 0.0
                        v 1.0 1.0 0.0
                        v 0.0 1.0 0.0
                        v 0.0 0.0 0.0
                        v 0.0 0.0 0.0 ]
                  |> ok
                  |> single

              Expect.equal g.Positions.Length 4 "the loop"
              close (totalArea g) 1.0 "area"
          }

          test "a loop that touches itself at a vertex" {
              // Two triangles sharing the origin, as one loop.
              let g =
                  poly
                      [ v 0.0 0.0 0.0; v 1.0 0.0 0.0; v 1.0 1.0 0.0; v 0.0 0.0 0.0; v -1.0 0.0 0.0; v -1.0 -1.0 0.0 ]
                  |> ok
                  |> single

              Expect.equal (g.Triangles.Length / 3) 2 ""
              checkCcw g
              close (totalArea g) 1.0 "area"
          }

          test "the fan fallback, turned counter-clockwise" {
              // No vertex of this self-intersecting loop is an ear (found in review).
              // The fan's signed areas are 6, -2, -2: the last two are swapped.
              let g =
                  poly [ v 1.0 0.0 0.0; v 3.0 0.0 0.0; v 0.0 3.0 0.0; v 1.0 2.0 0.0; v 2.0 0.0 0.0 ] |> ok |> single

              Expect.equal g.Normals[0] (v 0.0 0.0 1.0) ""
              Expect.equal g.Triangles [| 0; 1; 2; 0; 3; 2; 0; 4; 3 |] "triangles"
              checkCcw g
          }

          test "the fan fallback drops zero-area triangles" {
              // The same loop with (1.5, 0) added: still no ear, and the last fan
              // triangle (1,0) (2,0) (1.5,0) is flat.
              let g =
                  poly [ v 1.0 0.0 0.0; v 3.0 0.0 0.0; v 0.0 3.0 0.0; v 1.0 2.0 0.0; v 2.0 0.0 0.0; v 1.5 0.0 0.0 ]
                  |> ok
                  |> single

              Expect.equal g.Triangles [| 0; 1; 2; 0; 3; 2; 0; 4; 3 |] "triangles"
              checkCcw g
          }

          test "a clockwise last triangle is turned counter-clockwise" {
              // Self-intersecting: one ear, then a last triangle with negative area
              // (found in review).
              let g = poly [ v 0.0 0.0 0.0; v 3.0 0.0 0.0; v 0.0 1.0 0.0; v 2.0 2.0 0.0 ] |> ok |> single
              Expect.equal g.Triangles [| 3; 0; 1; 1; 3; 2 |] "triangles"
              checkCcw g
          }

          test "a pentagram: two ears, then a clockwise last triangle turned around" {
              let star =
                  [ for i in 0..4 ->
                        let a = Math.PI / 2.0 + float (2 * i) * 2.0 * Math.PI / 5.0
                        v (cos a) (sin a) 0.0 ]

              let g = poly star |> ok |> single
              Expect.equal g.Triangles [| 4; 0; 1; 1; 2; 3; 1; 4; 3 |] "triangles"
              checkCcw g
          }

          test "degenerate polygons produce nothing" {
              for vs in
                  [ []
                    [ v 0.0 0.0 0.0 ]
                    [ v 0.0 0.0 0.0; v 1.0 0.0 0.0 ]
                    [ v 0.0 0.0 0.0; v 1.0 0.0 0.0; v 0.0 0.0 0.0; v 1.0 0.0 0.0 ]
                    [ v 0.0 0.0 0.0; v 1.0 1.0 1.0; v 2.0 2.0 2.0 ] // collinear
                    [ v 0.0 0.0 0.0; v 1.0 1.0 0.0; v 1.0 0.0 0.0; v 0.0 1.0 0.0 ] ] do // bow tie: no net area
                  let m = poly vs |> ok
                  Expect.isEmpty m.Groups $"%A{vs}"
          } ]

let private widthScaleTests =
    let radiusOf (m: Mesh) (p0: Vec3) (p1: Vec3) =
        fst (toAxis p0 p1 (single m).Positions[0])

    testList
        "width scale"
        [ test "explicit" {
              let p0, p1 = v 0.0 0.0 0.0, v 0.0 1.0 0.0
              close (radiusOf (build (explicitScale 0.5) [ seg p0 p1 3.0 0 ] |> ok) p0 p1) 0.75 "r = k w / 2"
          }

          test "auto matches the default SVG" {
              // E = 100, W = 10: k = 100 / (800 − 2 (10 + 5)) = 100 / 770.
              let p0, p1 = v 0.0 0.0 0.0, v 0.0 100.0 0.0
              let m = build defaultMeshOptions [ seg p0 p1 10.0 0 ] |> ok
              close (radiusOf m p0 p1) (100.0 / 770.0 * 5.0) ""
          }

          test "auto uses the larger of the x and y extents, polygons included" {
              let p0, p1 = v 0.0 0.0 0.0, v 0.0 1.0 50.0 // z doesn't count
              let leaf = Polygon([ v 0.0 0.0 0.0; v 200.0 0.0 0.0; v 0.0 2.0 0.0 ], 1)
              let m = build defaultMeshOptions [ seg p0 p1 2.0 0; leaf ] |> ok
              let g = m.Groups |> List.find (fun g -> g.Color = 0)
              close (fst (toAxis p0 p1 g.Positions[0])) (200.0 / 778.0 * 1.0) ""
          }

          test "auto is 1 when the front view has no extent" {
              let p0, p1 = v 0.0 0.0 0.0, v 0.0 0.0 1.0
              close (radiusOf (build defaultMeshOptions [ seg p0 p1 2.0 0 ] |> ok) p0 p1) 1.0 ""
          }

          test "auto fails when the widest line wouldn't fit in the default SVG" {
              for w in [ 780.0; 790.0 ] do
                  build defaultMeshOptions [ seg (v 0.0 0.0 0.0) (v 0.0 1.0 0.0) w 0 ]
                  |> failsWith "--width-scale"
          }

          test "invalid options" {
              let g = [ seg (v 0.0 0.0 0.0) (v 0.0 1.0 0.0) 1.0 0 ]
              build { defaultMeshOptions with Sides = 2 } g |> failsWith "at least 3 sides"

              for k in [ 0.0; -1.0; nan; infinity ] do
                  build (explicitScale k) g |> failsWith "width scale"
          } ]

let private scaleTests =
    testList
        "scale"
        [ test "huge cylinders are finite" {
              // axiom: F(1e200); squaring its length used to overflow into NaNs.
              let p0, p1 = v 0.0 0.0 0.0, v 0.0 1e200 0.0
              let g = build defaultMeshOptions [ seg p0 p1 1.0 0 ] |> ok |> single

              for i in 0 .. g.Positions.Length - 1 do
                  Expect.isTrue (Vec3.isFinite g.Positions[i]) "finite position"
                  close (Vec3.length g.Normals[i]) 1.0 "unit normal"

              // Vertex 0 is p0 + r d0, with d0 = (0, 0, -1) (basis tie-break).
              close (-g.Positions[0].Z / 1e200) (1.0 / 779.0 / 2.0) "radius"
          }

          test "tiny cylinders keep their shape" {
              let p0, p1 = v 0.0 0.0 0.0, v 0.0 1e-200 0.0
              let g = build (explicitScale 1e-200) [ seg p0 p1 1.0 0 ] |> ok |> single

              for n in g.Normals do
                  close (Vec3.length n) 1.0 "unit normal"
          }

          test "huge and tiny triangles behave like unit ones" {
              for s in [ 1e-150; 1e-90; 1.0; 1e90; 1e150 ] do
                  let g =
                      build defaultMeshOptions [ Polygon([ v 0.0 0.0 0.0; v s 0.0 0.0; v 0.0 s 0.0 ], 0) ]
                      |> ok
                      |> single

                  Expect.equal g.Triangles [| 0; 1; 2 |] $"scale %g{s}"
                  closeVec g.Normals[0] (v 0.0 0.0 1.0) $"scale %g{s}"
          }

          test "polygons far from the origin behave like near ones" {
              // Found in review: scaling by the absolute coordinates made these vanish.
              for z in [ 1.0; 1e90; 1e200 ] do
                  for s in [ 1e-90; 1.0 ] do
                      let g =
                          build
                              defaultMeshOptions
                              [ Polygon([ v 0.0 0.0 z; v s 0.0 z; v 0.0 s z ], 0) ]
                          |> ok
                          |> single

                      Expect.equal g.Triangles [| 0; 1; 2 |] $"size %g{s} at z = %g{z}"
                      closeVec g.Normals[0] (v 0.0 0.0 1.0) $"size %g{s} at z = %g{z}"
                      Expect.equal g.Positions[1] (v s 0.0 z) "world coordinates"
          }

          test "polygons spanning nearly the whole double range" {
              // Found in review: the local frame's differences overflowed and the
              // triangle vanished. Whichever vertex comes first, it's kept.
              let a, b, c = v -1e308 0.0 0.0, v 1e308 0.0 0.0, v 0.0 1e308 0.0

              for loop in [ [ a; b; c ]; [ b; c; a ]; [ c; a; b ] ] do
                  let g = build (explicitScale 1.0) [ Polygon(loop, 0) ] |> ok |> single
                  Expect.equal (g.Triangles.Length / 3) 1 "one triangle"
                  closeVec g.Normals[0] (v 0.0 0.0 1.0) "normal"
          }

          test "subnormal detail survives a huge translation" {
              // Found in review: halving every vertex rounded these to zero.
              let s = Double.Epsilon
              let g =
                  build (explicitScale 1.0) [ Polygon([ v 0.0 0.0 1e308; v s 0.0 1e308; v 0.0 s 1e308 ], 0) ]
                  |> ok
                  |> single

              Expect.equal g.Triangles [| 0; 1; 2 |] ""
              closeVec g.Normals[0] (v 0.0 0.0 1.0) ""
          }

          test "coordinates beyond double precision are an error" {
              build defaultMeshOptions [ seg (v -1e308 0.0 0.0) (v 1e308 0.0 0.0) 1.0 0 ]
              |> failsWith "too large"

              build (explicitScale 1e300) [ seg (v 0.0 0.0 0.0) (v 0.0 1.0 0.0) 1e10 0 ]
              |> failsWith "too large"
          } ]

let private groupTests =
    testList
        "groups"
        [ test "by palette entry, ascending; equivalent colors share a group" {
              let m =
                  build
                      (explicitScale 0.1)
                      [ seg (v 0.0 0.0 0.0) (v 0.0 1.0 0.0) 1.0 3
                        seg (v 0.0 1.0 0.0) (v 0.0 2.0 0.0) 1.0 9
                        seg (v 0.0 2.0 0.0) (v 0.0 3.0 0.0) 1.0 -7
                        seg (v 0.0 3.0 0.0) (v 0.0 4.0 0.0) 1.0 0 ]
                  |> ok

              Expect.equal (m.Groups |> List.map _.Color) [ 0; 1; 3 ] "colors"
              let g1 = m.Groups |> List.find (fun g -> g.Color = 1)
              Expect.equal g1.Positions.Length 48 "9 and -7 are both entry 1"
              // Geometry order within the group: 9 (y 1-2) before -7 (y 2-3).
              Expect.isLessThan g1.Positions[0].Y 1.5 "first primitive first"
              Expect.isGreaterThan g1.Positions[24].Y 1.5 "second primitive second"
          }

          test "empty geometry gives an empty mesh" { Expect.isEmpty (build defaultMeshOptions [] |> ok).Groups "" }

          test "deterministic" {
              for _, (options, geometry) in all do
                  Expect.equal (build options geometry |> ok) (build options geometry |> ok) ""
          } ]

let private fixtureTests =
    testList
        "fixtures"
        [ test "one cylinder" {
              let options, geometry = oneCylinder
              let g = build options geometry |> ok |> single
              Expect.equal (g.Positions.Length, g.Triangles.Length / 3) (24, 24) ""
          }

          test "bent chain" {
              let options, geometry = bentChain
              let g = build options geometry |> ok |> single
              Expect.equal g.Color 2 ""
              Expect.equal (g.Positions.Length, g.Triangles.Length / 3) (72, 72) ""
          }

          test "concave leaf" {
              let options, geometry = concaveLeaf
              let g = build options geometry |> ok |> single
              Expect.equal g.Color 1 ""
              Expect.equal (g.Positions.Length, g.Triangles.Length / 3) (5, 3) ""
              // Area of the arrowhead in its plane (tilted: y stretched by √1.25).
              let expected = 2.6 * sqrt 1.25 // shoelace area of the 2D loop × stretch
              close (triangles g |> List.sumBy area) expected "area"
          }

          test "two colors" {
              let options, geometry = twoColors
              let m = build options geometry |> ok
              Expect.equal (m.Groups |> List.map _.Color) [ 1; 2 ] ""
              Expect.equal (m.Groups[1].Positions.Length) 48 "colors 2 and 10 share entry 2"
          } ]

let private gcTests =
    let sec p h l u w sL sU c contour =
        { P = p
          H = h
          L = l
          U = u
          Width = w
          ScaleL = sL
          ScaleU = sU
          Color = c
          Contour = contour }

    let h0 = v 0.0 1.0 0.0
    let l0 = v -1.0 0.0 0.0
    let u0 = v 0.0 0.0 1.0

    testList
        "generalized cylinders"
        [ test "circle contour: Sides override, vertex/triangle counts, and outward normals" {
              let gc =
                  GeneralizedCylinder
                      { Sides = 8
                        Sections =
                          [ sec (v 0.0 0.0 0.0) h0 l0 u0 2.0 1.0 1.0 0 Circle
                            sec (v 0.0 1.0 0.0) h0 l0 u0 2.0 1.0 1.0 0 Circle
                            sec (v 0.0 2.0 0.0) h0 l0 u0 2.0 1.0 1.0 0 Circle ] }

              let g = build (explicitScale 1.0) [ gc ] |> ok |> single
              Expect.equal g.Positions.Length (3 * 8) "3 shared rings of 8 vertices"
              Expect.equal (g.Triangles.Length / 3) (2 * 2 * 8) "2 spans * 2s triangles"

              for i in 0 .. g.Positions.Length - 1 do
                  close (Vec3.length g.Normals[i]) 1.0 "unit normal"

              for a, b, c as t in triangles g do
                  let centroid = (1.0 / 3.0) * (a + b + c)
                  let _, foot = toAxis (v 0.0 0.0 0.0) (v 0.0 2.0 0.0) centroid
                  Expect.isGreaterThan (Vec3.dot (faceNormal t) (centroid - foot)) 0.0 "outward winding"
          }

          test "circle parallel transport avoids twist across phyllotactic roll" {
              // Simulate a vertical trunk where section 1 has rolled 137.5° around H,
              // matching ABOP trunk rolls without twisting the circular mesh.
              let a = 137.5 * Math.PI / 180.0
              let l1 = v (-cos a) 0.0 (-sin a)
              let u1 = v (-sin a) 0.0 (cos a)

              let gc =
                  GeneralizedCylinder
                      { Sides = 6
                        Sections =
                          [ sec (v 0.0 0.0 0.0) h0 l0 u0 1.0 1.0 1.0 0 Circle
                            sec (v 0.0 1.0 0.0) h0 l1 u1 1.0 1.0 1.0 0 Circle ] }

              let g = build (explicitScale 1.0) [ gc ] |> ok |> single
              let ring0 = g.Positions |> Array.filter (fun p -> abs p.Y < 0.5)
              let ring1 = g.Positions |> Array.filter (fun p -> abs (p.Y - 1.0) < 0.5)
              Expect.equal ring0.Length 6 "6 vertices on ring 0"
              Expect.equal ring1.Length 6 "6 vertices on ring 1"

              for p0 in ring0 do
                  let hasMatch = ring1 |> Array.exists (fun p1 -> Vec3.length (p1 - p0 - v 0.0 1.0 0.0) < 1e-9)
                  Expect.isTrue hasMatch $"no longitudinal twist at %A{p0}"
          }

          test "flat contour has +U front normals and follows section roll" {
              let gc =
                  GeneralizedCylinder
                      { Sides = 0
                        Sections =
                          [ sec (v 0.0 0.0 0.0) h0 l0 u0 2.0 1.0 1.0 1 Flat
                            sec (v 0.0 1.0 0.0) h0 l0 u0 2.0 1.0 1.0 1 Flat ] }

              let g = build (explicitScale 1.0) [ gc ] |> ok |> single
              Expect.equal g.Positions.Length 4 "2 sections * 2 points"
              Expect.equal (g.Triangles.Length / 3) 2 "2 triangles"

              for n in g.Normals do
                  closeVec n (v 0.0 0.0 1.0) "+U front normal"

              for t in triangles g do
                  Expect.isGreaterThan (Vec3.dot (faceNormal t) (v 0.0 0.0 1.0)) 0.0 "winding faces +U"
          }

          test "UCup contour has odd vertex count with midrib on spine and curls toward +U" {
              let gc =
                  GeneralizedCylinder
                      { Sides = 8
                        Sections =
                          [ sec (v 0.0 0.0 0.0) h0 l0 u0 2.0 1.0 1.0 1 (UCup 0.5)
                            sec (v 0.0 1.0 0.0) h0 l0 u0 2.0 1.0 1.0 1 (UCup 0.5) ] }

              let g = build (explicitScale 1.0) [ gc ] |> ok |> single
              // s = 8 -> r = max 5 (8/2 + 1) = 5 (odd) -> 5 points per section
              Expect.equal g.Positions.Length 10 "2 * 5 vertices"
              Expect.equal (g.Triangles.Length / 3) 8 "4 quads * 2 triangles"
              Expect.isTrue (g.Positions |> Array.exists (fun p -> Vec3.length (p - v 0.0 0.0 0.0) < 1e-9)) "midrib at (0,0,0)"
              Expect.isTrue (g.Positions |> Array.exists (fun p -> Vec3.length (p - v 0.0 1.0 0.0) < 1e-9)) "midrib at (0,1,0)"
              let edges = g.Positions |> Array.filter (fun p -> abs (abs p.X - 1.0) < 1e-9)
              Expect.equal edges.Length 4 "4 outer edge vertices (2 per section)"

              for e in edges do
                  Expect.isGreaterThan e.Z 0.0 "edges curl toward +U"
          }

          test "VFold contour splits into 2 strips for crisp crease normals" {
              let gc =
                  GeneralizedCylinder
                      { Sides = 0
                        Sections =
                          [ sec (v 0.0 0.0 0.0) h0 l0 u0 2.0 1.0 1.0 1 (VFold 0.5)
                            sec (v 0.0 1.0 0.0) h0 l0 u0 2.0 1.0 1.0 1 (VFold 0.5) ] }

              let g = build (explicitScale 1.0) [ gc ] |> ok |> single
              Expect.equal g.Positions.Length 8 "2 strips * 4 vertices"
              Expect.equal (g.Triangles.Length / 3) 4 "4 triangles"
              // Left half normal points (+X, 0, +Z); right half normal points (-X, 0, +Z).
              Expect.isGreaterThan g.Normals[0].X 0.0 "left half tilts inward"
              Expect.isGreaterThan g.Normals[0].Z 0.0 "left half faces +U"
              Expect.isLessThan g.Normals[4].X 0.0 "right half tilts inward"
              Expect.isGreaterThan g.Normals[4].Z 0.0 "right half faces +U"
          }

          test "pinched !(0) tips skip zero-area triangles without NaN normals" {
              let g = build (fst gcFlatPinched) (snd gcFlatPinched) |> ok |> single
              Expect.equal (g.Triangles.Length / 3) 2 "only the two non-degenerate tip triangles"

              for i in 0 .. g.Positions.Length - 1 do
                  Expect.isTrue (Vec3.isFinite g.Normals[i]) "finite normal"
                  close (Vec3.length g.Normals[i]) 1.0 "unit normal"
                  closeVec g.Normals[i] (v 0.0 0.0 1.0) "+U normal from adjacent/triangle"
          }

          test "mid-GC color switch emits separate groups with duplicated boundary vertices" {
              let m = build (fst gcVFoldColors) (snd gcVFoldColors) |> ok
              Expect.equal (m.Groups |> List.map _.Color) [ 1; 2 ] "two palette groups"
              Expect.equal m.Groups[0].Positions.Length 8 "span 0 has both strips in color 1"
              Expect.equal m.Groups[1].Positions.Length 8 "span 1 duplicates boundary vertices in color 2"
          }

          test "coincident positions dropped and 180-degree hairpins split cleanly without NaN" {
              let gc =
                  GeneralizedCylinder
                      { Sides = 6
                        Sections =
                          [ sec (v 0.0 0.0 0.0) h0 l0 u0 0.5 1.0 1.0 0 Circle
                            sec (v 0.0 0.0 0.0) h0 l0 u0 0.5 1.0 1.0 0 Circle // coincident
                            sec (v 0.0 1.0 0.0) h0 l0 u0 0.5 1.0 1.0 0 Circle
                            sec (v 0.0 0.0 0.0) (v 0.0 -1.0 0.0) (v 1.0 0.0 0.0) u0 0.5 1.0 1.0 0 Circle ] } // 180° hairpin

              let g = build (explicitScale 1.0) [ gc ] |> ok |> single
              Expect.equal (g.Triangles.Length / 3) (2 * 2 * 6) "2 valid spans * 12 triangles"

              for n in g.Normals do
                  Expect.isTrue (Vec3.isFinite n) "finite normal after hairpin"
                  close (Vec3.length n) 1.0 "unit normal"
          }

          test "ScaleContour scales effective width in auto width_scale" {
              // Spine Y in [0, 100], effective width = 10 * 2 = 20 -> k = 100 / (800 - 2 * (10 + 10)) = 100 / 760.
              let gc =
                  GeneralizedCylinder
                      { Sides = 4
                        Sections =
                          [ sec (v 0.0 0.0 0.0) h0 l0 u0 10.0 2.0 0.5 0 Flat
                            sec (v 0.0 100.0 0.0) h0 l0 u0 10.0 2.0 0.5 0 Flat ] }

              let g = build defaultMeshOptions [ gc ] |> ok |> single
              let expectedHalfSpan = 0.5 * (100.0 / 760.0) * 10.0 * 2.0
              close (abs g.Positions[0].X) expectedHalfSpan "auto width_scale uses Width * max ScaleL ScaleU"
          }

          test "huge and tiny GCs remain finite with unit normals" {
              let big =
                  GeneralizedCylinder
                      { Sides = 6
                        Sections =
                          [ sec (v 0.0 0.0 0.0) h0 l0 u0 1.0 1.0 1.0 0 Circle
                            sec (v 0.0 1e200 0.0) h0 l0 u0 1.0 1.0 1.0 0 Circle ] }

              let gBig = build defaultMeshOptions [ big ] |> ok |> single

              for i in 0 .. gBig.Positions.Length - 1 do
                  Expect.isTrue (Vec3.isFinite gBig.Positions[i]) "finite big pos"
                  close (Vec3.length gBig.Normals[i]) 1.0 "unit big normal"

              let tiny =
                  GeneralizedCylinder
                      { Sides = 6
                        Sections =
                          [ sec (v 0.0 0.0 0.0) h0 l0 u0 1.0 1.0 1.0 0 Circle
                            sec (v 0.0 1e-200 0.0) h0 l0 u0 1.0 1.0 1.0 0 Circle ] }

              let gTiny = build (explicitScale 1e-200) [ tiny ] |> ok |> single

              for n in gTiny.Normals do
                  close (Vec3.length n) 1.0 "unit tiny normal"
          } ]

[<Tests>]
let tests =
    testList "Mesh" [ cylinderTests; polygonTests; widthScaleTests; scaleTests; groupTests; fixtureTests; gcTests ]
