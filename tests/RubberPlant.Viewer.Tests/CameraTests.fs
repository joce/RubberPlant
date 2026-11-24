module RubberPlant.Viewer.Tests.CameraTests

open System
open Expecto
open RubberPlant.Geometry
open RubberPlant.Viewer.Camera

let private expectVecClose (actual: Vec3) (expected: Vec3) (msg: string) =
    Expect.floatClose Accuracy.high actual.X expected.X $"%s{msg}.X"
    Expect.floatClose Accuracy.high actual.Y expected.Y $"%s{msg}.Y"
    Expect.floatClose Accuracy.high actual.Z expected.Z $"%s{msg}.Z"

let private cornersOf (h: Vec3) =
    [| for sx in [ -1.0; 1.0 ] do
           for sy in [ -1.0; 1.0 ] do
               for sz in [ -1.0; 1.0 ] do
                   Vec3.create (sx * h.X) (sy * h.Y) (sz * h.Z) |]

let private assertAllCornersInNdc (bounds: Bounds) (cam: Camera) (aspect: float) (gridExtent: float option) (label: string) =
    let mvp = viewProjectionMatrix bounds cam aspect gridExtent
    let struct (near, far) = clipPlanes bounds cam gridExtent
    Expect.isGreaterThan near 0.0 $"%s{label}: near > 0"
    Expect.isLessThan near far $"%s{label}: near < far"

    let mutable maxLateral = 0.0

    for c in cornersOf bounds.HalfExtents do
        let struct (cx, cy, cz, cw) = transformPoint mvp c
        Expect.isGreaterThan cw 0.0 $"%s{label}: corner in front of camera (w > 0)"
        let nx = cx / cw
        let ny = cy / cw
        let nz = cz / cw
        Expect.isLessThanOrEqual (abs nx) (1.0 + 1e-5) $"%s{label}: |ndc.x| <= 1"
        Expect.isLessThanOrEqual (abs ny) (1.0 + 1e-5) $"%s{label}: |ndc.y| <= 1"
        Expect.isGreaterThanOrEqual nz (-1.0 - 1e-5) $"%s{label}: ndc.z >= -1"
        Expect.isLessThanOrEqual nz (1.0 + 1e-5) $"%s{label}: ndc.z <= 1"
        maxLateral <- max maxLateral (max (abs nx) (abs ny))

    maxLateral

[<Tests>]
let tests =
    testList
        "Camera"
        [ test "pole-free basis is orthonormal and right-handed across all angles including poles" {
              let azimuths = [ -360.0; -270.0; -180.0; -90.0; -35.0; 0.0; 35.0; 90.0; 180.0; 270.0; 360.0 ]
              let elevations = [ -90.0; -60.0; -45.0; 0.0; 25.0; 45.0; 60.0; 90.0 ]

              for az in azimuths do
                  for el in elevations do
                      let struct (dir, right, up) = basisOfAngles az el
                      let tag = $"az=%g{az},el=%g{el}"
                      Expect.floatClose Accuracy.veryHigh (Vec3.length dir) 1.0 $"%s{tag} |dir|=1"
                      Expect.floatClose Accuracy.veryHigh (Vec3.length right) 1.0 $"%s{tag} |right|=1"
                      Expect.floatClose Accuracy.veryHigh (Vec3.length up) 1.0 $"%s{tag} |up|=1"
                      Expect.floatClose Accuracy.veryHigh (Vec3.dot dir right) 0.0 $"%s{tag} dir.right=0"
                      Expect.floatClose Accuracy.veryHigh (Vec3.dot dir up) 0.0 $"%s{tag} dir.up=0"
                      Expect.floatClose Accuracy.veryHigh (Vec3.dot right up) 0.0 $"%s{tag} right.up=0"
                      // Right-handed basis (right, up, dir): right x up = dir
                      expectVecClose (Vec3.cross right up) dir $"%s{tag} right x up = dir"
          }

          test "canonical orientations match viewer spec §3.1" {
              let struct (dF, rF, uF) = basisOfAngles 0.0 0.0
              expectVecClose dF (Vec3.create 0.0 0.0 1.0) "front dir"
              expectVecClose rF (Vec3.create 1.0 0.0 0.0) "front right"
              expectVecClose uF (Vec3.create 0.0 1.0 0.0) "front up"

              let struct (dS, rS, uS) = basisOfAngles 90.0 0.0
              expectVecClose dS (Vec3.create 1.0 0.0 0.0) "side dir"
              expectVecClose rS (Vec3.create 0.0 0.0 -1.0) "side right"
              expectVecClose uS (Vec3.create 0.0 1.0 0.0) "side up"

              let struct (dT, rT, uT) = basisOfAngles 0.0 90.0
              expectVecClose dT (Vec3.create 0.0 1.0 0.0) "top dir"
              expectVecClose rT (Vec3.create 1.0 0.0 0.0) "top right"
              expectVecClose uT (Vec3.create 0.0 0.0 -1.0) "top up"

              let struct (dB, rB, uB) = basisOfAngles 0.0 -90.0
              expectVecClose dB (Vec3.create 0.0 -1.0 0.0) "bottom dir"
              expectVecClose rB (Vec3.create 1.0 0.0 0.0) "bottom right"
              expectVecClose uB (Vec3.create 0.0 0.0 1.0) "bottom up"
          }

          test "aspect-aware 8-corner fit frames tall, wide, square, planar, 1D stick, empty, and zero-extent bounds" {
              let shapes =
                  [ "square", boundsOfHalfExtents (Vec3.create 1.0 1.0 1.0)
                    "tall", boundsOfHalfExtents (Vec3.create 0.5 5.0 0.5)
                    "wide", boundsOfHalfExtents (Vec3.create 6.0 0.5 1.0)
                    "planar-2d", boundsOfHalfExtents (Vec3.create 2.0 3.0 0.0)
                    "slender-1d-z", boundsOfHalfExtents (Vec3.create 0.0 0.0 4.0)
                    "slender-1d-y", boundsOfHalfExtents (Vec3.create 0.0 4.0 0.0) ]

              let aspects = [ 0.5; 1.0; 16.0 / 9.0 ]

              for name, b in shapes do
                  for aspect in aspects do
                      for view in canonicalViews do
                          let cam = fit b view.AzimuthDeg view.ElevationDeg aspect
                          Expect.equal cam.Target Vec3.zero $"%s{name} target centered"
                          Expect.isGreaterThan cam.Distance 0.0 $"%s{name} distance > 0"
                          let maxLat = assertAllCornersInNdc b cam aspect None $"%s{name}/{view.Label}/a=%g{aspect}"

                          // When the shape has non-zero lateral projection, the tightest corner reaches ~1/pad.
                          if name <> "slender-1d-z" && name <> "slender-1d-y" && name <> "planar-2d" then
                              Expect.floatClose
                                  (Accuracy.medium)
                                  maxLat
                                  (1.0 / fitPadding)
                                  $"%s{name}/{view.Label}/a=%g{aspect} tight to 1/pad"

              // Empty and zero-extent fallback both use unit box R = 1 and produce identical fit distance.
              let zeroExtent = boundsOfHalfExtents Vec3.zero
              let dEmpty = fitDistance emptyBounds 35.0 25.0 1.0
              let dZero = fitDistance zeroExtent 35.0 25.0 1.0
              Expect.isGreaterThan dEmpty 0.0 "empty fallback positive distance"
              Expect.floatClose Accuracy.veryHigh dEmpty dZero "empty and zero-extent agree"
          }

          test "shared-scale contactSheetDistance equals max canonical square fit and frames all 4 views" {
              let b = boundsOfHalfExtents (Vec3.create 4.0 10.0 1.5)
              let dSheet = contactSheetDistance b

              let expected =
                  canonicalViews
                  |> Array.map (fun v -> fitDistance b v.AzimuthDeg v.ElevationDeg 1.0)
                  |> Array.max

              Expect.floatClose Accuracy.veryHigh dSheet expected "contactSheetDistance = max(d_k)"

              for v in canonicalViews do
                  let cam =
                      { Target = Vec3.zero
                        AzimuthDeg = v.AzimuthDeg
                        ElevationDeg = v.ElevationDeg
                        Distance = dSheet }

                  assertAllCornersInNdc b cam 1.0 (Some 20.0) $"sheet/%s{v.Label}" |> ignore
          }

          test "clipPlanes keeps near strictly in front of auto-fitted slender end-on geometry (with and without grid)" {
              // Exact regression scenario from docs/viewer.md §7.1: half-extents (0.001, 0.001, 1) at (0, 0).
              let b = boundsOfHalfExtents (Vec3.create 0.001 0.001 1.0)
              let cam = fit b 0.0 0.0 1.0
              let fd = frontDepth b cam
              Expect.isGreaterThan fd 0.0 "frontDepth > 0"

              for label, gridOpt in [ "no-grid", None; "with-grid", Some 2.5 ] do
                  let struct (near, far) = clipPlanes b cam gridOpt
                  Expect.isGreaterThan near 0.0 $"%s{label}: near > 0"
                  Expect.isLessThan near fd $"%s{label}: near < frontDepth"
                  Expect.isLessThan fd far $"%s{label}: frontDepth < far"
                  assertAllCornersInNdc b cam 1.0 gridOpt $"slender/%s{label}" |> ignore

              // Pure 1D stick viewed strictly end-on (x_i = y_i = 0) also satisfies near < frontDepth.
              let stick = boundsOfHalfExtents (Vec3.create 0.0 0.0 1.0)
              let stickCam = fit stick 0.0 0.0 1.0
              let stickFd = frontDepth stick stickCam
              let struct (sNear, sFar) = clipPlanes stick stickCam (Some 2.5)
              Expect.isGreaterThan sNear 0.0 "1D stick near > 0"
              Expect.isLessThan sNear stickFd "1D stick near < frontDepth"
              Expect.isLessThan stickFd sFar "1D stick frontDepth < far"
          }

          test "clipPlanes is homogeneous of degree 1 across arbitrary scales and handles huge models and deep zoom" {
              let baseBounds = boundsOfHalfExtents (Vec3.create 2.0 5.0 3.0)
              let baseCam = fit baseBounds 35.0 25.0 1.5
              let baseGrid = Some 12.0
              let struct (baseNear, baseFar) = clipPlanes baseBounds baseCam baseGrid

              for scale in [ 1e-4; 1e4 ] do
                  let scaledBounds = boundsOfHalfExtents (scale * baseBounds.HalfExtents)
                  let scaledCam = fit scaledBounds 35.0 25.0 1.5
                  let struct (n, f) = clipPlanes scaledBounds scaledCam (Some(scale * 12.0))
                  Expect.floatClose Accuracy.high scaledCam.Distance (scale * baseCam.Distance) $"scale %g{scale} dist"
                  Expect.floatClose Accuracy.high n (scale * baseNear) $"scale %g{scale} near"
                  Expect.floatClose Accuracy.high f (scale * baseFar) $"scale %g{scale} far"

              // Huge model (fig2-8c scale, R ~ 18,740)
              let huge = boundsOfHalfExtents (Vec3.create 9000.0 15000.0 7000.0)
              let hugeCam = fit huge 35.0 25.0 1.0
              let struct (hNear, hFar) = clipPlanes huge hugeCam (Some 30000.0)
              Expect.isGreaterThan hNear 0.0 "huge near > 0"
              Expect.isLessThan hNear (frontDepth huge hugeCam) "huge near < frontDepth"
              Expect.isLessThan (hFar / hNear) 20000.0 "reasonable depth ratio on huge model"

              // Deep zoom inside the AABB (frontDepth <= 0)
              let insideCam = { baseCam with Distance = 0.25 }
              Expect.isLessThanOrEqual (frontDepth baseBounds insideCam) 0.0 "inside AABB has frontDepth <= 0"
              let struct (iNear, iFar) = clipPlanes baseBounds insideCam baseGrid
              Expect.isGreaterThan iNear 0.0 "inside near > 0"
              Expect.isLessThanOrEqual iNear (0.1 * insideCam.Distance + 1e-12) "inside near <= 0.1 * dist"
              Expect.isLessThan iNear iFar "inside near < far"

              // Grid visibility lowers baseNear on shallow wide models when meshNear > 0.05 * dist.
              let shallow = boundsOfHalfExtents (Vec3.create 10.0 0.1 0.1)
              let shallowCam = fit shallow 0.0 0.0 1.0
              let struct (nearNoGrid, _) = clipPlanes shallow shallowCam None
              let struct (nearWithGrid, _) = clipPlanes shallow shallowCam (Some 25.0)
              Expect.isLessThan nearWithGrid nearNoGrid "grid lowers near plane on shallow wide model"
          }

          test "pure interactive orbit, pan, and zoom helpers enforce invariants" {
              let cam =
                  { Target = Vec3.zero
                    AzimuthDeg = 35.0
                    ElevationDeg = 25.0
                    Distance = 10.0 }

              let upClamped = orbit 10.0 200.0 cam
              Expect.equal upClamped.ElevationDeg 90.0 "elevation clamped at +90"
              Expect.equal upClamped.AzimuthDeg 45.0 "azimuth updated"

              let downClamped = orbit -5.0 -300.0 cam
              Expect.equal downClamped.ElevationDeg -90.0 "elevation clamped at -90"

              let panned = pan 0.5 -0.25 cam
              let b = basis cam
              let expectedTarget = (5.0 * b.Right) + (-2.5 * b.Up)
              expectVecClose panned.Target expectedTarget "pan translates along camera right/up scaled by distance"

              let zoomedIn = zoom 1.0 0.01 1000.0 cam
              Expect.floatClose Accuracy.high zoomedIn.Distance 9.0 "zoom +1 multiplies by 0.9"
              let zoomedFloor = zoom 10000.0 0.01 1000.0 cam
              Expect.equal zoomedFloor.Distance 0.01 "zoom clamped at minDist"
              let zoomedCeil = zoom -10000.0 0.01 1000.0 cam
              Expect.equal zoomedCeil.Distance 1000.0 "zoom clamped at maxDist"
              let zoomedNan = zoom nan 0.01 1000.0 cam
              Expect.equal zoomedNan cam "zoom with NaN leaves camera unchanged"
          }

          test "clipPlanes handles panned target and empty bounds, and viewProjection centers target in NDC" {
              let b = boundsOfHalfExtents (Vec3.create 1.0 2.0 1.0)
              let cam =
                  { Target = Vec3.create 5.0 -3.0 2.0
                    AzimuthDeg = 35.0
                    ElevationDeg = 25.0
                    Distance = 20.0 }

              let struct (near, far) = clipPlanes b cam (Some 10.0)
              Expect.isGreaterThan near 0.0 "panned near > 0"
              Expect.isLessThan near far "panned near < far"

              // Target must project to NDC (0, 0) and eye position must map to (0, 0, 0) in eye space.
              let mvp = viewProjectionMatrix b cam 1.5 (Some 10.0)
              let struct (tx, ty, tz, tw) = transformPoint mvp cam.Target
              Expect.isGreaterThan tw 0.0 "target in front of camera"
              Expect.floatClose Accuracy.low (tx / tw) 0.0 "target NDC x = 0"
              Expect.floatClose Accuracy.low (ty / tw) 0.0 "target NDC y = 0"
              Expect.isGreaterThan (tz / tw) -1.0 "target behind near plane"
              Expect.isLessThan (tz / tw) 1.0 "target in front of far plane"

              let view = viewMatrix cam
              let eyePos = (basis cam).Pos
              let struct (ex, ey, ez, _) = transformPoint view eyePos
              Expect.floatClose Accuracy.low ex 0.0 "eye pos maps to eye origin x"
              Expect.floatClose Accuracy.low ey 0.0 "eye pos maps to eye origin y"
              Expect.floatClose Accuracy.low ez 0.0 "eye pos maps to eye origin z"

              // Empty bounds clipPlanes stays positive and finite.
              let emptyCam = fit emptyBounds 35.0 25.0 1.0
              let struct (eNear, eFar) = clipPlanes emptyBounds emptyCam None
              Expect.isGreaterThan eNear 0.0 "empty near > 0"
              Expect.isLessThan eNear eFar "empty near < far"
          } ]
