module RubberPlant.Core.Tests.ExpLogTests

open System
open System.Security.Cryptography
open Expecto
open RubberPlant
open RubberPlant.Ast
open RubberPlant.Model

let private toBits (x: float) = BitConverter.DoubleToInt64Bits x
let private ofBits (b: int64) = BitConverter.Int64BitsToDouble b

/// The error of `got`, in ulps of the exact value hi + lo (a double-double).
let private ulpError (got: float) (hi: int64, lo: int64) =
    let hi, lo = ofBits hi, ofBits lo

    if hi = 0.0 then
        if got = 0.0 then 0.0 else infinity
    else
        let a = abs hi
        let below = lo * float (sign hi) < 0.0
        let ulp = if below then a - Math.BitDecrement a else Math.BitIncrement a - a
        abs ((got - hi) - lo) / ulp

/// (x, (exp(x) hi, lo)) as bits, computed with 120-digit Decimal arithmetic.
let private expReference =
    [ 0xc0874910d52d3051L, (0x0000000000000001L, 0x8000000000000000L) // -745.1332191019411
      0xc0874385446d71c3L, (0x0000000000000001L, 0x0000000000000000L) // -744.4400719213812
      0xc0862e42fefa39efL, (0x000400000000001bL, 0x8000000000000000L) // -709.782712893384
      0xc086232bdd7abcd2L, (0x001000000000007cL, 0x8000000000000000L) // -708.3964185322641
      0xc059000000000000L, (0x36ea8c1f14e2af5dL, 0xb3843089bb228e2cL) // -100.0
      0xc025000000000000L, (0x3efcdfc263f6a0baL, 0xbb72e222c850c539L) // -10.5
      0xbff8000000000000L, (0x3fcc8f87724b5c1dL, 0x3c691afa497dc416L) // -1.5
      0xbff0000000000000L, (0x3fd78b56362cef38L, 0xbc6ca8a4270fadf5L) // -1.0
      0xbfe62e42fefa39efL, (0x3fe0000000000000L, 0x3c6abc9e3b39803fL) // -0.6931471805599453
      0xbfe0000000000000L, (0x3fe368b2fc6f960aL, 0xbc285314b9559e64L) // -0.5
      0xbfd62e42fefa39efL, (0x3fe6a09e667f3bcdL, 0xbc87233c057e4796L) // -0.34657359027997264
      0xbfd62e42fefa39f0L, (0x3fe6a09e667f3bccL, 0x3c823c2594027c9eL) // -0.3465735902799727
      0xbfb999999999999aL, (0x3fecf46d99d52b3aL, 0x3c8d255100c4c1e2L) // -0.1
      0xbee4f8b588e368f1L, (0x3fefffeb07515653L, 0x3c5a6d17a564b029L) // -1e-05
      0xbe40000000000000L, (0x3feffffffc000000L, 0x3c7ffffffeaaaaabL) // -2^-27
      0xbe30000000000000L, (0x3feffffffe000000L, 0x3c5fffffff555555L) // -2^-28
      0xbe20000000000000L, (0x3fefffffff000000L, 0x3c3fffffffaaaaabL) // -2^-29
      0xbca0000000000000L, (0x3fefffffffffffffL, 0x3940000000000000L) // -2^-53
      0xbbc79ca10c924223L, (0x3ff0000000000000L, 0xbbc79ca10c924223L) // -1e-20
      0x81a56e1fc2f8f359L, (0x3ff0000000000000L, 0x0000000000000000L) // -1e-300
      0x01a56e1fc2f8f359L, (0x3ff0000000000000L, 0x0000000000000000L) // 1e-300
      0x3bc79ca10c924223L, (0x3ff0000000000000L, 0x3bc79ca10c924223L) // 1e-20
      0x3ca0000000000000L, (0x3ff0000000000001L, 0xbc9fffffffffffffL) // 2^-53
      0x3e20000000000000L, (0x3ff0000000800000L, 0x3c400000002aaaabL) // 2^-29
      0x3e30000000000000L, (0x3ff0000001000000L, 0x3c60000000555555L) // 2^-28
      0x3e40000000000000L, (0x3ff0000002000000L, 0x3c80000000aaaaabL) // 2^-27
      0x3ee4f8b588e368f1L, (0x3ff0000a7c5e340eL, 0x3c9bf6ba1f2a2657L) // 1e-05
      0x3fb999999999999aL, (0x3ff1aec7b35a00d4L, 0xbc977d498b0e382aL) // 0.1
      0x3fd62e42fefa39efL, (0x3ff6a09e667f3bccL, 0x3c9f68d3de197eeaL) // 0.34657359027997264
      0x3fd62e42fefa39f0L, (0x3ff6a09e667f3bcdL, 0xbc83ed1b76ce8a92L) // 0.3465735902799727
      0x3fe0000000000000L, (0x3ffa61298e1e069cL, 0xbc8b4690082a4906L) // 0.5
      0x3fe62e42fefa39efL, (0x4000000000000000L, 0xbc8abc9e3b39803fL) // 0.6931471805599453
      0x3ff0000000000000L, (0x4005bf0a8b145769L, 0x3ca4d57ee2b1013aL) // 1.0
      0x3ff0a2b23f3bab73L, (0x4006a09e667f3bccL, 0xbc55775499becc39L) // 1.0397207708399179
      0x3ff8000000000000L, (0x4011ed3fe64fc541L, 0x3cb5f6e4658d43eaL) // 1.5
      0x40026bb1bbb55516L, (0x4024000000000001L, 0x3cbc6b626e893394L) // 2.302585092994046
      0x4025000000000000L, (0x40e1bb7015e84d3bL, 0x3d7bc1c4193bcdb9L) // 10.5
      0x4059000000000000L, (0x48f3494a9b171bf5L, 0xc594cf76bdb3376fL) // 100.0
      0x4086280000000000L, (0x7fdd422d2be5dc9bL, 0xfc6916aa7a2c8d07L) // 709.0
      0x40862e42fefa39efL, (0x7fefffffffffff2aL, 0x7c6b0e263400d160L) ] // 709.78271289338397

/// (x, (log(x) hi, lo)) as bits, computed with 120-digit Decimal arithmetic.
let private logReference =
    [ 0x0000000000000001L, (0xc0874385446d71c3L, 0xbd28e569fa8ee781L) // 5e-324
      0x000012688b70e62bL, (0xc0864e69394d9508L, 0xbd035918fe61c196L) // 1e-310
      0x000fffffffffffffL, (0xc086232bdd7abcd2L, 0xbd1f2f3fec1be37fL) // largest subnormal
      0x0010000000000000L, (0xc086232bdd7abcd2L, 0xbd1eef3fec1be37fL) // smallest normal
      0x01a56e1fc2f8f359L, (0xc085963447f87fb5L, 0xbd1aa670d35324e6L) // 1e-300
      0x2b2bff2ee48e0530L, (0xc06cc845b54b54f2L, 0x3d08ed150b29b629L) // 1e-100
      0x3ddb7cdfd9d7bdbbL, (0xc037069e2aa2aa5bL, 0x3cbf0b709e89338fL) // 1e-10
      0x3f50624dd2f1a9fcL, (0xc01ba18a998fffa0L, 0xbcaf25f824141444L) // 0.001
      0x3fd0000000000000L, (0xbff62e42fefa39efL, 0xbc8abc9e3b39803fL) // 0.25
      0x3fe0000000000000L, (0xbfe62e42fefa39efL, 0xbc7abc9e3b39803fL) // 0.5
      0x3fe6a09e667f3bccL, (0xbfd62e42fefa39f1L, 0x3c68d8f957c3d43cL) // 0.7071067811865475
      0x3fe6a09e667f3bcdL, (0xbfd62e42fefa39eeL, 0x3c3716fdfdbc882eL) // 0.7071067811865476
      0x3fe8000000000000L, (0xbfd269621134db92L, 0xbc7e0efadd9db02bL) // 0.75
      0x3feffffde7210be9L, (0xbeb0c6f82d74d230L, 0x3b53962ca9bd6672L) // 0.999999
      0x3feffffe00000000L, (0xbeb0000080000555L, 0xbb5565555622222dL) // 1 - 2^-20
      0x3fefffff00000000L, (0xbea0000040000155L, 0xbb45575555622222L) // 1 - 2^-21
      0x3fefffffffffffffL, (0xbca0000000000000L, 0xb940000000000000L) // 1 - 2^-53
      0x3ff0000000000001L, (0x3cafffffffffffffL, 0x3615555555555554L) // 1 + 2^-52
      0x3ff0000080000000L, (0x3e9fffff800002abL, 0xbb355955553bbbbcL) // 1 + 2^-21
      0x3ff0000100000000L, (0x3eafffff00000aabL, 0xbb45755553bbbbd1L) // 1 + 2^-20
      0x3ff000010c6f7a0bL, (0x3eb0c6f713f33a1dL, 0x3b331c4cf9d0a7deL) // 1.000001
      0x3ff6147ae147ae14L, (0x3fd49d041be0ce2fL, 0x3c7fd19f06b13f90L) // 1.38
      0x3ff6666666666666L, (0x3fd588c2d913348fL, 0xbc7fb3207a09005eL) // 1.4
      0x3ff6a09e667f3bccL, (0x3fd62e42fefa39eeL, 0xbc78d6e518e495a3L) // 1.414213562373095
      0x3ff6a09e667f3bcdL, (0x3fd62e42fefa39f0L, 0x3c7c2e0e1b1548c2L) // 1.4142135623730951
      0x3ff6b851eb851eb8L, (0x3fd6712984ec8f15L, 0xbc73acc7bbe0b21bL) // 1.42
      0x3ff8000000000000L, (0x3fd9f323ecbf984cL, 0xbc4a92e513217f5cL) // 1.5
      0x3fffffffffffffffL, (0x3fe62e42fefa39eeL, 0x3c7abc9e3b39803dL) // 2 - 2^-52
      0x4000000000000000L, (0x3fe62e42fefa39efL, 0x3c7abc9e3b39803fL) // 2.0
      0x4000000000000001L, (0x3fe62e42fefa39f1L, 0x3c7abc9e3b398037L) // 2 + 2^-51
      0x4005bf0a8b145769L, (0x3ff0000000000000L, 0xbc8ea8556644e4cdL) // e
      0x4008000000000000L, (0x3ff193ea7aad030bL, 0xbc9a256f99caabebL) // 3.0
      0x4024000000000000L, (0x40026bb1bbb55516L, 0xbcaf48ad494ea3e9L) // 10.0
      0x408f400000000000L, (0x401ba18a998fffa0L, 0x3cb112fc120a0a22L) // 1000.0
      0x4202a05f20000000L, (0x4037069e2aa2aa5bL, 0xbcbc6b626e89338fL) // 1e10
      0x54b249ad2594c37dL, (0x406cc845b54b54f2L, 0xbd08d863e84d85c7L) // 1e100
      0x7e37e43c8800759cL, (0x4085963447f87fb5L, 0x3d1abccc0710fcd4L) // 1e300
      0x7fefffffffffffffL, (0x40862e42fefa39efL, 0x3d1a9c9e3b39803fL) ] // MaxValue

/// SHA-256 of the bits of Builtins.exp and Builtins.log over a fixed deterministic set of inputs.
/// CI runs this test on Linux and Windows, so any platform divergence fails immediately.
let private digest () =
    let expInputs =
        seq {
            for i in -7500 .. 7500 -> float i * 0.1
            for i in -2000 .. 2000 -> float i * 0.00037
            for e in -1070 .. 9 -> Math.ScaleB(1.2345678901234567, e)
            for e in -1070 .. 9 -> -Math.ScaleB(1.2345678901234567, e)
        }

    let logInputs =
        seq {
            for i in 1..10000 -> float i * 0.00137
            for i in -500 .. 500 -> 1.0 + float i * Math.ScaleB(1.0, -22)
            for e in -1074 .. 1023 -> Math.ScaleB(1.0, e)
            for e in -1070 .. 1020 -> Math.ScaleB(1.4142135623730951, e)
        }

    let bytes =
        seq {
            for x in expInputs do
                yield! BitConverter.GetBytes(toBits (Builtins.exp x))

            for x in logInputs do
                yield! BitConverter.GetBytes(toBits (Builtins.log x))
        }
        |> Array.ofSeq

    Convert.ToHexString(SHA256.HashData bytes).ToLowerInvariant()

[<Tests>]
let tests =
    testList
        "Exp, Log, and Pow 0.5"
        [ test "exp is within one ulp of exact values across normal and subnormal ranges" {
              for xb, expected in expReference do
                  let x = ofBits xb
                  let got = Builtins.exp x
                  Expect.isLessThanOrEqual (ulpError got expected) 1.0 $"exp %.17g{x}"
          }

          test "log is within one ulp of exact values across subnormals, near-1, and full double range" {
              for xb, expected in logReference do
                  let x = ofBits xb
                  let got = Builtins.log x
                  Expect.isLessThanOrEqual (ulpError got expected) 1.0 $"log %.17g{x}"
          }

          test "exp special cases" {
              Expect.equal (toBits (Builtins.exp 0.0)) (toBits 1.0) "exp(0) = 1"
              Expect.equal (toBits (Builtins.exp -0.0)) (toBits 1.0) "exp(-0) = 1"
              Expect.equal (toBits (Builtins.exp Double.NegativeInfinity)) (toBits 0.0) "exp(-inf) = +0"
              Expect.equal (toBits (Builtins.exp Double.PositiveInfinity)) (toBits Double.PositiveInfinity) "exp(+inf) = +inf"
              Expect.isTrue (Double.IsNaN(Builtins.exp nan)) "exp(NaN) = NaN"
              Expect.equal (toBits (Builtins.exp 709.7827128933841)) (toBits Double.PositiveInfinity) "overflow -> +inf"
              Expect.equal (toBits (Builtins.exp 1000.0)) (toBits Double.PositiveInfinity) "large positive -> +inf"
              Expect.equal (toBits (Builtins.exp -745.1332191019412)) (toBits 0.0) "underflow -> +0"
              Expect.equal (toBits (Builtins.exp -1000.0)) (toBits 0.0) "large negative -> +0"
          }

          test "log special cases" {
              Expect.equal (toBits (Builtins.log 1.0)) (toBits 0.0) "log(1) = +0"
              Expect.equal (toBits (Builtins.log 0.0)) (toBits Double.NegativeInfinity) "log(0) = -inf"
              Expect.equal (toBits (Builtins.log -0.0)) (toBits Double.NegativeInfinity) "log(-0) = -inf"
              Expect.equal (toBits (Builtins.log Double.PositiveInfinity)) (toBits Double.PositiveInfinity) "log(+inf) = +inf"
              Expect.isTrue (Double.IsNaN(Builtins.log -1.0)) "log(-1) = NaN"
              Expect.isTrue (Double.IsNaN(Builtins.log -1e-300)) "log(-tiny) = NaN"
              Expect.isTrue (Double.IsNaN(Builtins.log Double.NegativeInfinity)) "log(-inf) = NaN"
              Expect.isTrue (Double.IsNaN(Builtins.log nan)) "log(NaN) = NaN"
          }

          test "Builtins.apply routes Exp and Log to deterministic implementations" {
              for x in [ -10.5; -0.5; 0.0; 0.5; 1.0; 2.5; 10.0 ] do
                  Expect.equal (toBits (Builtins.apply Builtins.Exp [| x |])) (toBits (Builtins.exp x)) $"apply Exp %g{x}"

              for x in [ 1e-10; 0.5; 1.0; 1.414; 2.0; 10.0; 1e100 ] do
                  Expect.equal (toBits (Builtins.apply Builtins.Log [| x |])) (toBits (Builtins.log x)) $"apply Log %g{x}"
          }

          test "Eval.eval special-cases x ^ 0.5 to IEEE-754 exact sqrt for x >= 0" {
              let evalPow x y = Eval.eval [||] (CBin(Pow, CNum x, CNum y))

              for x in [ 0.0; 0.21; 0.5; 1.0; 2.0; 3.0; 42.0; 137.5; 1e200; 1e-200; Double.PositiveInfinity ] do
                  Expect.equal (toBits (evalPow x 0.5)) (toBits (sqrt x)) $"%g{x}^0.5 == sqrt(%g{x})"

              Expect.equal (toBits (evalPow -0.0 0.5)) (toBits 0.0) "(-0.0)^0.5 == +0.0"
              Expect.equal (toBits (evalPow Double.NegativeInfinity 0.5)) (toBits Double.PositiveInfinity) "(-inf)^0.5 == +inf"
              Expect.equal (evalPow 2.0 3.0) 8.0 "other exponents unchanged"

              Expect.throwsT<Eval.EvalError>
                  (fun () -> evalPow -1.0 0.5 |> ignore)
                  "negative base ^ 0.5 raises EvalError (NaN)"
          }

          test "exp accuracy across the k = -1021 / -1022 normal-to-subnormal scaling split" {
              for x in [ -707.5; -708.05; -708.3964185322641; -708.5; -709.0 ] do
                  let got = Builtins.exp x
                  let expected = Math.Exp x
                  Expect.isLessThanOrEqual (abs (toBits got - toBits expected)) 2L $"exp %.17g{x}"
          }

          test "exp and log stay within 2 ulps of system Math across a broad sweep" {
              let ulpDistance (a: float) (b: float) =
                  if a = b then
                      0L
                  elif Double.IsNaN a || Double.IsNaN b || Double.IsInfinity a || Double.IsInfinity b then
                      Int64.MaxValue
                  elif (a < 0.0) <> (b < 0.0) then
                      Int64.MaxValue
                  else
                      abs (toBits a - toBits b)

              for i in -7450 .. 7090 do
                  let x = float i * 0.1
                  Expect.isLessThanOrEqual (ulpDistance (Builtins.exp x) (Math.Exp x)) 2L $"exp %.1f{x}"

              for i in 1..2000 do
                  let x = float i * 0.0137
                  Expect.isLessThanOrEqual (ulpDistance (Builtins.log x) (Math.Log x)) 2L $"log %.4f{x}"

              for e in -1074 .. 1023 do
                  let x = Math.ScaleB(1.37, e)
                  Expect.isLessThanOrEqual (ulpDistance (Builtins.log x) (Math.Log x)) 2L $"log 1.37*2^%d{e}"
          }

          test "exp and log produce the same bits on every platform" {
              Expect.equal (digest ()) "e288f4ed0c433a13d8fad4d89d394f8ee78ac8438ae7b259c1ffb468cf497b3a" "exp or log changed, or differs on this platform"
          } ]
