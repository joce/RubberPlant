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
      Color: int
      ScaleL: float
      ScaleU: float
      Contour: ContourKind
      ContourSides: int option }

/// Facing +Y; looking down −U onto the XY plane, `+` turns counter-clockwise (§5.1).
let initialState (settings: Settings) =
    { P = Vec3.zero
      H = Vec3.create 0.0 1.0 0.0
      L = Vec3.create -1.0 0.0 0.0
      U = Vec3.create 0.0 0.0 1.0
      Width = settings.Width
      Color = 0
      ScaleL = 1.0
      ScaleU = 1.0
      Contour = Circle
      ContourSides = None }

// Rotations. ABOP §1.5 gives R_U, R_L, R_H. R_U and R_L are applied with H, L, U as
// the rows of the frame: R_U(a) turns H toward L (left), R_L(a) pitches H toward −U
// (down). R_H follows cpfg, the software that made ABOP's figures, rather than the
// printed matrix: R_H(a) (roll left) turns L toward +U (language.md §5.1).

/// R_U(a): turn left by a degrees.
let turn (a: float) (t: State) =
    let struct (s, c) = sinCosDeg a

    { t with
        H = c * t.H + s * t.L
        L = c * t.L - s * t.H }

/// R_L(a): pitch down by a degrees.
let pitch (a: float) (t: State) =
    let struct (s, c) = sinCosDeg a

    { t with
        H = c * t.H - s * t.U
        U = c * t.U + s * t.H }

/// R_H(a): roll left by a degrees. Not ABOP's printed matrix: the roll turns
/// the way cpfg's does, so L goes toward +U (language.md 5.1, "Roll direction").
let roll (a: float) (t: State) =
    let struct (s, c) = sinCosDeg a

    { t with
        L = c * t.L + s * t.U
        U = c * t.U - s * t.L }

/// The direction opposite to gravity, used by `$` (§5.1).
let private vertical = Vec3.create 0.0 1.0 0.0

/// Below this, a cross product is treated as zero (§5.2).
let private parallelEpsilon = 1e-9

/// `$` (v0.2): roll around H so that L is horizontal and U points up
/// (ABOP p. 57). Does nothing when H is vertical, like cpfg (§5.2).
let rollToHorizontal (t: State) =
    let c = Vec3.cross vertical t.H
    let len = Vec3.length c

    if len < parallelEpsilon then
        t
    else
        let l = (1.0 / len) * c
        { t with L = l; U = Vec3.cross t.H l }

/// Tropism (v0.2, §5.5), computed as cpfg's `bend` does: turn H toward U by
/// e · (T · U), then toward L by e · (T · L), each a rotation in the plane of two
/// frame vectors, with the sine clamped to [-1, 1]. `tHat` is T normalized.
let bend (tHat: Vec3) (e: float) (t: State) =
    if e = 0.0 then
        t
    else
        let clamp x = max -1.0 (min 1.0 x)
        // Toward U by T's component along U (L unchanged).
        let s1 = clamp (e * Vec3.dot tHat t.U)
        let c1 = sqrt (1.0 - s1 * s1)
        let h1 = c1 * t.H + s1 * t.U
        let u1 = c1 * t.U - s1 * t.H
        // Then toward L by T's component along L (U unchanged).
        let s2 = clamp (e * Vec3.dot tHat t.L)
        let c2 = sqrt (1.0 - s2 * s2)

        { t with
            H = c2 * h1 + s2 * t.L
            L = c2 * t.L - s2 * h1
            U = u1 }

/// Symbols the turtle understands, and how many parameters each one uses.
let private turtleArity =
    Map.ofList
        [ "F", 1
          "f", 1
          "G", 1
          ".", 0
          "$", 0
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
          "'", 1
          "StartGC", 0
          "EndGC", 0
          "ContourCircle", 0
          "ContourFlat", 0
          "ContourCup", 1
          "ContourV", 1
          "ScaleContour", 2
          "ContourSides", 1 ]

type private GcVariant =
    | VarCircle
    | VarFlat
    | VarUCup
    | VarVFold

let private variantOf =
    function
    | Circle -> VarCircle
    | Flat -> VarFlat
    | UCup _ -> VarUCup
    | VFold _ -> VarVFold

type private GcBuilder =
    { OpenDepth: int
      IsForked: bool
      Sides: int
      ContourVariant: GcVariant
      Sections: ResizeArray<CrossSection> }

let private snapshotSection (t: State) : CrossSection =
    { P = t.P
      H = t.H
      L = t.L
      U = t.U
      Width = t.Width
      ScaleL = t.ScaleL
      ScaleU = t.ScaleU
      Color = t.Color
      Contour = t.Contour }

exception private TurtleError of code: string * message: string

let private invalidGeometry msg = raise (TurtleError(Codes.InvalidGeometry, msg))

let private isFinite = Vec3.isFinite

/// T / |T|, scaled first by the largest component so that neither huge nor
/// tiny (subnormal) components overflow: divide, don't multiply by 1/m,
/// which is infinite for a subnormal m. Zero for the zero vector (rejected
/// before use).
let private normalizeTropism (v: Vec3) =
    let m = max (abs v.X) (max (abs v.Y) (abs v.Z))

    if m = 0.0 then
        Vec3.zero
    else
        let w = Vec3.create (v.X / m) (v.Y / m) (v.Z / m)
        (1.0 / Vec3.length w) * w

type TurtleTrace =
    { /// Bracket stack depth (`stack.Count`) for each emitted primitive,
      /// parallel to `Geometry`. For a `Segment`, depth when drawn;
      /// for a `Polygon`, depth when `{` was opened.
      PrimitiveDepths: int[] }

/// Interprets a drawing string under `limits`. Returns the geometry, trace, and
/// any warnings, or an error if the turtle meets a non-finite value, an invalid
/// width, or exceeds the primitive budget: backends must only ever receive
/// finite geometry.
let interpretWithTrace
    (limits: Limits)
    (settings: Settings)
    (drawing: LString)
    : Result<Geometry * TurtleTrace * Diagnostic list, Diagnostic list> =
    let out = ResizeArray<Primitive>()
    let depths = ResizeArray<int>()
    let warnings = ResizeArray<Diagnostic>()
    let warned = System.Collections.Generic.HashSet<struct (string * int)>()
    let stack = System.Collections.Generic.Stack<struct (State * GcBuilder option)>()
    // One (opening bracket depth, vertex list) pair per open `{` (§5.3).
    let polygons = System.Collections.Generic.Stack<struct (int * ResizeArray<Vec3>)>()
    let mutable t = initialState settings
    let mutable activeGC: GcBuilder option = None
    let mutable primitiveCount = 0
    let mutable unmatchedClose = false
    let mutable vertexOutside = false
    let mutable warnedUnbalancedGC = false
    let tHat = normalizeTropism settings.Tropism

    let warnUnbalancedGC (msg: string) =
        if not warnedUnbalancedGC then
            warnedUnbalancedGC <- true
            warnings.Add(Diagnostic.warning Codes.UnbalancedGC None msg)

    let consumeBudget () =
        match limits.MaxPrimitives with
        | Some limit when primitiveCount >= limit ->
            raise (TurtleError(Codes.PrimitiveLimitExceeded, $"primitive limit of %s{string limit} exceeded"))
        | _ -> primitiveCount <- primitiveCount + 1

    let addPrimitive (depth: int) (p: Primitive) =
        out.Add p
        depths.Add depth

    /// `F`, `f` and `G`: emit the segment (`draw`), record the polygon vertex
    /// (`vertex`), then bend toward T (§5.5).
    let move (d: float) draw vertex =
        let p1 = t.P + d * t.H

        if not (isFinite p1) then
            invalidGeometry "the turtle's position is no longer finite (overflow)"

        if draw then
            match activeGC with
            | Some gc when polygons.Count = 0 ->
                consumeBudget ()
                gc.Sections.Add(snapshotSection { t with P = p1 })
            | _ ->
                consumeBudget ()
                addPrimitive stack.Count (Segment(t.P, p1, t.Width, t.Color))

        if vertex && polygons.Count > 0 then
            let struct (_, verts) = polygons.Peek()
            verts.Add p1

        t <- bend tHat settings.Elasticity { t with P = p1 }

    let run () =
        match limits.MaxPrimitives with
        | Some limit when limit < 0 ->
            raise (TurtleError(Codes.PrimitiveLimitExceeded, $"primitive limit of %s{string limit} exceeded"))
        | _ -> ()

        // These can come from CLI overrides, which the checker never saw.
        if not (System.Double.IsFinite settings.Width) || settings.Width < 0.0 then
            invalidGeometry $"the initial width must be finite and non-negative, got %s{formatNumber settings.Width}"

        if not (isFinite settings.Tropism) || tHat = Vec3.zero then
            invalidGeometry "the tropism vector must have finite components and not be the zero vector"

        if not (System.Double.IsFinite settings.Elasticity) then
            invalidGeometry $"the elasticity must be finite, got %s{formatNumber settings.Elasticity}"

        for sym in drawing do
            match sym with
            | Push ->
                stack.Push(struct (t, activeGC))

                match activeGC with
                | None -> ()
                | Some gc ->
                    let lastSec = gc.Sections[gc.Sections.Count - 1]

                    activeGC <-
                        Some
                            { OpenDepth = stack.Count
                              IsForked = true
                              Sides = gc.Sides
                              ContourVariant = gc.ContourVariant
                              Sections = ResizeArray([ lastSec ]) }
            | Pop ->
                if stack.Count > 0 then
                    match activeGC with
                    | None -> ()
                    | Some gc ->
                        if gc.IsForked then
                            if gc.Sections.Count >= 2 then
                                addPrimitive
                                    gc.OpenDepth
                                    (GeneralizedCylinder { Sides = gc.Sides; Sections = List.ofSeq gc.Sections })
                        else
                            warnUnbalancedGC "unclosed `StartGC` inside `[...]`; generalized cylinder dropped"

                    let struct (savedT, savedGC) = stack.Pop()
                    t <- savedT
                    activeGC <- savedGC
            | Cut -> () // the cut happens in the next derivation step (language.md "Cutting branches")
            | Mod(name, args) ->
                match turtleArity.TryFind name with
                | None -> () // not a turtle symbol: ignored (§5.2)
                | Some arity ->
                    if args.Length > arity && warned.Add(struct (name, args.Length)) then
                        warnings.Add(
                            Diagnostic.warning
                                Codes.ExtraTurtleParameters
                                None
                                $"the turtle ignores the extra parameters of `%s{name}` (it uses %s{string arity}, got %s{string args.Length})"
                        )

                    // The value a command consumes: parameter `i`, or the
                    // setting/default it falls back to. Only consumed values must be finite;
                    // unused parameters are ignored (with the warning above).
                    let argAt (i: int) (setting: string option) (dflt: float) =
                        let v = if args.Length > i then args[i] else dflt

                        if not (System.Double.IsFinite v) then
                            if args.Length > i then
                                invalidGeometry $"`%s{format [| sym |]}`: turtle parameters must be finite"
                            else
                                match setting with
                                | Some s ->
                                    invalidGeometry $"`%s{name}` uses `#%s{s}`, which is not finite (%s{formatNumber v})"
                                | None ->
                                    invalidGeometry $"`%s{name}` uses a non-finite default (%s{formatNumber v})"

                        v

                    let arg (setting: string) (dflt: float) = argAt 0 (Some setting) dflt

                    match name with
                    | "F" -> move (arg "step" settings.Step) true true
                    | "f" -> move (arg "step" settings.Step) false true
                    | "G" -> move (arg "step" settings.Step) true false
                    | "." ->
                        if polygons.Count > 0 then
                            let struct (_, verts) = polygons.Peek()
                            verts.Add t.P
                        else
                            vertexOutside <- true
                    | "$" -> t <- rollToHorizontal t
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
                                    invalidGeometry $"`%s{format [| sym |]}`: widths must be non-negative"

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

                        match activeGC with
                        | Some gc when polygons.Count = 0 && gc.Sections.Count > 0 ->
                            let lastIdx = gc.Sections.Count - 1
                            gc.Sections[lastIdx] <- { gc.Sections[lastIdx] with Color = c }
                        | _ -> ()
                    // An empty polygon (ABOP p. 127); v0.1 also recorded P here (§5.3).
                    | "{" -> polygons.Push(struct (stack.Count, ResizeArray()))
                    | "}" ->
                        if polygons.Count > 0 then
                            let struct (openDepth, verts) = polygons.Pop()
                            consumeBudget ()
                            addPrimitive openDepth (Polygon(List.ofSeq verts, t.Color))
                        else
                            unmatchedClose <- true
                    | "StartGC" ->
                        let openExplicit () =
                            activeGC <-
                                Some
                                    { OpenDepth = stack.Count
                                      IsForked = false
                                      Sides = defaultArg t.ContourSides 0
                                      ContourVariant = variantOf t.Contour
                                      Sections = ResizeArray([ snapshotSection t ]) }

                        match activeGC with
                        | None -> openExplicit ()
                        | Some gc when gc.IsForked && gc.Sections.Count = 1 -> openExplicit ()
                        | Some _ ->
                            warnUnbalancedGC "`StartGC` while a generalized cylinder is already active; ignored"
                    | "EndGC" ->
                        match activeGC with
                        | None ->
                            warnUnbalancedGC "`EndGC` without an active `StartGC`; ignored"
                        | Some gc ->
                            if gc.Sections.Count >= 2 then
                                addPrimitive
                                    gc.OpenDepth
                                    (GeneralizedCylinder { Sides = gc.Sides; Sections = List.ofSeq gc.Sections })

                            activeGC <- None
                    | "ContourCircle" ->
                        match activeGC with
                        | Some gc when gc.ContourVariant <> VarCircle ->
                            warnUnbalancedGC "cannot switch contour kind inside an active generalized cylinder; ignored"
                        | _ -> t <- { t with Contour = Circle }
                    | "ContourFlat" ->
                        match activeGC with
                        | Some gc when gc.ContourVariant <> VarFlat ->
                            warnUnbalancedGC "cannot switch contour kind inside an active generalized cylinder; ignored"
                        | _ -> t <- { t with Contour = Flat }
                    | "ContourCup" ->
                        let d = argAt 0 None 0.5

                        match activeGC with
                        | Some gc when gc.ContourVariant <> VarUCup ->
                            warnUnbalancedGC "cannot switch contour kind inside an active generalized cylinder; ignored"
                        | _ -> t <- { t with Contour = UCup d }
                    | "ContourV" ->
                        let d = argAt 0 None 0.5

                        match activeGC with
                        | Some gc when gc.ContourVariant <> VarVFold ->
                            warnUnbalancedGC "cannot switch contour kind inside an active generalized cylinder; ignored"
                        | _ -> t <- { t with Contour = VFold d }
                    | "ScaleContour" ->
                        let sL = argAt 0 None 1.0
                        let sU = argAt 1 None 1.0

                        if sL < 0.0 || sU < 0.0 then
                            invalidGeometry $"`%s{format [| sym |]}`: contour scales must be non-negative"

                        t <- { t with ScaleL = sL; ScaleU = sU }
                    | "ContourSides" ->
                        let sidesOpt =
                            if args.Length > 0 then
                                let v = truncate (argAt 0 None 0.0)

                                if v < 3.0 || v > float System.Int32.MaxValue then
                                    invalidGeometry $"`%s{format [| sym |]}`: contour sides must be at least 3"

                                Some(int v)
                            else
                                None

                        match activeGC with
                        | Some _ ->
                            warnUnbalancedGC "cannot change `ContourSides` inside an active generalized cylinder; ignored"
                        | None -> t <- { t with ContourSides = sidesOpt }
                    | _ -> ()

    try
        run ()

        if unmatchedClose then
            warnings.Add(Diagnostic.warning Codes.UnbalancedPolygon None "`}` without a matching `{`; ignored")

        if polygons.Count > 0 then
            warnings.Add(Diagnostic.warning Codes.UnbalancedPolygon None "`{` without a matching `}`; polygon dropped")

        let hasUnclosedExplicitGC =
            (match activeGC with
             | Some gc when not gc.IsForked -> true
             | _ -> false)
            || (stack
                |> Seq.exists (fun struct (_, gcOpt) ->
                    match gcOpt with
                    | Some gc when not gc.IsForked -> true
                    | _ -> false))

        if hasUnclosedExplicitGC then
            warnUnbalancedGC "`StartGC` without a matching `EndGC`; generalized cylinder dropped"

        if vertexOutside then
            warnings.Add(Diagnostic.warning Codes.VertexOutsidePolygon None "`.` outside a polygon; ignored")

        Ok(List.ofSeq out, { PrimitiveDepths = depths.ToArray() }, List.ofSeq warnings)
    with TurtleError(code, msg) ->
        Error [ Diagnostic.error code None msg ]

/// Interprets a drawing string under `limits`. Returns the geometry and any
/// warnings, or an error if the turtle meets a non-finite value, an invalid
/// width, or exceeds the primitive budget: backends must only ever receive
/// finite geometry.
let interpretWith
    (limits: Limits)
    (settings: Settings)
    (drawing: LString)
    : Result<Geometry * Diagnostic list, Diagnostic list> =
    interpretWithTrace limits settings drawing |> Result.map (fun (g, _, ws) -> g, ws)

/// Interprets a drawing string under default execution limits.
let interpret (settings: Settings) (drawing: LString) : Result<Geometry * Diagnostic list, Diagnostic list> =
    interpretWith Limits.defaults settings drawing
