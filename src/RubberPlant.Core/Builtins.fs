/// Built-in functions (§4.1). Trigonometry works in degrees, like the turtle.
module RubberPlant.Builtins

open System

type Builtin =
    | Sin
    | Cos
    | Tan
    | Asin
    | Acos
    | Atan
    | Atan2
    | Sqrt
    | Exp
    | Log
    | Abs
    | Floor
    | Ceil
    | Round
    | Min
    | Max

let private table =
    [ "sin", Sin, 1
      "cos", Cos, 1
      "tan", Tan, 1
      "asin", Asin, 1
      "acos", Acos, 1
      "atan", Atan, 1
      "atan2", Atan2, 2
      "sqrt", Sqrt, 1
      "exp", Exp, 1
      "log", Log, 1
      "abs", Abs, 1
      "floor", Floor, 1
      "ceil", Ceil, 1
      "round", Round, 1
      "min", Min, 2
      "max", Max, 2 ]

/// Name → (function, arity).
let byName: Map<string, Builtin * int> =
    table |> List.map (fun (n, b, a) -> n, (b, a)) |> Map.ofList

let isBuiltin name = byName.ContainsKey name

let private rad = Math.PI / 180.0
let private deg = 180.0 / Math.PI

/// Sine of an angle in degrees, exact at multiples of 90° (so `sin(180)` is
/// 0, and quarter turns don't leave rounding noise in turtle coordinates).
let sinDeg (a: float) =
    let r = a % 360.0
    let r = if r < 0.0 then r + 360.0 else r

    match r with
    | 0.0
    | 180.0 -> 0.0
    | 90.0 -> 1.0
    | 270.0 -> -1.0
    | _ -> Math.Sin(r * rad)

/// Cosine of an angle in degrees, exact at multiples of 90°.
let cosDeg (a: float) = sinDeg (a + 90.0)

let apply (f: Builtin) (args: float[]) : float =
    match f with
    | Sin -> sinDeg args[0]
    | Cos -> cosDeg args[0]
    | Tan -> Math.Tan(args[0] * rad)
    | Asin -> Math.Asin args[0] * deg
    | Acos -> Math.Acos args[0] * deg
    | Atan -> Math.Atan args[0] * deg
    | Atan2 -> Math.Atan2(args[0], args[1]) * deg
    | Sqrt -> Math.Sqrt args[0]
    | Exp -> Math.Exp args[0]
    | Log -> Math.Log args[0]
    | Abs -> Math.Abs args[0]
    | Floor -> Math.Floor args[0]
    | Ceil -> Math.Ceiling args[0]
    // C's round: halves go away from zero (not .NET's default banker's rounding).
    | Round -> Math.Round(args[0], MidpointRounding.AwayFromZero)
    | Min -> Math.Min(args[0], args[1])
    | Max -> Math.Max(args[0], args[1])
