/// Small hand-written geometries for the mesh backends (backends.md §9). The mesh
/// tests check them with assertions; the OBJ and glTF backends will add full
/// golden files for them (golden/mesh/).
module RubberPlant.Backends.Tests.MeshFixtures

open RubberPlant.Geometry
open RubberPlant.Backends.Mesh

let v x y z = Vec3.create x y z

/// One vertical cylinder, radius 0.05 (explicit width scale).
let oneCylinder =
    { defaultMeshOptions with WidthScale = Some 0.01 }, [ Segment(v 0.0 0.0 0.0, v 0.0 1.0 0.0, 10.0, 0) ]

/// A chain of three segments bending in 3D, with the automatic width scale.
let bentChain =
    defaultMeshOptions,
    [ Segment(v 0.0 0.0 0.0, v 0.0 1.0 0.0, 10.0, 2)
      Segment(v 0.0 1.0 0.0, v 0.5 1.8 0.0, 10.0, 2)
      Segment(v 0.5 1.8 0.0, v 0.5 2.6 0.4, 10.0, 2) ]

/// A concave leaf (an arrowhead with a reflex vertex) in a tilted plane.
let concaveLeaf =
    let p x y = v x y (0.5 * y)
    defaultMeshOptions, [ Polygon([ p 0.0 0.0; p 1.0 1.0; p 0.0 3.0; p (-1.0) 1.0; p 0.0 0.8 ], 1) ]

/// Two palette entries: brown segments (colors 2 and 10, the same entry) and a
/// green triangle, interleaved.
let twoColors =
    defaultMeshOptions,
    [ Segment(v 0.0 0.0 0.0, v 0.0 1.0 0.0, 4.0, 2)
      Polygon([ v 0.0 1.0 0.0; v 0.5 1.5 0.0; v 0.0 2.0 0.0 ], 1)
      Segment(v 0.0 1.0 0.0, v 0.0 2.0 0.0, 2.0, 10) ]

let all =
    [ "one-cylinder", oneCylinder
      "bent-chain", bentChain
      "concave-leaf", concaveLeaf
      "two-colors", twoColors ]
