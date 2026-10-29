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

let private deg = 180.0 / Math.PI

// Sine and cosine in degrees, computed with + - * / only (language.md §4.1).
//
// IEEE 754 fixes the result of + - * / and sqrt to the last bit, but not of sin
// and cos: Math.Sin calls the system's math library (glibc, Microsoft's CRT,
// ...), and their results differ in the last bit. So RubberPlant computes them
// itself, and gets the same bits on every platform. .NET never fuses a multiply
// and an add on its own, so the code below rounds the same way everywhere.
//
// 1. Reduce the angle to [0°, 45°]. In degrees this is exact: a % 360 is exact
//    (IEEE remainder), and r - 90k and 90 - x are exact subtractions (Sterbenz:
//    a - b is exact when b/2 <= a <= 2b). Quarter turns give exactly 0 and ±1.
// 2. Convert to radians as hi + lo, a double-double product with π/180, so the
//    conversion adds well under an ulp of error.
// 3. Evaluate fdlibm's kernels on [0, π/4], under an ulp of error.

// Coefficients of fdlibm's __kernel_sin and __kernel_cos (k_sin.c, k_cos.c;
// Copyright (C) 1993 by Sun Microsystems, Inc. Developed at SunPro, a Sun
// Microsystems, Inc. business. Permission to use, copy, modify, and distribute
// this software is freely granted, provided that this notice is preserved.)
// The kernels' structure follows FreeBSD's msun versions of the same files.
let private S1 = -1.66666666666666324348e-01 // 0xBFC55555 55555549
let private S2 = 8.33333333332248946124e-03 // 0x3F811111 1110F8A6
let private S3 = -1.98412698298579493134e-04 // 0xBF2A01A0 19C161D5
let private S4 = 2.75573137070700676789e-06 // 0x3EC71DE3 57B1FE7D
let private S5 = -2.50507602534068634195e-08 // 0xBE5AE5E6 8A2B9CEB
let private S6 = 1.58969099521155010221e-10 // 0x3DE5D93A 5ACFD57C
let private C1 = 4.16666666666666019037e-02 // 0x3FA55555 5555554C
let private C2 = -1.38888888888741095749e-03 // 0xBF56C16C 16C15177
let private C3 = 2.48015872894767294178e-05 // 0x3EFA01A0 19CB1590
let private C4 = -2.75573143513906633035e-07 // 0xBE927E4F 809C52AD
let private C5 = 2.08757232129817482790e-09 // 0x3E21EE9E BDB4B1C4
let private C6 = -1.13596475577881948265e-11 // 0xBDA8FAE9 BE8838D4

/// π/180 = PiOver180Hi + PiOver180Lo, to about 2^-106.
let private PiOver180Hi = 0.017453292519943295 // 0x3F91DF46 A2529D39
let private PiOver180Lo = 2.9486522708701687e-19 // 0x3C15C1D8 BECDD291

/// PiOver180Hi split into two halves of at most 26 bits (Veltkamp), so their
/// products with the halves of a split x are exact.
let private PiOver180HiHead = 0.01745329238474369
let private PiOver180HiTail = 1.3519960498364902e-10

/// sin(hi + lo) for |hi + lo| <= π/4, with |lo| about half an ulp of hi.
let inline private kernelSin (hi: float) (lo: float) =
    let z = hi * hi
    let w = z * z
    let r = S2 + z * (S3 + z * S4) + z * w * (S5 + z * S6)
    let v = z * hi
    hi - ((z * (0.5 * lo - v * r) - lo) - v * S1)

/// cos(hi + lo) for |hi + lo| <= π/4, with |lo| about half an ulp of hi.
let inline private kernelCos (hi: float) (lo: float) =
    let z = hi * hi
    let w = z * z
    let r = z * (C1 + z * (C2 + z * C3)) + w * w * (C4 + z * (C5 + z * C6))
    let hz = 0.5 * z
    let w = 1.0 - hz
    w + (((1.0 - w) - hz) + (z * r - hi * lo))

/// x degrees (0 <= x <= 45) in radians, as an unevaluated sum hi + lo.
let inline private toRadians (x: float) =
    let hi = x * PiOver180Hi
    // Dekker's product: x = xh + xl exactly, and err = x * PiOver180Hi - hi exactly.
    let t = 134217729.0 * x // 2^27 + 1
    let xh = t - (t - x)
    let xl = x - xh

    let err =
        ((xh * PiOver180HiHead - hi) + xh * PiOver180HiTail + xl * PiOver180HiHead)
        + xl * PiOver180HiTail

    struct (hi, err + x * PiOver180Lo)

/// Sine and cosine of an angle in degrees, with the same bits on every
/// platform, within an ulp of the exact values. Exact at multiples of 90°
/// (0 is +0), odd and even exactly. NaN for an infinite or NaN angle.
let sinCosDeg (a: float) : struct (float * float) =
    if not (Double.IsFinite a) then
        struct (nan, nan)
    else
        let r = abs a
        // % calls into the runtime (fmod). Its result is exact by definition, so every
        // platform agrees on it, but most angles are already below 360: skip the call.
        let r = if r < 360.0 then r else r % 360.0

        // Quadrant k, and x = r - 90k in [0, 90): both exact.
        let k, x =
            if r < 90.0 then 0, r
            elif r < 180.0 then 1, r - 90.0
            elif r < 270.0 then 2, r - 180.0
            else 3, r - 270.0

        let struct (sx, cx) =
            if x <= 45.0 then
                let struct (hi, lo) = toRadians x
                struct (kernelSin hi lo, kernelCos hi lo)
            else
                // sin x = cos(90 - x), and 90 - x is exact.
                let struct (hi, lo) = toRadians (90.0 - x)
                struct (kernelCos hi lo, kernelSin hi lo)

        let struct (s, c) =
            match k with
            | 0 -> struct (sx, cx)
            | 1 -> struct (cx, -sx)
            | 2 -> struct (-sx, -cx)
            | _ -> struct (-cx, sx)

        let s = if a < 0.0 then -s else s
        // + 0.0 turns -0 into +0, so exact zeros are always +0.
        struct (s + 0.0, c + 0.0)

/// Sine of an angle in degrees (see sinCosDeg).
let sinDeg (a: float) =
    let struct (s, _) = sinCosDeg a
    s

/// Cosine of an angle in degrees (see sinCosDeg).
let cosDeg (a: float) =
    let struct (_, c) = sinCosDeg a
    c

/// Tangent of an angle in degrees: sin / cos, so ±infinity at odd multiples of 90°.
let tanDeg (a: float) =
    let struct (s, c) = sinCosDeg a
    s / c

let apply (f: Builtin) (args: float[]) : float =
    match f with
    | Sin -> sinDeg args[0]
    | Cos -> cosDeg args[0]
    | Tan -> tanDeg args[0]
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
