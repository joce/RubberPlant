module RubberPlant.Tests.TurtleTests

open Expecto
open RubberPlant
open RubberPlant.Geometry
open RubberPlant.Model
open RubberPlant.Tests.TestUtil
open RubberPlant.Tests.CheckerTests

let draw (src: string) : Geometry * Diagnostic list =
    let m, _ = compileOk src

    match Pipeline.run m with
    | Ok o -> o.Geometry, o.Warnings
    | Error ds -> failtestf "failed:\n%s" (formatDiags ds)

let drawError (src: string) : Diagnostic =
    let m, _ = compileOk src

    match Pipeline.run m with
    | Ok o -> failtestf "expected an error, got %A" o.Geometry
    | Error [ d ] -> d
    | Error ds -> failtestf "expected one error, got:\n%s" (formatDiags ds)

let v x y z = Vec3.create x y z

let close (a: Vec3) (b: Vec3) =
    abs (a.X - b.X) < 1e-9 && abs (a.Y - b.Y) < 1e-9 && abs (a.Z - b.Z) < 1e-9

let segments (g: Geometry) =
    g
    |> List.choose (function
        | Segment(p0, p1, _, _) -> Some(p0, p1)
        | _ -> None)

let expectPath (g: Geometry) (points: Vec3 list) =
    let segs = segments g
    let actual = (segs |> List.map fst) @ [ snd (List.last segs) ]

    if List.length actual <> List.length points || not (List.forall2 close actual points) then
        failtestf "expected %A, got %A" points actual

let dot (a: Vec3) (b: Vec3) = a.X * b.X + a.Y * b.Y + a.Z * b.Z

let cross (a: Vec3) (b: Vec3) =
    v (a.Y * b.Z - a.Z * b.Y) (a.Z * b.X - a.X * b.Z) (a.X * b.Y - a.Y * b.X)

type Rotation =
    | Turn of float
    | Pitch of float
    | Roll of float

let frameStaysOrthonormal (rotations: Rotation list) =
    let angle =
        function
        | Turn a
        | Pitch a
        | Roll a -> a

    let finite =
        rotations |> List.forall (fun r -> System.Double.IsFinite(angle r))

    let apply t r =
        match r with
        | Turn a -> Turtle.turn a t
        | Pitch a -> Turtle.pitch a t
        | Roll a -> Turtle.roll a t

    let t = rotations |> List.fold apply (Turtle.initialState defaultSettings)
    let eps = 1e-6
    let unit (a: Vec3) = abs (dot a a - 1.0) < eps
    let hl = cross t.H t.L

    not finite
    || (unit t.H
        && unit t.L
        && unit t.U
        && abs (dot t.H t.L) < eps
        && abs (dot t.H t.U) < eps
        && abs (dot t.L t.U) < eps
        && abs (hl.X - t.U.X) < eps
        && abs (hl.Y - t.U.Y) < eps
        && abs (hl.Z - t.U.Z) < eps)

[<Tests>]
let tests =
    testList
        "Turtle"
        [ test "square: F-F-F-F" {
              let g, _ = draw "#axiom F-F-F-F"
              expectPath g [ v 0 0 0; v 0 1 0; v 1 1 0; v 1 0 0; v 0 0 0 ]
          }

          test "+ turns counter-clockwise" {
              let g, _ = draw "#axiom +F"
              expectPath g [ v 0 0 0; v -1 0 0 ]
          }

          test "parameters override step and angle" {
              let g, _ = draw "#axiom F(2) +(90) F(3)"
              expectPath g [ v 0 0 0; v 0 2 0; v -3 2 0 ]
          }

          test "settings: step and angle" {
              let g, _ = draw "#step 2\n#angle 45\n#axiom + + F"
              expectPath g [ v 0 0 0; v -2 0 0 ]
          }

          test "f moves without drawing" {
              let g, _ = draw "#axiom f F"
              expectPath g [ v 0 1 0; v 0 2 0 ]
          }

          test "| turns around" {
              let g, _ = draw "#axiom F | F"
              expectPath g [ v 0 0 0; v 0 1 0; v 0 0 0 ]
          }

          test "& pitches down, toward -U" {
              let g, _ = draw "#axiom &F"
              expectPath g [ v 0 0 0; v 0 0 -1 ]
          }

          test "^ pitches up" {
              let g, _ = draw "#axiom ^F"
              expectPath g [ v 0 0 0; v 0 0 1 ]
          }

          test "\\ rolls left: L goes toward -U" {
              // After rolling left 90°, L = -U0 = (0,0,-1); turning left then heads there.
              let g, _ = draw "#axiom \\ + F"
              expectPath g [ v 0 0 0; v 0 0 -1 ]
          }

          test "/ rolls right" {
              let g, _ = draw "#axiom / + F"
              expectPath g [ v 0 0 0; v 0 0 1 ]
          }

          test "brackets save and restore the state" {
              let g, _ = draw "#axiom F[+F]F"

              match segments g with
              | [ (_, a); (b0, b); (c0, c) ] ->
                  Expect.isTrue (close a (v 0 1 0)) ""
                  Expect.isTrue (close b0 (v 0 1 0) && close b (v -1 1 0)) "branch"
                  Expect.isTrue (close c0 (v 0 1 0) && close c (v 0 2 0)) "back on the main axis"
              | s -> failtestf "unexpected %A" s
          }

          test "other modules are ignored" {
              let g, _ = draw "#axiom X F Y(1) Z"
              Expect.equal (List.length g) 1 ""
          }

          test "! tapers width; !(w) sets it" {
              let g, _ = draw "#width 10\n#axiom F ! F ! ! F !(3) F"

              let widths =
                  g
                  |> List.map (function
                      | Segment(_, _, w, _) -> w
                      | _ -> nan)

              Expect.sequenceEqual (widths |> List.map (fun w -> System.Math.Round(w, 9))) [ 10.0; 7.0; 3.43; 3.0 ] ""
          }

          test "#taper changes the decrement" {
              let g, _ = draw "#taper 50\n#axiom ! F"

              match g with
              | [ Segment(_, _, w, _) ] -> Expect.equal w 0.5 ""
              | _ -> failtest "one segment expected"
          }

          test "' increments the color; '(c) sets it" {
              let g, _ = draw "#axiom F ' F '(5) F"

              let colors =
                  g
                  |> List.map (function
                      | Segment(_, _, _, c) -> c
                      | _ -> -1)

              Expect.equal colors [ 0; 1; 5 ] ""
          }

          test "polygon from f moves" {
              let g, _ = draw "#axiom {f+f+f}"

              match g with
              | [ Polygon(vs, 0) ] ->
                  Expect.equal (List.length vs) 4 ""
                  Expect.isTrue (List.forall2 close vs [ v 0 0 0; v 0 1 0; v -1 1 0; v -1 0 0 ]) $"%A{vs}"
              | _ -> failtestf "unexpected %A" g
          }

          test "F inside a polygon draws and adds a vertex" {
              let g, _ = draw "#axiom {F+F}"

              match g with
              | [ Segment _; Segment _; Polygon(vs, _) ] -> Expect.equal (List.length vs) 3 ""
              | _ -> failtestf "unexpected %A" g
          }

          test "brackets inside a polygon keep the vertex list" {
              let g, _ = draw "#axiom {f[+f]f}"

              match g with
              | [ Polygon(vs, _) ] -> Expect.equal (List.length vs) 4 ""
              | _ -> failtestf "unexpected %A" g
          }

          test "nested polygons" {
              let g, _ = draw "#axiom {f{f+f}f}"

              match g with
              | [ Polygon(inner, _); Polygon(outer, _) ] ->
                  Expect.equal (List.length inner) 3 ""
                  Expect.equal (List.length outer) 3 ""
              | _ -> failtestf "unexpected %A" g
          }

          test "extra parameters: one warning per module shape" {
              let _, warnings = draw "#axiom F(1,2) F(1,2) F(3,4)"

              match warnings with
              | [ w ] -> Expect.equal w.Code Codes.ExtraTurtleParameters ""
              | ws -> failtestf "unexpected:\n%s" (formatDiags ws)
          }

          test "3D Hilbert curve (Fig 1.19) visits every point of a 4x4x4 cube" {
              let src =
                  System.IO.File.ReadAllText(System.IO.Path.Combine(__SOURCE_DIRECTORY__, "../../examples/hilbert-3d.rp"))

              let g, _ = draw src

              let points =
                  segments g
                  |> List.collect (fun (a, b) -> [ a; b ])
                  |> List.map (fun p -> round p.X, round p.Y, round p.Z)
                  |> List.distinct

              let extent f =
                  points |> List.map f |> List.distinct |> List.length

              Expect.equal (List.length g) 63 "segments"
              Expect.equal (List.length points) 64 "distinct points: the curve never revisits a point"
              Expect.equal (extent (fun (x, _, _) -> x), extent (fun (_, y, _) -> y), extent (fun (_, _, z) -> z)) (4, 4, 4) ""
          }

          test "non-finite turtle parameter" {
              Expect.equal (drawError "#axiom +(log(0))F").Code Codes.InvalidGeometry ""
          }

          test "position overflow" {
              Expect.equal (drawError "#axiom F(1e308) F(1e308)").Code Codes.InvalidGeometry ""
          }

          test "negative width" {
              Expect.equal (drawError "#axiom !(-1) F").Code Codes.InvalidGeometry ""
          }

          test "huge angles keep the frame orthonormal" {
              Expect.floatClose Accuracy.high (Builtins.cosDeg 1e20) (Builtins.cosDeg (1e20 % 360.0)) ""

              let g, _ = draw "#angle 1e20\n#axiom +F"

              match segments g with
              | [ (p0, p1) ] ->
                  let d = p1 - p0
                  Expect.floatClose Accuracy.high (dot d d) 1.0 "unit step"
              | s -> failtestf "unexpected %A" s
          }

          test "huge color index saturates" {
              let g, _ = draw "#axiom '(1e20) F"

              match g with
              | [ Segment(_, _, _, c) ] -> Expect.equal c System.Int32.MaxValue ""
              | _ -> failtest "one segment expected"
          }

          test "extra-parameter warnings: per name and arity" {
              let _, warnings = draw "#axiom F(1,2) F(1,2,3) |(1) |(1)"
              Expect.equal (warnings |> List.map _.Code) (List.replicate 3 Codes.ExtraTurtleParameters) ""
          }

          test "unbalanced polygon warnings" {
              let _, warnings = draw "#axiom } {f"
              Expect.equal (warnings |> List.map _.Code) [ Codes.UnbalancedPolygon; Codes.UnbalancedPolygon ] ""
          }

          testProperty "rotations keep the frame orthonormal and right-handed" frameStaysOrthonormal ]
