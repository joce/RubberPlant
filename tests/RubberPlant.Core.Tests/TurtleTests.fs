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

let private polygonsOf (g: Geometry) =
    g
    |> List.choose (function
        | Polygon(vs, _) -> Some vs
        | _ -> None)

let private expectVertices (expected: Vec3 list) (actual: Vec3 list) =
    if List.length actual <> List.length expected || not (List.forall2 close actual expected) then
        failtestf "expected vertices %A, got %A" expected actual

let private isOrthonormal (t: Turtle.State) =
    let eps = 1e-6
    let unit (a: Vec3) = abs (dot a a - 1.0) < eps
    let hl = cross t.H t.L

    unit t.H
    && unit t.L
    && unit t.U
    && abs (dot t.H t.L) < eps
    && abs (dot t.H t.U) < eps
    && abs (dot t.L t.U) < eps
    && abs (hl.X - t.U.X) < eps
    && abs (hl.Y - t.U.Y) < eps
    && abs (hl.Z - t.U.Z) < eps

let private start = Turtle.initialState defaultSettings
let private s30 = sin (System.Math.PI / 6.0)
let private c30 = cos (System.Math.PI / 6.0)

/// With T = (1, 0, 0) and the turtle facing +Y, |H × T| = 1, so sin α = e.
let private sideways e = $"settings {{ tropism = (1, 0, 0); elasticity = %g{e}; }}\n"

let bendKeepsFrameOrthonormal (rotations: Rotation list) (tx: float, ty: float, tz: float) (e: float) =
    let apply t r =
        match r with
        | Turn a -> Turtle.turn a t
        | Pitch a -> Turtle.pitch a t
        | Roll a -> Turtle.roll a t

    let angle =
        function
        | Turn a
        | Pitch a
        | Roll a -> a

    let finite = List.forall System.Double.IsFinite [ tx; ty; tz; e ]
    let small = List.forall (fun x -> abs x < 1e6) [ tx; ty; tz ]

    if not finite || not small || rotations |> List.exists (angle >> System.Double.IsFinite >> not) then
        true
    else
        let len = sqrt (tx * tx + ty * ty + tz * tz)

        if len < 1e-3 then
            true
        else
            let tHat = (1.0 / len) * v tx ty tz
            let t = rotations |> List.fold apply start
            let bent = List.fold (fun t _ -> Turtle.bend tHat e t) t [ 1..10 ]
            isOrthonormal bent

let v02 =
    testList
        "v0.2"
        [ testList
              "polygons"
              [ test "{ starts an empty polygon" {
                    match draw "axiom: {f+f+f};" |> fst |> polygonsOf with
                    | [ vs ] -> expectVertices [ v 0 1 0; v -1 1 0; v -1 0 0 ] vs
                    | ps -> failtestf "unexpected %A" ps
                }

                test ". records the current position, repeats included" {
                    match draw "axiom: { . f . };" |> fst |> polygonsOf with
                    | [ vs ] -> expectVertices [ v 0 0 0; v 0 1 0; v 0 1 0 ] vs
                    | ps -> failtestf "unexpected %A" ps
                }

                test ". goes to the innermost polygon" {
                    match draw "axiom: { . { f . } f };" |> fst |> polygonsOf with
                    | [ inner; outer ] ->
                        expectVertices [ v 0 1 0; v 0 1 0 ] inner
                        expectVertices [ v 0 0 0; v 0 2 0 ] outer
                    | ps -> failtestf "unexpected %A" ps
                }

                test ". outside a polygon: one warning per drawing" {
                    let g, warnings = draw "axiom: . F . [ . ];"
                    Expect.equal (List.length g) 1 "the segment only"
                    Expect.equal (warnings |> List.map _.Code) [ Codes.VertexOutsidePolygon ] ""
                }

                test ". ignores parameters, with a warning" {
                    let g, warnings = draw "axiom: { .(5) f };"
                    Expect.equal (List.length (polygonsOf g)) 1 ""
                    Expect.equal (warnings |> List.map _.Code) [ Codes.ExtraTurtleParameters ] ""
                }

                test "G draws but records no vertex" {
                    let g, _ = draw "axiom: { . G + G(2) . };"
                    expectPath g [ v 0 0 0; v 0 1 0; v -2 1 0 ]

                    match polygonsOf g with
                    | [ vs ] -> expectVertices [ v 0 0 0; v -2 1 0 ] vs
                    | ps -> failtestf "unexpected %A" ps
                }

                test "G uses the step setting" {
                    let g, _ = draw "settings { step = 3; }\naxiom: G;"
                    expectPath g [ v 0 0 0; v 0 3 0 ]
                }

                test "G is predefined by the turtle" {
                    expectError Codes.NamespaceClash 1 "let G = 1;\naxiom: F;"
                } ]

          testList
              "$ roll to horizontal"
              [ test "levels L and points U up" {
                    let t = start |> Turtle.pitch 90.0 |> Turtle.roll 30.0 |> Turtle.rollToHorizontal
                    Expect.isTrue (close t.H (v 0 0 -1)) $"H unchanged: %A{t.H}"
                    Expect.isTrue (close t.L (v -1 0 0)) $"L = V x H / |V x H|: %A{t.L}"
                    Expect.isTrue (close t.U (v 0 1 0)) $"U = H x L: %A{t.U}"
                }

                test "oblique heading" {
                    let before = start |> Turtle.pitch 45.0 |> Turtle.roll 77.0 |> Turtle.turn 10.0
                    let t = Turtle.rollToHorizontal before
                    Expect.isTrue (close t.H before.H) "H unchanged"
                    Expect.floatClose Accuracy.veryHigh t.L.Y 0.0 "L is horizontal"
                    Expect.isGreaterThan t.U.Y 0.0 "U points up"
                    Expect.isTrue (isOrthonormal t) "orthonormal, right-handed"
                }

                test "a turn after $ stays in the horizontal plane" {
                    let g, _ = draw "axiom: &(90) /(30) $ + F;"
                    expectPath g [ v 0 0 0; v -1 0 0 ]
                }

                test "does nothing when H is vertical (cpfg)" {
                    let rolled = start |> Turtle.roll 30.0
                    Expect.equal (Turtle.rollToHorizontal rolled) rolled "the roll is kept"
                    let down = start |> Turtle.turn 180.0 |> Turtle.roll 30.0
                    Expect.equal (Turtle.rollToHorizontal down) down "heading down"
                    let withDollar, _ = draw "axiom: /(30) $ & F;"
                    let without, _ = draw "axiom: /(30) & F;"
                    Expect.equal withDollar without ""
                } ]

          testList
              "tropism"
              [ test "the bend: sin \u03b1 = e |H x T|, toward T" {
                    let g, _ = draw (sideways 0.5 + "axiom: F F;")
                    expectPath g [ v 0 0 0; v 0 1 0; v s30 (1.0 + c30) 0 ]
                }

                test "the whole frame turns around H x T" {
                    let t = Turtle.bend (v 1 0 0) 0.5 start
                    Expect.isTrue (close t.H (v s30 c30 0)) $"H: %A{t.H}"
                    Expect.isTrue (close t.L (v -c30 s30 0)) $"L: %A{t.L}"
                    Expect.isTrue (close t.U (v 0 0 1)) $"U is along the axis and doesn't move: %A{t.U}"
                }

                test "negative elasticity bends away from T" {
                    let g, _ = draw (sideways -0.5 + "axiom: F F;")
                    expectPath g [ v 0 0 0; v 0 1 0; v -s30 (1.0 + c30) 0 ]
                }

                test "the sine is clamped" {
                    let g, _ = draw (sideways 5.0 + "axiom: F F;")
                    expectPath g [ v 0 0 0; v 0 1 0; v 1 1 0 ]
                }

                test "T is normalized: only its direction matters" {
                    let unitT, _ = draw (sideways 0.3 + "axiom: F F F;")
                    let longT, _ = draw "settings { tropism = (7, 0, 0); elasticity = 0.3; }\naxiom: F F F;"
                    let hugeT, _ = draw "settings { tropism = (1e300, 0, 0); elasticity = 0.3; }\naxiom: F F F;"
                    let tinyT, _ = draw "settings { tropism = (1e-310, 0, 0); elasticity = 0.3; }\naxiom: F F F;"
                    let smallestT, _ = draw "settings { tropism = (5e-324, 0, 0); elasticity = 0.3; }\naxiom: F F F;"
                    let path g = (segments g |> List.map fst) @ [ snd (List.last (segments g)) ]
                    expectPath longT (path unitT)
                    expectPath hugeT (path unitT)
                    expectPath tinyT (path unitT) // subnormal: 1/m would overflow
                    expectPath smallestT (path unitT)
                }

                test "elasticity 0 disables it" {
                    let g, _ = draw "settings { tropism = (1, 0, 0); }\naxiom: F F;"
                    expectPath g [ v 0 0 0; v 0 1 0; v 0 2 0 ]
                }

                test "no bend when H is parallel to T" {
                    let g, _ = draw "settings { elasticity = 0.5; }\naxiom: F F;"
                    expectPath g [ v 0 0 0; v 0 1 0; v 0 2 0 ] // a vertical stem, against gravity
                    let along, _ = draw "settings { tropism = (0, 1, 0); elasticity = 0.5; }\naxiom: F F;"
                    expectPath along [ v 0 0 0; v 0 1 0; v 0 2 0 ]
                }

                test "f and G bend too" {
                    let g, _ = draw (sideways 0.5 + "axiom: f F;")
                    expectPath g [ v 0 1 0; v s30 (1.0 + c30) 0 ]
                    let g2, _ = draw (sideways 0.5 + "axiom: G F;")
                    expectPath g2 [ v 0 0 0; v 0 1 0; v s30 (1.0 + c30) 0 ]
                }

                test "turns don't bend" {
                    let g, _ = draw (sideways 0.5 + "axiom: + - & ^ / \\ | | $ F;")
                    expectPath g [ v 0 0 0; v 0 1 0 ]
                }

                test "the segment and the vertex come before the bend" {
                    let g, _ = draw (sideways 0.5 + "axiom: { . F F };")
                    expectPath g [ v 0 0 0; v 0 1 0; v s30 (1.0 + c30) 0 ]

                    match polygonsOf g with
                    | [ vs ] -> expectVertices [ v 0 0 0; v 0 1 0; v s30 (1.0 + c30) 0 ] vs
                    | ps -> failtestf "unexpected %A" ps
                }

                test "gravity bends a horizontal branch down" {
                    let g, _ = draw "settings { elasticity = 0.2; }\naxiom: + F F;"

                    match segments g with
                    | [ (_, a); (_, b) ] ->
                        Expect.isTrue (close a (v -1 0 0)) ""
                        Expect.isLessThan b.Y 0.0 "the second segment goes down"
                        Expect.floatClose Accuracy.veryHigh b.Y -0.2 "sin \u03b1 = 0.2"
                    | s -> failtestf "unexpected %A" s
                }

                test "invalid overrides are runtime errors" {
                    let m, _ = compileOk "axiom: F;"

                    for s in
                        [ { m.Settings with Tropism = Vec3.zero }
                          { m.Settings with Tropism = v nan 0 0 }
                          { m.Settings with Elasticity = infinity } ] do
                        match Pipeline.run { m with Settings = s } with
                        | Error [ d ] -> Expect.equal d.Code Codes.InvalidGeometry ""
                        | r -> failtestf "expected an error, got %A" r
                }

                testProperty "bending keeps the frame orthonormal and right-handed" bendKeepsFrameOrthonormal ] ]

[<Tests>]
let tests =
    testList
        "Turtle"
        [ test "square: F-F-F-F" {
              let g, _ = draw "axiom: F-F-F-F;"
              expectPath g [ v 0 0 0; v 0 1 0; v 1 1 0; v 1 0 0; v 0 0 0 ]
          }

          test "+ turns counter-clockwise" {
              let g, _ = draw "axiom: +F;"
              expectPath g [ v 0 0 0; v -1 0 0 ]
          }

          test "parameters override step and angle" {
              let g, _ = draw "axiom: F(2) +(90) F(3);"
              expectPath g [ v 0 0 0; v 0 2 0; v -3 2 0 ]
          }

          test "settings: step and angle" {
              let g, _ = draw "settings { step = 2; angle = 45; }\naxiom: + + F;"
              expectPath g [ v 0 0 0; v -2 0 0 ]
          }

          test "f moves without drawing" {
              let g, _ = draw "axiom: f F;"
              expectPath g [ v 0 1 0; v 0 2 0 ]
          }

          test "| turns around" {
              let g, _ = draw "axiom: F | F;"
              expectPath g [ v 0 0 0; v 0 1 0; v 0 0 0 ]
          }

          test "& pitches down, toward -U" {
              let g, _ = draw "axiom: &F;"
              expectPath g [ v 0 0 0; v 0 0 -1 ]
          }

          test "^ pitches up" {
              let g, _ = draw "axiom: ^F;"
              expectPath g [ v 0 0 0; v 0 0 1 ]
          }

          test "\\ rolls left: L goes toward -U" {
              // After rolling left 90°, L = -U0 = (0,0,-1); turning left then heads there.
              let g, _ = draw "axiom: \\ + F;"
              expectPath g [ v 0 0 0; v 0 0 -1 ]
          }

          test "/ rolls right" {
              let g, _ = draw "axiom: / + F;"
              expectPath g [ v 0 0 0; v 0 0 1 ]
          }

          test "brackets save and restore the state" {
              let g, _ = draw "axiom: F[+F]F;"

              match segments g with
              | [ (_, a); (b0, b); (c0, c) ] ->
                  Expect.isTrue (close a (v 0 1 0)) ""
                  Expect.isTrue (close b0 (v 0 1 0) && close b (v -1 1 0)) "branch"
                  Expect.isTrue (close c0 (v 0 1 0) && close c (v 0 2 0)) "back on the main axis"
              | s -> failtestf "unexpected %A" s
          }

          test "other modules are ignored" {
              let g, _ = draw "axiom: X F Y(1) Z;"
              Expect.equal (List.length g) 1 ""
          }

          test "! tapers width; !(w) sets it" {
              let g, _ = draw "settings { width = 10; }\naxiom: F ! F ! ! F !(3) F;"

              let widths =
                  g
                  |> List.map (function
                      | Segment(_, _, w, _) -> w
                      | _ -> nan)

              Expect.sequenceEqual (widths |> List.map (fun w -> System.Math.Round(w, 9))) [ 10.0; 7.0; 3.43; 3.0 ] ""
          }

          test "taper changes the decrement" {
              let g, _ = draw "settings { taper = 50; }\naxiom: ! F;"

              match g with
              | [ Segment(_, _, w, _) ] -> Expect.equal w 0.5 ""
              | _ -> failtest "one segment expected"
          }

          test "' increments the color; '(c) sets it" {
              let g, _ = draw "axiom: F ' F '(5) F;"

              let colors =
                  g
                  |> List.map (function
                      | Segment(_, _, _, c) -> c
                      | _ -> -1)

              Expect.equal colors [ 0; 1; 5 ] ""
          }

          test "polygon from f moves" {
              let g, _ = draw "axiom: {.f+f+f};"

              match g with
              | [ Polygon(vs, 0) ] ->
                  Expect.equal (List.length vs) 4 ""
                  Expect.isTrue (List.forall2 close vs [ v 0 0 0; v 0 1 0; v -1 1 0; v -1 0 0 ]) $"%A{vs}"
              | _ -> failtestf "unexpected %A" g
          }

          test "F inside a polygon draws and adds a vertex" {
              let g, _ = draw "axiom: {.F+F};"

              match g with
              | [ Segment _; Segment _; Polygon(vs, _) ] -> Expect.equal (List.length vs) 3 ""
              | _ -> failtestf "unexpected %A" g
          }

          test "brackets inside a polygon keep the vertex list" {
              let g, _ = draw "axiom: {.f[+f]f};"

              match g with
              | [ Polygon(vs, _) ] -> Expect.equal (List.length vs) 4 ""
              | _ -> failtestf "unexpected %A" g
          }

          test "nested polygons" {
              let g, _ = draw "axiom: {.f{.f+f}f};"

              match g with
              | [ Polygon(inner, _); Polygon(outer, _) ] ->
                  Expect.equal (List.length inner) 3 ""
                  Expect.equal (List.length outer) 3 ""
              | _ -> failtestf "unexpected %A" g
          }

          test "extra parameters: one warning per module shape" {
              let _, warnings = draw "axiom: F(1,2) F(1,2) F(3,4);"

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
              Expect.equal (drawError "axiom: +(log(0))F;").Code Codes.InvalidGeometry ""
          }

          test "position overflow" {
              Expect.equal (drawError "axiom: F(1e308) F(1e308);").Code Codes.InvalidGeometry ""
          }

          test "negative width" {
              Expect.equal (drawError "axiom: !(-1) F;").Code Codes.InvalidGeometry ""
          }

          test "non-finite default is checked when consumed" {
              // The pop would hide the NaN frame; the consumed angle must still be finite.
              Expect.equal (drawError "settings { angle = exp(1000); }\naxiom: [+]F;").Code Codes.InvalidGeometry ""
              Expect.equal (drawError "settings { step = exp(1000); }\naxiom: F;").Code Codes.InvalidGeometry ""
          }

          test "unused parameters may be non-finite" {
              let g, warnings = draw "axiom: |(log(0)) F(1, log(0));"
              Expect.equal (List.length g) 1 ""
              Expect.equal (warnings |> List.map _.Code) [ Codes.ExtraTurtleParameters; Codes.ExtraTurtleParameters ] ""
          }

          test "initial width from an override is checked" {
              let m, _ = compileOk "axiom: F;"
              let m = { m with Settings = { m.Settings with Width = -1.0 } }

              match Pipeline.run m with
              | Error [ d ] -> Expect.equal d.Code Codes.InvalidGeometry ""
              | r -> failtestf "expected an error, got %A" r
          }

          test "huge angles keep the frame orthonormal" {
              Expect.floatClose Accuracy.high (Builtins.cosDeg 1e20) (Builtins.cosDeg (1e20 % 360.0)) ""

              let g, _ = draw "settings { angle = 1e20; }\naxiom: +F;"

              match segments g with
              | [ (p0, p1) ] ->
                  let d = p1 - p0
                  Expect.floatClose Accuracy.high (dot d d) 1.0 "unit step"
              | s -> failtestf "unexpected %A" s
          }

          test "color indices: truncate toward zero, then saturate" {
              let colors src =
                  draw src
                  |> fst
                  |> List.map (function
                      | Segment(_, _, _, c) -> c
                      | _ -> failtest "segments only")

              Expect.equal (colors "axiom: '(1e20) F '(2147483648) F '(-1e20) F;") [ System.Int32.MaxValue; System.Int32.MaxValue; System.Int32.MinValue ] "saturation"
              Expect.equal (colors "axiom: '(-1.7) F '(2.9) F;") [ -1; 2 ] "truncation toward zero"
              Expect.equal (colors "axiom: '(2147483647) ' F;") [ System.Int32.MaxValue ] "bare ' saturates at the top"
          }

          test "extra-parameter warnings: per name and arity" {
              let _, warnings = draw "axiom: F(1,2) F(1,2,3) |(1) |(1);"
              Expect.equal (warnings |> List.map _.Code) (List.replicate 3 Codes.ExtraTurtleParameters) ""
          }

          test "unbalanced polygon warnings" {
              let _, warnings = draw "axiom: } {f;"
              Expect.equal (warnings |> List.map _.Code) [ Codes.UnbalancedPolygon; Codes.UnbalancedPolygon ] ""
          }

          testProperty "rotations keep the frame orthonormal and right-handed" frameStaysOrthonormal
          v02 ]
