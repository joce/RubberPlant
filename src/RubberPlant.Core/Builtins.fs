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

// Sine, cosine and tangent in degrees, computed with + - * / only (language.md §4.1).
//
// IEEE 754 fixes the result of + - * / and sqrt to the last bit, but not of sin,
// cos and tan: Math.Sin calls the system's math library (glibc, Microsoft's CRT,
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

// Coefficients of fdlibm's __kernel_tan (k_tan.c; Copyright 2004 Sun
// Microsystems, Inc. All Rights Reserved. Permission to use, copy, modify, and
// distribute this software is freely granted, provided that this notice is
// preserved.) Transcribed from FreeBSD's lib/msun/src/k_tan.c.
let private T0 = 3.33333333333334091986e-01 // 0x3FD55555 55555563
let private T1 = 1.33333333333201242699e-01 // 0x3FC11111 1110FE7A
let private T2 = 5.39682539762260521377e-02 // 0x3FABA1BA 1BB341FE
let private T3 = 2.18694882948595424599e-02 // 0x3F9664F4 8406D637
let private T4 = 8.86323982359930005737e-03 // 0x3F8226E3 E96E8493
let private T5 = 3.59207910759131235356e-03 // 0x3F6D6D22 C9560328
let private T6 = 1.45620945432529025516e-03 // 0x3F57DBC8 FEE08315
let private T7 = 5.88041240820264096874e-04 // 0x3F4344D8 F2F26501
let private T8 = 2.46463134818469906812e-04 // 0x3F3026F7 1A8D1068
let private T9 = 7.81794442939557092300e-05 // 0x3F147E88 A03792A6
let private T10 = 7.14072491382608190305e-05 // 0x3F12B80F 32F0A7E9
let private T11 = -1.85586374855275456654e-05 // 0xBEF375CB DB605373
let private T12 = 2.59073051863633712884e-05 // 0x3EFB2A70 74BF7AD4
let private Pio4 = 7.85398163397448278999e-01 // 0x3FE921FB 54442D18
let private Pio4Lo = 3.06161699786838301793e-17 // 0x3C81A626 33145C07

/// About 0.6744 (high word 0x3FE59428): above it, __kernel_tan works on π/4 − x.
let private TanReflect = BitConverter.Int64BitsToDouble 0x3FE5942800000000L

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

/// The double with its low 32 bits cleared (fdlibm's SET_LOW_WORD(x, 0)).
let inline private clearLow (x: float) =
    BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits x &&& 0xFFFFFFFF00000000L)

/// tan(hi + lo) when iy = 1, -1/tan(hi + lo) when iy = -1, for 0 < hi + lo <= π/4,
/// with |lo| about half an ulp of hi. fdlibm's __kernel_tan, for positive x.
let private kernelTan (hi: float) (lo: float) (iy: int) =
    let reflect = hi >= TanReflect
    // tan(π/4 - x) = (1 - tan x) / (1 + tan x): evaluate the polynomial on π/4 - x.
    let x, y = if reflect then (Pio4 - hi) + (Pio4Lo - lo), 0.0 else hi, lo
    let z = x * x
    let w = z * z
    let r = T1 + w * (T3 + w * (T5 + w * (T7 + w * (T9 + w * T11))))
    let v = z * (T2 + w * (T4 + w * (T6 + w * (T8 + w * (T10 + w * T12)))))
    let s = z * x
    let r = y + z * (s * (r + v) + y)
    let r = r + T0 * s
    let w = x + r

    if reflect then
        let v = float iy
        v - 2.0 * (x - (w * w / (w + v) - r))
    elif iy = 1 then
        w
    else
        // -1 / (x + r), accurately: z + v = x + r with z's low word cleared.
        let z = clearLow w
        let v = r - (z - x)
        let a = -1.0 / w
        let t = clearLow a
        let s = 1.0 + t * z
        t + a * (s + t * v)

/// Below this angle (2^-900 degrees), Dekker's partial products in toRadians
/// could underflow, and its error term would no longer be exact.
let private Tiny = Math.ScaleB(1.0, -900)

/// x degrees (Tiny <= x <= 45) in radians, as an unevaluated sum hi + lo.
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

/// Reduces a finite angle in degrees: |a| = 360 n + 90 k + x, with the quadrant k
/// in 0..3 and x in [0, 90). Exact: a % 360 is exact (IEEE remainder), and each
/// r - 90k is an exact subtraction (Sterbenz).
let inline private reduce (a: float) =
    let r = abs a
    // % calls into the runtime (fmod). Its result is exact by definition, so every
    // platform agrees on it, but most angles are already below 360: skip the call.
    let r = if r < 360.0 then r else r % 360.0

    if r < 90.0 then struct (0, r)
    elif r < 180.0 then struct (1, r - 90.0)
    elif r < 270.0 then struct (2, r - 180.0)
    else struct (3, r - 270.0)

/// Sine and cosine of an angle in degrees, with the same bits on every
/// platform, within an ulp of the exact values. Exact at multiples of 90°
/// (0 is +0), odd and even exactly. NaN for an infinite or NaN angle.
let sinCosDeg (a: float) : struct (float * float) =
    if not (Double.IsFinite a) then
        struct (nan, nan)
    else
        let struct (k, x) = reduce a

        let struct (sx, cx) =
            if x < Tiny then
                // sin x = x π/180 to far beyond double precision (the next term is
                // smaller by a factor of about 10^-540), and cos x = 1. A single
                // product is within 0.66 ulp (0.5 for its rounding, 0.15 for the missing
                // PiOver180Lo), subnormal results included.
                struct (x * PiOver180Hi, 1.0)
            elif x <= 45.0 then
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

/// Tangent of an angle in degrees, with the same bits on every platform, within
/// an ulp of the exact value. Exact at multiples of 90°: 0 at multiples of 180°,
/// and at the poles the sign of sin / cos with cos = +0 (tan 90 = +∞, tan 270 =
/// tan -90 = -∞). Odd exactly. NaN for an infinite or NaN angle.
let tanDeg (a: float) =
    if not (Double.IsFinite a) then
        nan
    else
        let struct (k, x) = reduce a
        // tan(90k + x) is tan x for an even k, and -1/tan x for an odd k.
        let odd = k &&& 1 = 1

        let t =
            if x = 0.0 then
                if not odd then 0.0
                elif k = 1 then infinity
                else -infinity
            else
                // With y = 90 - x (exact) when x > 45: tan x = -(-1/tan y) and
                // -1/tan x = -tan y.
                let swapped = x > 45.0
                let y = if swapped then 90.0 - x else x
                let iy = if odd = swapped then 1 else -1

                let value =
                    if y < Tiny then
                        // Only reached for even k and no swap (iy = 1): see sinCosDeg.
                        let t = y * PiOver180Hi
                        if iy = 1 then t else -1.0 / t
                    else
                        let struct (hi, lo) = toRadians y
                        kernelTan hi lo iy

                if swapped then -value else value

        let t = if a < 0.0 then -t else t
        // + 0.0 turns -0 into +0.
        t + 0.0

// Natural exponential and logarithm, computed with IEEE-754 + - * / and bit
// manipulation only (language.md §4.1, v0.3-organs-animation.md §2.2), so they
// produce identical bits on Linux, Windows, and Windows Native AOT.

// Coefficients of fdlibm's __ieee754_exp (e_exp.c; Copyright (C) 2004 by Sun
// Microsystems, Inc. All rights reserved. Permission to use, copy, modify, and
// distribute this software is freely granted, provided that this notice is
// preserved.)
let private ExpOThreshold = 7.09782712893383973096e+02 // 0x40862E42 FEFA39EF
let private ExpUThreshold = -7.45133219101941108420e+02 // 0xC0874910 D52D3051
let private Ln2HiPos = 6.93147180369123816490e-01 // 0x3FE62E42 FEE00000
let private Ln2HiNeg = -6.93147180369123816490e-01 // 0xBFE62E42 FEE00000
let private Ln2LoPos = 1.90821492927058770002e-10 // 0x3DEA39EF 35793C76
let private Ln2LoNeg = -1.90821492927058770002e-10 // 0xBDEA39EF 35793C76
let private InvLn2 = 1.44269504088896338700e+00 // 0x3FF71547 652B82FE
let private ExpP1 = 1.66666666666666019037e-01 // 0x3FC55555 5555553E
let private ExpP2 = -2.77777777770155933842e-03 // 0xBF66C16C 16BEBD93
let private ExpP3 = 6.61375632143793436117e-05 // 0x3F11566A AF25DE2C
let private ExpP4 = -1.65339022054652515390e-06 // 0xBEBBBD41 C5D26BF1
let private ExpP5 = 4.13813679705723846039e-08 // 0x3E663769 72BEA4D0
let private TwoM1000 = BitConverter.Int64BitsToDouble 0x0170000000000000L // 2^-1000
let private Two512 = BitConverter.Int64BitsToDouble 0x5FF0000000000000L // 2^512

// Coefficients of fdlibm's __ieee754_log (e_log.c; Copyright (C) 1993 by Sun
// Microsystems, Inc. Developed at SunPro, a Sun Microsystems, Inc. business.
// Permission to use, copy, modify, and distribute this software is freely
// granted, provided that this notice is preserved.)
let private Two54 = 18014398509481984.0 // 0x43500000 00000000 (2^54)
let private Lg1 = 6.666666666666735130e-01 // 0x3FE55555 55555593
let private Lg2 = 3.999999999940941908e-01 // 0x3FD99999 9997FA04
let private Lg3 = 2.857142874366239149e-01 // 0x3FD24924 94229359
let private Lg4 = 2.222219843214978396e-01 // 0x3FCC71C5 1D8E78AF
let private Lg5 = 1.818357216161805012e-01 // 0x3FC74664 96CB03DE
let private Lg6 = 1.531383769920937332e-01 // 0x3FC39A09 D078C69F
let private Lg7 = 1.479819860511658591e-01 // 0x3FC2F112 DF3E5244

/// Natural exponential e^x, with the same bits on every platform, within one
/// ulp of the exact value. Exact at 0 -> 1, -inf -> 0, +inf -> +inf, NaN -> NaN.
let exp (x: float) : float =
    let bits = BitConverter.DoubleToInt64Bits x
    let hx = int32 (bits >>> 32)
    let xsb = (uint32 hx) >>> 31
    let hxAbs = hx &&& 0x7fffffff
    let lx = uint32 (bits &&& 0xffffffffL)

    if hxAbs >= 0x7ff00000 then
        if ((hxAbs &&& 0xfffff) ||| int32 lx) <> 0 then
            nan
        elif xsb = 0u then
            infinity
        else
            0.0
    elif x > ExpOThreshold then
        infinity
    elif x < ExpUThreshold then
        0.0
    elif hxAbs < 0x3e300000 then
        // |x| < 2^-28: exp(x) = 1 + x within 0.51 ulp; exact for ±0 -> 1.0.
        1.0 + x
    else
        let k, hi, lo =
            if hxAbs > 0x3fd62e42 then
                // |x| > 0.5 * ln 2
                if hxAbs < 0x3ff0a2b2 then
                    // |x| < 1.5 * ln 2
                    let hi = x - (if xsb = 0u then Ln2HiPos else Ln2HiNeg)
                    let lo = if xsb = 0u then Ln2LoPos else Ln2LoNeg
                    let k = if xsb = 0u then 1 else -1
                    k, hi, lo
                else
                    let half = if xsb = 0u then 0.5 else -0.5
                    let k = int (InvLn2 * x + half)
                    let t = float k
                    k, x - t * Ln2HiPos, t * Ln2LoPos
            else
                0, x, 0.0

        let r = hi - lo
        let t = r * r
        let c = r - t * (ExpP1 + t * (ExpP2 + t * (ExpP3 + t * (ExpP4 + t * ExpP5))))

        let y =
            if k = 0 then
                1.0 - ((r * c) / (c - 2.0) - r)
            else
                1.0 - ((lo - (r * c) / (2.0 - c)) - hi)

        if k = 0 then
            y
        elif k >= -1021 then
            if k = 1024 then
                (y * Two512) * Two512
            else
                BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits y + (int64 k <<< 52))
        else
            let scaled =
                BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits y + (int64 (k + 1000) <<< 52))

            scaled * TwoM1000

/// Natural logarithm ln(x), with the same bits on every platform, within one
/// ulp of the exact value. Exact at 1 -> 0, ±0 -> -inf, +inf -> +inf, x < 0 or NaN -> NaN.
let log (x: float) : float =
    let bits = BitConverter.DoubleToInt64Bits x
    let hx = int32 (bits >>> 32)
    let lx = uint32 (bits &&& 0xffffffffL)

    if ((hx &&& 0x7fffffff) ||| int32 lx) = 0 then
        -infinity
    elif hx < 0 then
        nan
    elif hx >= 0x7ff00000 then
        if ((hx &&& 0xfffff) ||| int32 lx) <> 0 then
            nan
        else
            infinity
    else
        let mutable k = 0
        let mutable hx = hx
        let mutable lx = lx

        if hx < 0x00100000 then
            // Subnormal: normalize into normal range by multiplying by 2^54.
            k <- -54
            let scaledBits = BitConverter.DoubleToInt64Bits(x * Two54)
            hx <- int32 (scaledBits >>> 32)
            lx <- uint32 (scaledBits &&& 0xffffffffL)

        k <- k + (hx >>> 20) - 1023
        hx <- hx &&& 0x000fffff
        let i = (hx + 0x95f64) &&& 0x100000
        let xNorm = BitConverter.Int64BitsToDouble((int64 (hx ||| (i ^^^ 0x3ff00000)) <<< 32) ||| int64 lx)
        k <- k + (i >>> 20)
        let f = xNorm - 1.0

        if (0x000fffff &&& (2 + hx)) < 3 then
            // |f| < 2^-20: x is very close to a power of 2.
            if f = 0.0 then
                if k = 0 then
                    0.0
                else
                    let dk = float k
                    dk * Ln2HiPos + dk * Ln2LoPos
            else
                let r = f * f * (0.5 - 0.33333333333333333 * f)

                if k = 0 then
                    f - r
                else
                    let dk = float k
                    dk * Ln2HiPos - ((r - dk * Ln2LoPos) - f)
        else
            let s = f / (2.0 + f)
            let dk = float k
            let z = s * s
            let ii = hx - 0x6147a
            let w = z * z
            let jj = 0x6b851 - hx
            let t1 = w * (Lg2 + w * (Lg4 + w * Lg6))
            let t2 = z * (Lg1 + w * (Lg3 + w * (Lg5 + w * Lg7)))
            let r = t2 + t1

            if (ii ||| jj) > 0 then
                let hfsq = 0.5 * f * f

                if k = 0 then
                    f - (hfsq - s * (hfsq + r))
                else
                    dk * Ln2HiPos - ((hfsq - (s * (hfsq + r) + dk * Ln2LoPos)) - f)
            else if k = 0 then
                f - s * (f - r)
            else
                dk * Ln2HiPos - ((s * (f - r) - dk * Ln2LoPos) - f)

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
    | Exp -> exp args[0]
    | Log -> log args[0]
    | Abs -> Math.Abs args[0]
    | Floor -> Math.Floor args[0]
    | Ceil -> Math.Ceiling args[0]
    // C's round: halves go away from zero (not .NET's default banker's rounding).
    | Round -> Math.Round(args[0], MidpointRounding.AwayFromZero)
    | Min -> Math.Min(args[0], args[1])
    | Max -> Math.Max(args[0], args[1])
