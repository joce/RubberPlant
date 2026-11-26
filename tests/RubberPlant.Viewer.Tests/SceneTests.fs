module RubberPlant.Viewer.Tests.SceneTests

open System
open Expecto
open RubberPlant
open RubberPlant.Geometry
open RubberPlant.Backends
open RubberPlant.Backends.Mesh
open RubberPlant.Viewer
open RubberPlant.Viewer.Scene

let private unitTriAt (offset: Vec3) (color: int) : MeshGroup =
    { Color = color
      Positions =
        [| offset + Vec3.create 0.0 0.0 0.0
           offset + Vec3.create 2.0 0.0 0.0
           offset + Vec3.create 0.0 4.0 0.0 |]
      Normals =
        [| Vec3.create 0.0 0.0 1.0
           Vec3.create 0.0 0.0 1.0
           Vec3.create 0.0 0.0 1.0 |]
      Triangles = [| 0; 1; 2 |] }

[<Tests>]
let tests =
    testList
        "Scene"
        [ test "double-precision centering eliminates single-precision jitter far from the origin" {
              let shift = Vec3.create 1e9 -1e9 1e9
              let mesh = { Groups = [ unitTriAt shift 1 ] }
              let scene = Expect.wantOk (fromMesh mesh) "scene builds"

              Expect.isFalse scene.IsEmpty "non-empty"
              Expect.floatClose Accuracy.veryHigh scene.Center.X (1e9 + 1.0) "center X"
              Expect.floatClose Accuracy.veryHigh scene.Center.Y (-1e9 + 2.0) "center Y"
              Expect.floatClose Accuracy.veryHigh scene.Center.Z 1e9 "center Z"
              Expect.floatClose Accuracy.veryHigh scene.Extent.X 2.0 "extent X"
              Expect.floatClose Accuracy.veryHigh scene.Extent.Y 4.0 "extent Y"
              Expect.floatClose Accuracy.veryHigh scene.Extent.Z 0.0 "extent Z"

              // Centered vertices must be exact in float32 despite 1e9 world offset.
              Expect.equal scene.Vertices.Length 18 "3 verts * 6 floats"
              Expect.equal scene.Vertices[0] -1.0f "v0.x"
              Expect.equal scene.Vertices[1] -2.0f "v0.y"
              Expect.equal scene.Vertices[2] 0.0f "v0.z"
              Expect.equal scene.Vertices[6] 1.0f "v1.x"
              Expect.equal scene.Vertices[7] -2.0f "v1.y"
              Expect.equal scene.Vertices[12] -1.0f "v2.x"
              Expect.equal scene.Vertices[13] 2.0f "v2.y"
          }

          test "uint32 index offsetting packs multiple MeshGroups in ascending Color order, including >65535 vertices" {
              // Group A (Color 5): 70,000 vertices; Group B (Color 1): 3 vertices.
              // Ascending Color order puts Color 1 first, Color 5 second, OR let's put 70,000 in Color 1
              // so Color 5's indices strictly exceed UInt16.MaxValue (65,535).
              let n = 70_000
              let bigPos = Array.init n (fun i -> Vec3.create (float i * 0.001) 0.0 0.0)
              let bigNorm = Array.create n (Vec3.create 0.0 1.0 0.0)

              let bigGroup =
                  { Color = 1
                    Positions = bigPos
                    Normals = bigNorm
                    Triangles = [| 0; 1; n - 1 |] }

              let secondGroup = unitTriAt (Vec3.create 0.0 1.0 0.0) 5
              // Pass groups intentionally out of order to verify sorting by ascending Color.
              let mesh = { Groups = [ secondGroup; bigGroup ] }
              let scene = Expect.wantOk (fromMesh mesh) "large multi-group mesh builds"

              Expect.equal scene.Ranges.Length 2 "two draw ranges"
              Expect.equal scene.Ranges[0] { Color = 1; FirstIndex = 0; IndexCount = 3 } "first range color 1"
              Expect.equal scene.Ranges[1] { Color = 5; FirstIndex = 3; IndexCount = 3 } "second range color 5"

              Expect.equal scene.Indices[0] 0u "g0 tri[0]"
              Expect.equal scene.Indices[2] (uint32 (n - 1)) "g0 tri[2] > 65535"
              Expect.equal scene.Indices[3] (uint32 n) "g1 tri[0] offset by 70000"
              Expect.equal scene.Indices[4] (uint32 (n + 1)) "g1 tri[1] offset by 70000"
              Expect.equal scene.Indices[5] (uint32 (n + 2)) "g1 tri[2] offset by 70000"
          }

          test "float32 span overflow and non-finite coordinates return Error" {
              let huge =
                  { Groups =
                      [ { Color = 0
                          Positions =
                            [| Vec3.create -1e40 0.0 0.0
                               Vec3.create 1e40 0.0 0.0
                               Vec3.create 0.0 1.0 0.0 |]
                          Normals = Array.create 3 (Vec3.create 0.0 0.0 1.0)
                          Triangles = [| 0; 1; 2 |] } ] }

              let err = Expect.wantError (fromMesh huge) "extent > float32 max fails"
              Expect.stringContains err "single-precision" "descriptive error"

              let nanMesh =
                  { Groups =
                      [ { Color = 0
                          Positions =
                            [| Vec3.create nan 0.0 0.0
                               Vec3.create 1.0 0.0 0.0
                               Vec3.create 0.0 1.0 0.0 |]
                          Normals = Array.create 3 (Vec3.create 0.0 0.0 1.0)
                          Triangles = [| 0; 1; 2 |] } ] }

              Expect.isError (fromMesh nanMesh) "NaN vertex fails"
          }

          test "empty mesh and degenerate-only geometry produce empty scene cleanly" {
              let sEmpty = Expect.wantOk (fromMesh { Groups = [] }) "empty mesh"
              Expect.isTrue sEmpty.IsEmpty "IsEmpty true"
              Expect.isTrue sEmpty.Bounds.IsEmpty "Bounds.IsEmpty true"
              Expect.isEmpty sEmpty.Vertices "no vertices"
              Expect.isEmpty sEmpty.Indices "no indices"
              Expect.isEmpty sEmpty.Ranges "no ranges"

              // Zero-length and zero-width segments yield Stats.BBox <> None while Mesh.build returns Groups = [].
              let compiled, _ = Expect.wantOk (Pipeline.compile "settings { width = 0; }\naxiom: F(1) F(0);\n") "compile"
              let output = Expect.wantOk (Pipeline.run compiled) "run"
              let mesh = Expect.wantOk (Mesh.build Mesh.defaultMeshOptions output.Geometry) "mesh"
              Expect.isEmpty mesh.Groups "degenerate geometry yields empty mesh groups"
              let scene = Expect.wantOk (fromMesh mesh) "scene from degenerate mesh"
              Expect.isTrue scene.IsEmpty "scene keyed on mesh.Groups.IsEmpty"
          }

          test "niceGridStep picks deterministic {1,2,5}*10^k yielding 8-16 cells across 2.5*span" {
              for span in [ 1e-4; 0.0073; 0.25; 1.0; 6.4; 6.41; 10.0; 123.4; 18740.0; 1e5 ] do
                  let step = niceGridStep span
                  let ratio = 2.5 * span / step
                  Expect.isLessThanOrEqual ratio (16.0 + 1e-12) $"span=%g{span}: 2.5*span/step <= 16"
                  Expect.isGreaterThan ratio 6.0 $"span=%g{span}: 2.5*span/step > 6 (minimal {{1,2,5}}*10^k)"

                  // Verify step is indeed in {1, 2, 5} * 10^k.
                  let exp = Math.Floor(Math.Log10 step)
                  let mantissa = step / Math.Pow(10.0, exp)
                  let okMantissa =
                      abs (mantissa - 1.0) < 1e-9
                      || abs (mantissa - 2.0) < 1e-9
                      || abs (mantissa - 5.0) < 1e-9
                      || abs (mantissa - 10.0) < 1e-9

                  Expect.isTrue okMantissa $"span=%g{span}: mantissa %g{mantissa} in {{1,2,5}}"
          }

          test "buildGrid places y=0 plane at y_local = -center.Y, handles y<0 models, and colors axes in bounds" {
              // Model spanning y in [-2, +6] -> center.Y = 2 -> y_local = -2.
              let mesh =
                  { Groups =
                      [ { Color = 0
                          Positions =
                            [| Vec3.create -1.0 -2.0 -1.0
                               Vec3.create 1.0 6.0 1.0
                               Vec3.create 1.0 -2.0 1.0 |]
                          Normals = Array.create 3 (Vec3.create 0.0 0.0 1.0)
                          Triangles = [| 0; 1; 2 |] } ] }

              let scene = Expect.wantOk (fromMesh mesh) "scene"
              let grid = Expect.wantSome (buildGrid scene) "grid built"
              Expect.isGreaterThan grid.VertexCount 0 "grid has lines"
              Expect.isGreaterThan grid.Extent 0.0 "grid extent > 0"

              // Every vertex has y_local = float32 (-center.Y) = -2.0f.
              let mutable sawXAxis = false
              let mutable sawZAxis = false

              for i = 0 to grid.VertexCount - 1 do
                  let o = i * 6
                  let y = grid.Vertices[o + 1]
                  let r, _, b = grid.Vertices[o + 3], grid.Vertices[o + 4], grid.Vertices[o + 5]
                  Expect.equal y -2.0f "grid lies on world y = 0 (local y = -center.Y)"

                  if r > b + 0.2f then sawXAxis <- true
                  if b > r + 0.2f then sawZAxis <- true

              Expect.isTrue sawXAxis "red-tinted X axis present when z=0 is in bounds"
              Expect.isTrue sawZAxis "blue-tinted Z axis present when x=0 is in bounds"

              // Model far away from x=0 and z=0 should NOT contain red/blue axis lines.
              let farMesh = { Groups = [ unitTriAt (Vec3.create 500.0 0.0 500.0) 0 ] }
              let farScene = Expect.wantOk (fromMesh farMesh) "far scene"
              let farGrid = Expect.wantSome (buildGrid farScene) "far grid"

              for i = 0 to farGrid.VertexCount - 1 do
                  let o = i * 6
                  let r, g, b = farGrid.Vertices[o + 3], farGrid.Vertices[o + 4], farGrid.Vertices[o + 5]
                  Expect.isLessThan (abs (r - b)) 0.1f "no axis color when origin outside grid bounds"
                  Expect.isLessThan (abs (g - b)) 0.1f "neutral gray grid lines"
          }

          test "buildGrid omits grid cleanly when |center.Y| exceeds float32 range" {
              let skyHigh =
                  { Groups = [ unitTriAt (Vec3.create 0.0 1e40 0.0) 0 ] }

              let scene = Expect.wantOk (fromMesh skyHigh) "small mesh at y=1e40 centers fine"
              Expect.equal (buildGrid scene) None "grid omitted instead of producing non-finite y_local"
          }

          test "niceGridStep handles subnormal, huge, zero, negative, and non-finite spans without hanging" {
              Expect.isTrue (Double.IsNaN(niceGridStep 0.0)) "span=0 -> NaN"
              Expect.isTrue (Double.IsNaN(niceGridStep -5.0)) "span<0 -> NaN"
              Expect.isTrue (Double.IsNaN(niceGridStep nan)) "span=NaN -> NaN"
              Expect.isTrue (Double.IsNaN(niceGridStep infinity)) "span=+Inf -> NaN"

              let subStep = niceGridStep 1e-323
              Expect.isTrue (Double.IsFinite subStep && subStep > 0.0) "subnormal span returns positive finite step immediately"

              let hugeStep = niceGridStep 1e308
              Expect.isTrue (Double.IsFinite hugeStep && hugeStep > 0.0) "1e308 span returns positive finite step immediately"

              // Subnormal mesh through fromMesh + buildGrid completes instantaneously.
              let subMesh =
                  { Groups =
                      [ { Color = 0
                          Positions =
                            [| Vec3.create 0.0 0.0 0.0
                               Vec3.create 1e-323 0.0 0.0
                               Vec3.create 0.0 1e-323 0.0 |]
                          Normals = Array.create 3 (Vec3.create 0.0 0.0 1.0)
                          Triangles = [| 0; 1; 2 |] } ] }

              let subScene = Expect.wantOk (fromMesh subMesh) "subnormal scene"
              let subGrid = buildGrid subScene
              Expect.isSome subGrid "subnormal grid returns cleanly"
          }

          test "buildGrid does not collapse on huge X/Z offsets and handles empty/zero-extent meshes" {
              // Regression for |center.X / step| >= 1e15: grid must retain full local span, not collapse to 0.
              let farXMesh = { Groups = [ unitTriAt (Vec3.create 1e20 0.0 0.0) 0 ] }
              let farScene = Expect.wantOk (fromMesh farXMesh) "farX scene"
              let farGrid = Expect.wantSome (buildGrid farScene) "farX grid built"
              let mutable minLocalX = Single.PositiveInfinity
              let mutable maxLocalX = Single.NegativeInfinity

              for i = 0 to farGrid.VertexCount - 1 do
                  let x = farGrid.Vertices[i * 6]
                  if x < minLocalX then minLocalX <- x
                  if x > maxLocalX then maxLocalX <- x

              Expect.isGreaterThan (maxLocalX - minLocalX) 1.0f "grid retains full local X span at 1e20 offset"

              // Empty scene and zero-extent mesh fall back to span = 1.0.
              let emptyGrid = Expect.wantSome (buildGrid emptyScene) "empty scene grid"
              Expect.floatClose Accuracy.veryHigh emptyGrid.Step (niceGridStep 1.0) "empty fallback step"

              let zeroExtentMesh =
                  { Groups =
                      [ { Color = 0
                          Positions = Array.create 3 (Vec3.create 1.0 2.0 3.0)
                          Normals = Array.create 3 (Vec3.create 0.0 1.0 0.0)
                          Triangles = [| 0; 1; 2 |] } ] }

              let zScene = Expect.wantOk (fromMesh zeroExtentMesh) "zero-extent scene"
              let zGrid = Expect.wantSome (buildGrid zScene) "zero-extent grid"
              Expect.floatClose Accuracy.veryHigh zGrid.Step (niceGridStep 1.0) "zero-extent fallback step"
          }

          test "fromMesh rejects mismatched normal count, bad triangle arity, and out-of-range indices" {
              let badNorms =
                  { Groups =
                      [ { Color = 0
                          Positions = Array.create 3 Vec3.zero
                          Normals = Array.create 2 (Vec3.create 0.0 1.0 0.0)
                          Triangles = [| 0; 1; 2 |] } ] }

              Expect.isError (fromMesh badNorms) "mismatched normals"

              let badArity =
                  { Groups =
                      [ { Color = 0
                          Positions = Array.create 3 Vec3.zero
                          Normals = Array.create 3 (Vec3.create 0.0 1.0 0.0)
                          Triangles = [| 0; 1 |] } ] }

              Expect.isError (fromMesh badArity) "triangles not multiple of 3"

              let badIdx =
                  { Groups =
                      [ { Color = 0
                          Positions = Array.create 3 Vec3.zero
                          Normals = Array.create 3 (Vec3.create 0.0 1.0 0.0)
                          Triangles = [| 0; 1; -1 |] } ] }

              Expect.isError (fromMesh badIdx) "negative triangle index"
          }

          test "fromMeshWithFrame preserves frame center/extent/bounds on empty meshes and keeps buildGrid stable" {
              let fullMesh = { Groups = [ unitTriAt (Vec3.create 10.0 2.0 4.0) 0 ] }
              let fullScene = Expect.wantOk (fromMesh fullMesh) "full scene"
              let frame: ReferenceFrame =
                  { Center = fullScene.Center
                    Extent = fullScene.Extent
                    Bounds = fullScene.Bounds
                    WidthScale = 0.25 }

              // Empty frame (e.g. step 0 of Capsella) must keep Center, Extent, Bounds, and non-unit grid.
              let emptyFrameScene = Expect.wantOk (fromMeshWithFrame frame { Groups = [] }) "empty frame scene"
              Expect.isTrue emptyFrameScene.IsEmpty "IsEmpty is true"
              Expect.isEmpty emptyFrameScene.Vertices "no vertices"
              Expect.isEmpty emptyFrameScene.Indices "no indices"
              Expect.isEmpty emptyFrameScene.Ranges "no ranges"
              Expect.equal emptyFrameScene.Center frame.Center "Center preserved"
              Expect.equal emptyFrameScene.Extent frame.Extent "Extent preserved"
              Expect.equal emptyFrameScene.Bounds frame.Bounds "Bounds preserved"
              Expect.isFalse emptyFrameScene.Bounds.IsEmpty "Bounds.IsEmpty stays false"

              let fullGrid = Expect.wantSome (buildGrid fullScene) "full grid"
              let emptyFrameGrid = Expect.wantSome (buildGrid emptyFrameScene) "empty frame grid"
              Expect.equal emptyFrameGrid.Step fullGrid.Step "grid step matches full sequence"
              Expect.equal emptyFrameGrid.Vertices fullGrid.Vertices "grid vertices match full sequence"

              // Non-empty frame centers against frame.Center rather than its own AABB center.
              let earlyMesh = { Groups = [ unitTriAt (Vec3.create 0.0 2.0 4.0) 1 ] }
              let earlyScene = Expect.wantOk (fromMeshWithFrame frame earlyMesh) "early frame scene"
              Expect.isFalse earlyScene.IsEmpty "non-empty"
              Expect.equal earlyScene.Center frame.Center "Center preserved on early frame"
              Expect.equal earlyScene.Extent frame.Extent "Extent preserved on early frame"
              Expect.equal earlyScene.Bounds frame.Bounds "Bounds preserved on early frame"
              // fullScene.Center.X = 11.0, earlyMesh v0.X = 0.0 -> local x = -11.0f
              Expect.equal earlyScene.Vertices[0] -11.0f "vertex centered against frame.Center.X"
          }

          test "validateAndAccumulateMeshBounds and referenceFrameOfBounds compute union AABB across frames" {
              let emptyMesh = { Groups = [] }
              let m1 = { Groups = [ unitTriAt (Vec3.create 0.0 0.0 0.0) 0 ] }
              let m2 = { Groups = [ unitTriAt (Vec3.create 10.0 4.0 -2.0) 0 ] }

              let acc0 = Expect.wantOk (validateAndAccumulateMeshBounds emptyMesh None) "empty"
              Expect.equal acc0 None "empty leaves None"
              let acc1 = Expect.wantOk (validateAndAccumulateMeshBounds m1 acc0) "m1"
              let acc2 = Expect.wantOk (validateAndAccumulateMeshBounds m2 acc1) "m2"
              let frame = Expect.wantOk (referenceFrameOfBounds 0.5 acc2) "frame"
              // Union X: [0, 12], Y: [0, 8], Z: [-2, 0] -> Center = (6, 4, -1), Extent = (12, 8, 2)
              Expect.equal frame.Center (Vec3.create 6.0 4.0 -1.0) "union center"
              Expect.equal frame.Extent (Vec3.create 12.0 8.0 2.0) "union extent"
              Expect.equal frame.WidthScale 0.5 "width scale"
              Expect.isFalse frame.Bounds.IsEmpty "non-empty bounds"

              let emptyFrame = Expect.wantOk (referenceFrameOfBounds 1.0 None) "all-empty frame"
              Expect.isTrue emptyFrame.Bounds.IsEmpty "all-empty frame has empty bounds"
          } ]
