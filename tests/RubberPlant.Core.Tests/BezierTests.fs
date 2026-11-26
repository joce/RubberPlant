module RubberPlant.Tests.BezierTests

open Expecto
open RubberPlant
open RubberPlant.Geometry

let private close (a: float) (b: float) msg = Expect.floatClose Accuracy.high a b msg

let private closeVec (a: Vec3) (b: Vec3) msg =
    close a.X b.X msg
    close a.Y b.Y msg
    close a.Z b.Z msg

let private flatPatchPoints (z: float) =
    [ for i in 0..3 do
          for j in 0..3 -> $"%g{float j / 3.0} %g{float i / 3.0} %g{z}" ]
    |> String.concat "\n"

let private minimalSurface (extraHeader: string) (patchBlock: string) =
    $"""0 1 0 1 -1 1
%s{extraHeader}CONTACT POINT: 0 0 0
END POINT: 0 1 0
HEADING: 0 1 0
UP: 0 0 1
SIZE: 1
%s{patchBlock}
"""

let private defaultInstance (surf: BezierSurface) =
    { Surface = surf
      Origin = Vec3.zero
      AxisX = Vec3.create 1.0 0.0 0.0
      AxisY = Vec3.create 0.0 1.0 0.0
      AxisZ = Vec3.create 0.0 0.0 1.0
      Scale = Vec3.create 1.0 1.0 1.0
      Color = 0 }

[<Tests>]
let tests =
    testList
        "Bezier"
        [ test "parses .s header, PRECISION S and T independently, TOP/BOTTOM COLOR, and 16 points" {
              let text =
                  minimalSurface
                      "PRECISION S: 3 T: 5\n"
                      ("leaf0\nTOP COLOR: 2 DIFFUSE: 0.5\nBOTTOM COLOR: 3 DIFFUSE: 0.25\nAL: ~ A: ~ AR: ~\nL: ~ R: ~\nBL: ~ B: ~ BR: ~\n"
                       + flatPatchPoints 0.0)

              let surf, warns = Expect.wantOk (Bezier.parse "L" None text) "parse"
              Expect.isEmpty warns "no warnings"
              Expect.equal surf.Name "L" "surface name"
              Expect.equal surf.DefaultDivisions (3, 5) "reads both S and T precision"
              Expect.equal surf.Patches.Length 1 "one patch"
              Expect.equal surf.Patches[0].Name "leaf0" "patch name"
              Expect.equal surf.Patches[0].ControlPoints.Length 16 "16 control points"
              closeVec surf.EndPoint (Vec3.create 0.0 1.0 0.0) "canonical endpoint"
          }

          test "default PRECISION is (6, 6) when omitted and cpfg X:/Y:/Z: vectors are accepted" {
              let text =
                  $"""-1 1 -1 1 -1 1
CONTACT POINT: X: 0 Y: 0 Z: 0
END POINT: X: 0 Y: 1 Z: 0
HEADING: X: 0 Y: 1 Z: 0
UP: X: 0 Y: 0 Z: 1
SIZE: 2
p0
%s{flatPatchPoints 0.0}
"""

              let surf, warns = Expect.wantOk (Bezier.parse "L" None text) "parse"
              Expect.isEmpty warns "no warnings"
              Expect.equal surf.DefaultDivisions (6, 6) "default precision"
              closeVec surf.EndPoint (Vec3.create 0.0 0.5 0.0) "endpoint scaled by 1/SIZE"
          }

          test "canonical orientation uses exact B_s^T transpose for non-symmetric frames (ABOP Fig 5.1)" {
              // Choose non-symmetric HEADING and UP so B_s <> B_s^T:
              // HEADING = (1, 0, 0) -> Y_s = (1, 0, 0)
              // UP      = (0, 1, 0) -> X_s = Y_s x UP = (0, 0, 1), Z_s = X_s x Y_s = (0, 1, 0)
              let cp = Vec3.create 10.0 -4.0 2.5
              let size = 2.0
              let xS = Vec3.create 0.0 0.0 1.0
              let yS = Vec3.create 1.0 0.0 0.0
              let zS = Vec3.create 0.0 1.0 0.0
              let localPt (u: float) (v: float) = Vec3.create (0.3 * u) (0.7 * v + 0.1) (-0.2 * u * v)
              let worldOf (q: Vec3) = cp + size * (q.X * xS + q.Y * yS + q.Z * zS)
              let endLocal = Vec3.create 0.25 1.5 -0.4
              let endWorld = worldOf endLocal

              let fmt (v: float) = v.ToString("R", System.Globalization.CultureInfo.InvariantCulture)

              let ptsText =
                  [ for i in 0..3 do
                        for j in 0..3 ->
                            let p = worldOf (localPt (float i / 3.0) (float j / 3.0))
                            $"%s{fmt p.X} %s{fmt p.Y} %s{fmt p.Z}" ]
                  |> String.concat "\n"

              let text =
                  $"""-100 100 -100 100 -100 100
CONTACT POINT: %s{fmt cp.X} %s{fmt cp.Y} %s{fmt cp.Z}
END POINT: %s{fmt endWorld.X} %s{fmt endWorld.Y} %s{fmt endWorld.Z}
HEADING: 1 0 0
UP: 0 1 0
SIZE: %s{fmt size}
p0
%s{ptsText}
"""

              let surf, _ = Expect.wantOk (Bezier.parse "S" None text) "parse"
              closeVec surf.EndPoint endLocal "END POINT round-trips into canonical space"

              for i in 0..3 do
                  for j in 0..3 do
                      let expected = localPt (float i / 3.0) (float j / 3.0)
                      closeVec surf.Patches[0].ControlPoints[i * 4 + j] expected $"cp[{i},{j}]"

              // Placing the canonical surface into a matching world frame recovers the original world points.
              let inst =
                  { Surface = surf
                    Origin = cp
                    AxisX = size * xS
                    AxisY = size * yS
                    AxisZ = size * zS
                    Scale = Vec3.create 1.0 1.0 1.0
                    Color = 0 }

              let actualPts = Bezier.instanceControlPoints inst |> Array.ofSeq

              for i in 0..3 do
                  for j in 0..3 do
                      let expectedW = worldOf (localPt (float i / 3.0) (float j / 3.0))
                      closeVec actualPts[i * 4 + j] expectedW $"world round-trip [{i},{j}]"
          }

          test "rejects malformed .s inputs" {
              let badCases =
                  [ ""
                    "1 2 3"
                    minimalSurface "PRECISION S: 0 T: 4\n" ("p0\n" + flatPatchPoints 0.0)
                    minimalSurface "PRECISION S: 4 T: -1\n" ("p0\n" + flatPatchPoints 0.0)
                    "0 1 0 1 0 1\nEND POINT: 0 1 0\nHEADING: 0 1 0\nUP: 0 0 1\nSIZE: 1\np0\n" + flatPatchPoints 0.0
                    "0 1 0 1 0 1\nCONTACT POINT: 0 0 0\nEND POINT: 0 1 0\nHEADING: 0 1 0\nUP: 0 0 1\nSIZE: 0\np0\n"
                    + flatPatchPoints 0.0
                    "0 1 0 1 0 1\nCONTACT POINT: 0 0 0\nEND POINT: 0 1 0\nHEADING: 0 1 0\nUP: 0 2 0\nSIZE: 1\np0\n"
                    + flatPatchPoints 0.0
                    minimalSurface "" "p0\n0 0 0\n1 0 0"
                    minimalSurface "" "" ]

              for text in badCases do
                  Expect.isError (Bezier.parse "Bad" None text) $"expected parse error for:\n%s{text}"
          }

          test "unknown and non-reciprocal neighbor links emit RP0307 with Pos and clear to None" {
              let p1 = "p1\nA: missing\nB: p2\nL: p2\n" + flatPatchPoints 0.0
              let p2 = "p2\nA: p1\n" + flatPatchPoints 0.0
              let text = minimalSurface "" (p1 + "\n" + p2)
              let pos = { Line = 4; Column = 1 }

              let surf, warns = Expect.wantOk (Bezier.parse "L" (Some pos) text) "parse"
              Expect.equal (warns |> List.map _.Code) [ Codes.InvalidSurfaceNeighbor; Codes.InvalidSurfaceNeighbor ] "2 warnings"
              Expect.allEqual (warns |> List.map _.Pos) (Some pos) "pos preserved"
              Expect.isNone surf.Patches[0].Neighbors.Top "unknown A cleared"
              Expect.equal surf.Patches[0].Neighbors.Bottom (Some "p2") "reciprocal B kept"
              Expect.isNone surf.Patches[0].Neighbors.Left "non-reciprocal L cleared"
              Expect.equal surf.Patches[1].Neighbors.Top (Some "p1") "reciprocal A kept"
          }

          test "3-tier normal solver handles pinched corners without zero or NaN normals" {
              // Top row (i = 0) collapses to a single point (0.5, 1.0, 0.0), creating a pinched tip.
              let pts =
                  [ for i in 0..3 do
                        for j in 0..3 ->
                            let u = float i / 3.0
                            let v = float j / 3.0
                            let x = (v - 0.5) * u + 0.5
                            let y = 1.0 - u
                            let z = 0.2 * u * (1.0 - u) * sin (System.Math.PI * v)
                            $"%g{x} %g{y} %g{z}" ]
                  |> String.concat "\n"

              let surf, _ = Expect.wantOk (Bezier.parse "Pinch" None (minimalSurface "PRECISION S: 4 T: 4\n" ("p0\n" + pts))) "parse"
              let tess = Bezier.tessellateSurface None (defaultInstance surf)

              Expect.isGreaterThan tess.Triangles.Length 0 "has non-degenerate triangles"

              for n in tess.Normals do
                  Expect.isTrue (Vec3.isFinite n) "finite normal"
                  close (Vec3.length n) 1.0 "unit normal"

              for t = 0 to tess.Triangles.Length / 3 - 1 do
                  let a = tess.Positions[tess.Triangles[3 * t]]
                  let b = tess.Positions[tess.Triangles[3 * t + 1]]
                  let c = tess.Positions[tess.Triangles[3 * t + 2]]
                  Expect.isGreaterThan (Vec3.length (Vec3.cross (b - a) (c - a))) 0.0 "no zero-area triangle"
          }

          test "order-independent seam averaging matches across shared edges regardless of patch order" {
              // Two tilted planes meeting along s = 1 of p0 / s = 0 of p1.
              let makePts (y0: float) (y1: float) (z0: float) (z1: float) =
                  [ for i in 0..3 do
                        let u = float i / 3.0
                        let y = y0 + (y1 - y0) * u
                        let z = z0 + (z1 - z0) * u

                        for j in 0..3 ->
                            let x = float j / 3.0
                            $"%g{x} %g{y} %g{z}" ]
                  |> String.concat "\n"

              let pA = "pA\nB: pB\n" + makePts 0.0 1.0 0.0 0.0
              let pB = "pB\nA: pA\n" + makePts 1.0 2.0 0.0 1.0

              let surfAB, _ = Expect.wantOk (Bezier.parse "S" None (minimalSurface "PRECISION S: 2 T: 2\n" (pA + "\n" + pB))) "AB"
              let surfBA, _ = Expect.wantOk (Bezier.parse "S" None (minimalSurface "PRECISION S: 2 T: 2\n" (pB + "\n" + pA))) "BA"

              let tessAB = Bezier.tessellateSurface None (defaultInstance surfAB)
              let tessBA = Bezier.tessellateSurface None (defaultInstance surfBA)

              // Seam vertices: pA row a=2 (indices 6,7,8) and pB row a=0 (indices 9,10,11) in AB.
              for k in 0..2 do
                  closeVec tessAB.Normals[6 + k] tessAB.Normals[9 + k] "shared seam normals match in AB"
                  // Normal of pA is (0, 0, -1), pB is (0, 1, -1)/sqrt(2), average should have Y > 0 and Z < 0.
                  Expect.isGreaterThan tessAB.Normals[6 + k].Y 0.2 "averaged Y > 0"
                  Expect.isLessThan tessAB.Normals[6 + k].Z -0.5 "averaged Z < 0"
                  // Order independence: pB's row 0 in BA (indices 0,1,2) matches pA's row 2 in AB (indices 6,7,8).
                  closeVec tessAB.Normals[6 + k] tessBA.Normals[k] "order-independent seam normal"
          }

          test "opposite normals across a folded seam keep each patch's own normal" {
              // p0 has normal +Z, p1 folds back 180 deg with normal -Z.
              let pts0 =
                  [ for i in 0..3 do
                        let y = float i / 3.0
                        for j in 0..3 -> $"%g{float j / 3.0} %g{y} 0" ]
                  |> String.concat "\n"

              let pts1 =
                  [ for i in 0..3 do
                        let y = 1.0 - float i / 3.0
                        for j in 0..3 -> $"%g{float j / 3.0} %g{y} 0" ]
                  |> String.concat "\n"

              let p0 = "p0\nB: p1\n" + pts0
              let p1 = "p1\nA: p0\n" + pts1
              let surf, _ = Expect.wantOk (Bezier.parse "Fold" None (minimalSurface "PRECISION S: 2 T: 2\n" (p0 + "\n" + p1))) "fold"
              let tess = Bezier.tessellateSurface None (defaultInstance surf)
              // Row a=2 of p0 (idx 6) keeps p0's own normal (0, 0, -1) or (0, 0, +1); row a=0 of p1 (idx 9) keeps opposite!
              let n0 = tess.Normals[6]
              let n1 = tess.Normals[9]
              close (Vec3.length n0) 1.0 "unit n0"
              close (Vec3.length n1) 1.0 "unit n1"
              closeVec (n0 + n1) Vec3.zero "each patch keeps its own normal on exact cancellation"
          }

          test "surfaceBoundaryPolygons extracts 16 edge vertices per patch" {
              let surf, _ = Expect.wantOk (Bezier.parse "L" None (minimalSurface "" ("p0\n" + flatPatchPoints 0.0))) "parse"
              let polys = Bezier.surfaceBoundaryPolygons (defaultInstance surf)
              Expect.equal polys.Length 1 "one patch"
              Expect.equal polys[0].Length 16 "16 boundary vertices"
              closeVec (polys[0][0]) (Vec3.create 0.0 0.0 0.0) "corner (0,0)"
              closeVec (polys[0][4]) (Vec3.create 0.0 1.0 0.0) "corner (1,0)"
              closeVec (polys[0][8]) (Vec3.create 1.0 1.0 0.0) "corner (1,1)"
              closeVec (polys[0][12]) (Vec3.create 1.0 0.0 0.0) "corner (0,1)"
          } ]
