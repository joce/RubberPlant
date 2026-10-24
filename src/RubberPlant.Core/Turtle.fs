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
        // The initial width can come from a CLI override, which the checker never saw.
        if not (System.Double.IsFinite settings.Width) || settings.Width < 0.0 then
            raise (
                TurtleError $"the initial width must be finite and non-negative, got %s{formatNumber settings.Width}"
            )

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

                    // The value a command consumes: its first parameter, or the
                    // setting it defaults to. Only consumed values must be finite;
                    // unused parameters are ignored (with the warning above).
                    let arg (setting: string) (dflt: float) =
                        let v = if args.Length > 0 then args[0] else dflt

                        if not (System.Double.IsFinite v) then
                            if args.Length > 0 then
                                raise (TurtleError $"`%s{format [| sym |]}`: turtle parameters must be finite")
                            else
                                raise (
                                    TurtleError $"`%s{name}` uses `#%s{setting}`, which is not finite (%s{formatNumber v})"
                                )

                        v

                    match name with
                    | "F" -> move (arg "step" settings.Step) true
                    | "f" -> move (arg "step" settings.Step) false
                    | "+" -> t <- turn (arg "angle" settings.Angle) t
                    | "-" -> t <- turn -(arg "angle" settings.Angle) t
                    | "&" -> t <- pitch (arg "angle" settings.Angle) t
                    | "^" -> t <- pitch -(arg "angle" settings.Angle) t
                    | "\\" -> t <- roll (arg "angle" settings.Angle) t
                    | "/" -> t <- roll -(arg "angle" settings.Angle) t
                    | "|" -> t <- turn 180.0 t
                    | "!" ->
                        let w =
                            if args.Length > 0 then
                                let w = arg "width" 0.0

                                if w < 0.0 then
                                    raise (TurtleError $"`%s{format [| sym |]}`: widths must be non-negative")

                                w
                            else
                                t.Width * (1.0 - settings.Taper / 100.0)

                        t <- { t with Width = w }
                    | "'" ->
                        // Color indices are 32-bit signed integers: `'(a)` truncates
                        // toward zero, then saturates; bare `'` saturates at the top.
                        let c =
                            if args.Length > 0 then
                                int (max -2147483648.0 (min 2147483647.0 (truncate (arg "color" 0.0))))
                            elif t.Color = System.Int32.MaxValue then
                                t.Color
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
