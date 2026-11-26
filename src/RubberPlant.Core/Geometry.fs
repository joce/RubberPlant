/// Backend-neutral geometry produced by the turtle (§5.4).
module RubberPlant.Geometry

[<Struct>]
type Vec3 =
    { X: float
      Y: float
      Z: float }

    static member (+)(a: Vec3, b: Vec3) = { X = a.X + b.X; Y = a.Y + b.Y; Z = a.Z + b.Z }
    static member (-)(a: Vec3, b: Vec3) = { X = a.X - b.X; Y = a.Y - b.Y; Z = a.Z - b.Z }
    static member (*)(k: float, a: Vec3) = { X = k * a.X; Y = k * a.Y; Z = k * a.Z }

module Vec3 =
    let create x y z = { X = x; Y = y; Z = z }
    let zero = create 0.0 0.0 0.0

    let dot (a: Vec3) (b: Vec3) = a.X * b.X + a.Y * b.Y + a.Z * b.Z

    let cross (a: Vec3) (b: Vec3) =
        create (a.Y * b.Z - a.Z * b.Y) (a.Z * b.X - a.X * b.Z) (a.X * b.Y - a.Y * b.X)

    let length (a: Vec3) = sqrt (dot a a)

    let isFinite (a: Vec3) =
        System.Double.IsFinite a.X && System.Double.IsFinite a.Y && System.Double.IsFinite a.Z

type ContourKind =
    | Circle
    | Flat
    | UCup of depth: float
    | VFold of depth: float

type CrossSection =
    { P: Vec3
      H: Vec3
      L: Vec3
      U: Vec3
      Width: float
      ScaleL: float
      ScaleU: float
      Color: int
      Contour: ContourKind }

type GeneralizedCylinder =
    { Sides: int
      Sections: CrossSection list }

type PatchNeighbors =
    { TopLeft: string option // AL
      Top: string option // A
      TopRight: string option // AR
      Left: string option // L
      Right: string option // R
      BottomLeft: string option // BL
      Bottom: string option // B
      BottomRight: string option // BR
    }

type BezierPatch =
    { Name: string
      Neighbors: PatchNeighbors
      /// 4x4 grid indexed [i * 4 + j] where i = 0..3 (s/u axis), j = 0..3 (t/v axis),
      /// transformed into canonical surface space (Y = HEADING, X = HEADING x UP, Z = X x Y).
      ControlPoints: Vec3[] }

type BezierSurface =
    { Name: string
      DefaultDivisions: int * int
      EndPoint: Vec3
      Patches: BezierPatch[] }

type SurfaceInstance =
    { Surface: BezierSurface
      Origin: Vec3
      /// Right-handed basis columns: X = -L_t, Y = H_t, Z = U_t.
      AxisX: Vec3
      AxisY: Vec3
      AxisZ: Vec3
      Scale: Vec3
      Color: int }

type Primitive =
    | Segment of p0: Vec3 * p1: Vec3 * width: float * color: int
    | Polygon of vertices: Vec3 list * color: int
    | GeneralizedCylinder of GeneralizedCylinder
    | Surface of SurfaceInstance

type Geometry = Primitive list
