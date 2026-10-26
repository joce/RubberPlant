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

type Primitive =
    | Segment of p0: Vec3 * p1: Vec3 * width: float * color: int
    | Polygon of vertices: Vec3 list * color: int

type Geometry = Primitive list
