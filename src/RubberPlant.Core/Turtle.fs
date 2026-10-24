/// Turtle interpretation (§5): turns a drawing string into geometry.
module RubberPlant.Turtle

open RubberPlant.Model
open RubberPlant.LString
open RubberPlant.Geometry
open RubberPlant.Builtins

type State =
    { P: Vec3
      /// Heading.
      H: Vec3
      /// Left.
      L: Vec3
      /// Up. Always H × L.
      U: Vec3
      Width: float
      Color: int }

/// Facing +Y; looking down −U onto the XY plane, `+` turns counter-clockwise (§5.1).
let initialState (settings: Settings) =
    { P = Vec3.zero
      H = Vec3.create 0.0 1.0 0.0
      L = Vec3.create -1.0 0.0 0.0
      U = Vec3.create 0.0 0.0 1.0
      Width = settings.Width
      Color = 0 }

// Rotations. ABOP §1.5 gives R_U, R_L, R_H; applied with H, L, U as the rows
// of the frame, they give the behavior the symbol table names: R_U(a) turns
// H toward L (left), R_L(a) pitches H toward −U (down), R_H(a) rolls L
// toward −U (left).

/// R_U(a): turn left by a degrees.
let turn (a: float) (t: State) =
    let c, s = cosDeg a, sinDeg a

    { t with
        H = c * t.H + s * t.L
        L = c * t.L - s * t.H }

/// R_L(a): pitch down by a degrees.
let pitch (a: float) (t: State) =
    let c, s = cosDeg a, sinDeg a

    { t with
        H = c * t.H - s * t.U
        U = c * t.U + s * t.H }

/// R_H(a): roll left by a degrees.
let roll (a: float) (t: State) =
    let c, s = cosDeg a, sinDeg a

    { t with
        L = c * t.L - s * t.U
        U = c * t.U + s * t.L }

/// Symbols the turtle understands, and how many parameters each one uses.
let private turtleArity =
    Map.ofList
        [ "F", 1
          "f", 1
          "+", 1
          "-", 1
          "&", 1
          "^", 1
          "\\", 1
          "/", 1
          "|", 0
          "{", 0
          "}", 0
          "!", 1
          "'", 1 ]

exception private TurtleError of string

let private isFinite (v: Vec3) =
    System.Double.IsFinite v.X && System.Double.IsFinite v.Y && System.Double.IsFinite v.Z

/// Interprets a drawing string. Returns the geometry and any warnings, or an
/// error if the turtle meets a non-finite value or an invalid width: backends
/// must only ever receive finite geometry.
let interpret (settings: Settings) (drawing: LString) : Result<Geometry * Diagnostic list, Diagnostic list> =
    let out = ResizeArray<Primitive>()
    let warnings = ResizeArray<Diagnostic>()
    let warned = System.Collections.Generic.HashSet<struct (string * int)>()
    let stack = System.Collections.Generic.Stack<State>()
    // One vertex list per open `{` (§5.3).
    let polygons = System.Collections.Generic.Stack<ResizeArray<Vec3>>()
    let mutable t = initialState settings
    let mutable unmatchedClose = false

    let move (d: float) draw =
        let p1 = t.P + d * t.H

        if not (isFinite p1) then
            raise (TurtleError "the turtle's position is no longer finite (overflow)")

        if draw then
            out.Add(Segment(t.P, p1, t.Width, t.Color))

        if polygons.Count > 0 then
            polygons.Peek().Add p1

        t <- { t with P = p1 }

    let run () =
        for sym in drawing do
            match sym with
            | Push -> stack.Push t
            | Pop -> if stack.Count > 0 then t <- stack.Pop()
            | Mod(name, args) ->
                match turtleArity.TryFind name with
                | None -> () // not a turtle symbol: ignored (§5.2)
                | Some arity ->
                    if args.Length > arity && warned.Add(struct (name, args.Length)) then
                        warnings.Add(
                            Diagnostic.warning
                                Codes.ExtraTurtleParameters
                                None
                                $"the turtle ignores the extra parameters of `%s{name}` (it uses %d{arity}, got %d{args.Length})"
                        )

                    if args.Length > 0 && not (System.Double.IsFinite args[0]) then
                        raise (TurtleError $"`%s{format [| sym |]}`: turtle parameters must be finite")

                    let arg dflt =
                        if args.Length > 0 then args[0] else dflt

                    match name with
                    | "F" -> move (arg settings.Step) true
                    | "f" -> move (arg settings.Step) false
                    | "+" -> t <- turn (arg settings.Angle) t
                    | "-" -> t <- turn -(arg settings.Angle) t
                    | "&" -> t <- pitch (arg settings.Angle) t
                    | "^" -> t <- pitch -(arg settings.Angle) t
                    | "\\" -> t <- roll (arg settings.Angle) t
                    | "/" -> t <- roll -(arg settings.Angle) t
                    | "|" -> t <- turn 180.0 t
                    | "!" ->
                        let w =
                            if args.Length > 0 then
                                if args[0] < 0.0 then
                                    raise (TurtleError $"`%s{format [| sym |]}`: widths must be non-negative")

                                args[0]
                            else
                                t.Width * (1.0 - settings.Taper / 100.0)

                        t <- { t with Width = w }
                    | "'" ->
                        // Truncate toward zero, saturating at the int range.
                        let c =
                            if args.Length > 0 then
                                int (max -2147483648.0 (min 2147483647.0 (truncate args[0])))
                            else
                                t.Color + 1

                        t <- { t with Color = c }
                    | "{" -> polygons.Push(ResizeArray [ t.P ])
                    | "}" ->
                        if polygons.Count > 0 then
                            out.Add(Polygon(List.ofSeq (polygons.Pop()), t.Color))
                        else
                            unmatchedClose <- true
                    | _ -> ()

    try
        run ()

        if unmatchedClose then
            warnings.Add(Diagnostic.warning Codes.UnbalancedPolygon None "`}` without a matching `{`; ignored")

        if polygons.Count > 0 then
            warnings.Add(Diagnostic.warning Codes.UnbalancedPolygon None "`{` without a matching `}`; polygon dropped")

        Ok(List.ofSeq out, List.ofSeq warnings)
    with TurtleError msg ->
        Error [ Diagnostic.error Codes.InvalidGeometry None msg ]
