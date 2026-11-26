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

let private sec p h l u w sL sU c contour =
    { P = p
      H = h
      L = l
      U = u
      Width = w
      ScaleL = sL
      ScaleU = sU
      Color = c
      Contour = contour }

/// Bent 6-sided circular generalized cylinder tapering along the spine.
let gcCircle =
    let inv = 1.0 / sqrt 2.0
    let h0 = v 0.0 1.0 0.0
    let h1 = v inv inv 0.0
    let l0 = v -1.0 0.0 0.0
    let l1 = v -inv inv 0.0
    let u0 = v 0.0 0.0 1.0

    { defaultMeshOptions with WidthScale = Some 1.0 },
    [ GeneralizedCylinder
          { Sides = 6
            Sections =
              [ sec (v 0.0 0.0 0.0) h0 l0 u0 0.4 1.0 1.0 2 Circle
                sec (v 0.0 1.0 0.0) h0 l0 u0 0.3 1.0 1.0 2 Circle
                sec (v inv (1.0 + inv) 0.0) h1 l1 u0 0.2 1.0 1.0 2 Circle ] } ]

/// Flat ribbon with pinched !(0) base and tip, verifying +U front normals.
let gcFlatPinched =
    let h = v 0.0 1.0 0.0
    let l = v -1.0 0.0 0.0
    let u = v 0.0 0.0 1.0

    { defaultMeshOptions with WidthScale = Some 1.0 },
    [ GeneralizedCylinder
          { Sides = 0
            Sections =
              [ sec (v 0.0 0.0 0.0) h l u 0.0 1.0 1.0 1 Flat
                sec (v 0.0 1.0 0.0) h l u 1.0 1.0 1.0 1 Flat
                sec (v 0.0 2.0 0.0) h l u 0.0 1.0 1.0 1 Flat ] } ]

/// UCup contour morphing from flat (0.0) to cupped (0.8) to shallow (0.2).
let gcUCupMorph =
    let h = v 0.0 1.0 0.0
    let l = v -1.0 0.0 0.0
    let u = v 0.0 0.0 1.0

    { Sides = 6; WidthScale = Some 1.0 },
    [ GeneralizedCylinder
          { Sides = 0
            Sections =
              [ sec (v 0.0 0.0 0.0) h l u 0.5 1.0 1.0 4 (UCup 0.0)
                sec (v 0.0 1.0 0.0) h l u 1.2 1.0 1.0 4 (UCup 0.8)
                sec (v 0.0 2.0 0.0) h l u 0.4 1.0 1.0 4 (UCup 0.2) ] } ]

/// VFold contour with mid-GC color switch across palette entries.
let gcVFoldColors =
    let h = v 0.0 1.0 0.0
    let l = v -1.0 0.0 0.0
    let u = v 0.0 0.0 1.0

    { defaultMeshOptions with WidthScale = Some 1.0 },
    [ GeneralizedCylinder
          { Sides = 0
            Sections =
              [ sec (v 0.0 0.0 0.0) h l u 0.8 1.0 1.0 1 (VFold 0.5)
                sec (v 0.0 1.0 0.0) h l u 1.0 1.0 1.0 2 (VFold 0.5)
                sec (v 0.0 2.0 0.0) h l u 0.4 1.0 1.0 2 (VFold 0.5) ] } ]

let all =
    [ "one-cylinder", oneCylinder
      "bent-chain", bentChain
      "concave-leaf", concaveLeaf
      "two-colors", twoColors
      "gc-circle", gcCircle
      "gc-flat-pinched", gcFlatPinched
      "gc-ucup-morph", gcUCupMorph
      "gc-vfold-colors", gcVFoldColors ]
