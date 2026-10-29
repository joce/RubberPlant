module RubberPlant.Core.Tests.TrigTests

open System
open System.Security.Cryptography
open Expecto
open RubberPlant

let private toBits (x: float) = BitConverter.DoubleToInt64Bits x
let private ofBits (b: int64) = BitConverter.Int64BitsToDouble b

/// Distance in ulps between two finite doubles of the same sign.
let private ulps (a: float) (b: float) = abs (toBits a - toBits b)

/// (angle, sin, cos) as bits. The expected values are the exact sine and cosine
/// rounded to the nearest double, computed with mpmath at 200 bits of precision
/// (sinpi and cospi of (angle mod 360) / 180, so quarter turns are exact).
let private reference =
    [
        0x3fe0000000000000L, 0x3f81df37c4954c21L, 0x3fefffb02599c9cdL // 0.5
        0x3ff0000000000000L, 0x3f91df0b2b89dd1eL, 0x3feffec097f5af8aL // 1.0
        0x4024000000000000L, 0x3fc63a1a7e0b738aL, 0x3fef838b8c811c17L // 10.0
        0x4032000000000000L, 0x3fd3c6ef372fe950L, 0x3fee6f0e134454ffL // 18.0
        0x4032f33333333333L, 0x3fd4c8967c7e5d8fL, 0x3fee44033e0e1fbaL // 18.95
        0x4036800000000000L, 0x3fd87de2a6aea963L, 0x3fed906bcf328d46L // 22.5
        0x4039b33333333333L, 0x3fdbc112087aacdcL, 0x3fecd59f7a382ce0L // 25.7
        0x403c000000000000L, 0x3fde0bd274245078L, 0x3fec411b4f6d2708L // 28.0
        0x403e000000000000L, 0x3fe0000000000000L, 0x3febb67ae8584caaL // 30.0
        0x4041800000000000L, 0x3fe25abcf87c4978L, 0x3fea367e59158747L // 35.0
        0x4042000000000000L, 0x3fe2cf2304755a5eL, 0x3fe9e3779b97f4a8L // 36.0
        0x40467ffffffdda3fL, 0x3fe6a09e667d8994L, 0x3fe6a09e6680ee05L // 44.999999999
        0x4046800000000000L, 0x3fe6a09e667f3bcdL, 0x3fe6a09e667f3bcdL // 45.0
        0x40468000000225c1L, 0x3fe6a09e6680ee05L, 0x3fe6a09e667d8994L // 45.000000001
        0x404e000000000000L, 0x3febb67ae8584caaL, 0x3fe0000000000000L // 60.0
        0x4052000000000000L, 0x3fee6f0e134454ffL, 0x3fd3c6ef372fe950L // 72.0
        0x4055400000000000L, 0x3fefe0d3b41815a2L, 0x3fb64fd6b8c28103L // 85.0
        0x40567ffffff54339L, 0x3ff0000000000000L, 0x3de7fcd3d8cadfbdL // 89.99999999
        0x4057af5c28f5c28fL, 0x3fefe3fb9d4f7bbfL, 0xbfb527850b07ee32L // 94.74
        0x405e000000000000L, 0x3febb67ae8584caaL, 0xbfe0000000000000L // 120.0
        0x40609428f5c28f5cL, 0x3fe78b340321e871L, 0xbfe5ac1fe1de3732L // 132.63
        0x4061300000000000L, 0x3fe59e6f5ae6a0a7L, 0xbfe797c6a435ce85L // 137.5
        0x4062c00000000000L, 0x3fe0000000000000L, 0xbfebb67ae8584caaL // 150.0
        0x40667fffffca501bL, 0x3e1dfc09ecf201d2L, 0xbff0000000000000L // 179.9999999
        0x406680000035afe5L, 0xbe1dfc09ecf201d2L, 0xbff0000000000000L // 180.0000001
        0x4069000000000000L, 0xbfd5e3a8748a0bf5L, 0xbfee11f642522d1cL // 200.0
        0x406bd00000000000L, 0xbfe59e6f5ae6a0a7L, 0xbfe797c6a435ce85L // 222.5
        0x4070dffff583a53cL, 0xbfefffffffffff77L, 0xbe876ce7d7742995L // 269.99999
        0x4072c00000000000L, 0xbfebb67ae8584caaL, 0x3fe0000000000000L // 300.0
        0x40767fffffffffffL, 0xbcd1df46a2529d39L, 0x3ff0000000000000L // 359.99999999999994
        0x3ddb7cdfd9d7bdbbL, 0x3d7eb443a0930a1fL, 0x3ff0000000000000L // 1e-10
        0x01a56e1fc2f8f359L, 0x0147f00f350a8dddL, 0x3ff0000000000000L // 1e-300
        0x0000000000000001L, 0x0000000000000000L, 0x3ff0000000000000L // 5e-324
        0x4202a05f20000000L, 0xbfef838b8c811c17L, 0x3fc63a1a7e0b738aL // 10000000000.0
        0x40fe240c9fbe76c9L, 0xbfd9393c3aa77f72L, 0x3fed68efde6f84a3L // 123456.789
        0x4415af1d78b58c40L, 0xbfef838b8c811c17L, 0x3fc63a1a7e0b738aL // 1e+20
        0x7e37e43c8800759cL, 0x0000000000000000L, 0x3ff0000000000000L // 1e+300
        0x405d251b5de9da2bL, 0x3fec9e345cf3114eL, 0xbfdca2eb19742f55L // 116.57979533993846
        0x404b272143622eeeL, 0x3fe9fd1062bb354dL, 0x3fe2abb51b90496aL // 54.305702612820696
        0x406d4ac3df85884fL, 0xbfe9ff9feeaf5895L, 0xbfe2a8241c2c84a0L // 234.33641029434736
        0x403a13ba69f51468L, 0x3fdc22129dcdabc8L, 0x3fecbe1585651bbbL // 26.077063200315393
        0x40681d5c56278c19L, 0xbfcc9d3a2f9693a6L, 0xbfef30af1dac996aL // 192.9175215504081
        0x406074bc7fa736f8L, 0x3fe7e96702743043L, 0xbfe54403c865b9ebL // 131.64801008853078
        0x4034e12e4fefca70L, 0x3fd6cf594b168b13L, 0x3fede60adc24e097L // 20.879612918894452
        0x4066d5a8de952a56L, 0xbfa7e977d22b1c95L, 0xbfeff70f9e946844L // 182.67686394819128
        0x402aff3323bc3964L, 0x3fcde0ab232e89a8L, 0x3fef1db4e0a01c04L // 13.498437039114556
        0x40638399289a2afbL, 0x3fd9ea95f384bcd3L, 0xbfed424a07a9841fL // 156.1124461184589
        0x403925e036d4700bL, 0x3fdb327f8f91f31dL, 0x3fecf782365191b2L // 25.147952486862817
        0x4040540e3f66083cL, 0x3fe1446f964514b6L, 0x3feaf10007779c04L // 32.65668480379142
        0x40631a7607f51e7eL, 0x3fdd3a3f7cf17a84L, 0xbfec77db50674ce6L // 152.82690809130503
        0x40729aab11a7206dL, 0xbfec575a2bcad502L, 0x3fddb78e661fcaf1L // 297.6667648819337
        0x404648cb5bd14f6eL, 0x3fe674d9e271168aL, 0x3fe6cc0ee3dc6e36L // 44.568706013872415
        0x4054176cfd9833deL, 0x3fef8c77d312997aL, 0x3fc56bd5a6c978cdL // 80.36602725852524
        0x406c3c07dd6783f9L, 0xbfe6f87f867368ebL, 0xbfe64762acb063ebL // 225.87596006601214
        0x407552cdb2bc8a69L, 0xbfd4a6b571cd3c45L, 0x3fee49cee630cecbL // 341.17521928452203
        0x4069f839d90b2dfcL, 0xbfddce6a4f462e44L, 0xbfec51593be739f4L // 207.7570615022995
        0x4061d9c25247689aL, 0x3fe3584f5044a406L, 0xbfe97d9a4ee155dfL // 142.80497087428085
        0x4075f73aba7f2dcdL, 0xbfc306a8a83047b2L, 0x3fefa4ff4cae83cbL // 351.4518380134512
        0x4030c50f520cc78bL, 0x3fd277382f8b76ccL, 0x3feea39c14ce1d49L // 16.76976502239226
        0x407350c7403fe2c8L, 0xbfe8da0019a3ccbcL, 0x3fe428cbc48cd537L // 309.04864525752464
        0x405a109913b71ed5L, 0x3fef039c55bae7c6L, 0xbfcf87214691c31fL // 104.25934307940345
        0x4049f74634aa59c4L, 0x3fe93160fa2ca20cL, 0x3fe3bb2d00a14f71L // 51.93183000867751
        0x404533ddc7d8767dL, 0x3fe5946f4cebd209L, 0x3fe7a0ec9b33bc95L // 42.40520570821261
        0x405bc36bd58d8a7bL, 0x3feddd26032127cfL, 0xbfd6fdc4d11dc30bL // 111.05345667669637
        0x40725ce348bb0abdL, 0xbfed470868847581L, 0x3fd9d51f4f617266L // 293.80548928321133
        0x405043ef9028cce8L, 0x3fed042f5f4280a9L, 0x3fdafc3c65a92fdeL // 65.06149677261749
        0x406a2c08acb688c8L, 0xbfdf650083bc7a4fL, 0xbfebe2a9cdda2275L // 209.37605891848784
        0x406cc0487d4e88c6L, 0xbfe8843fce89cb09L, 0xbfe490bf31aa3f0cL // 230.00884881342625
        0x4060c2050a8c4cceL, 0x3fe6fe8f36622ffbL, 0xbfe64121192eadfdL // 134.06311538126323
        0x4068a60428a17082L, 0xbfd2e99acc56d5d6L, 0xbfee9225af0a0c08L // 197.1880076554408
        0x40369aa1c66585d2L, 0x3fd8995bd4ea8de8L, 0x3fed8ab772c435b9L // 22.604030990396332
        0x403574d804dafd8cL, 0x3fd76928ef4593e3L, 0x3fedc843191e2b99L // 21.45642118784376
        0x40528949eb15ac78L, 0x3feec859b15f3dd6L, 0x3fd17c221afe10c5L // 74.14513661495755
        0x406e9e352b3dad59L, 0xbfecfd15e155f044L, 0xbfdb1ab09e9fb613L // 244.94399034544293
        0x40633ddd053f359dL, 0x3fdc1f6dc7d7313eL, 0xbfecbebb1acab5dbL // 153.93323004098502
        0x405c45f367fceef1L, 0x3fed6f9244e625e7L, 0xbfd91a348600c19eL // 113.09298133564495
        0x406a59ac33f1c16eL, 0xbfe062ef1ce31b8dL, 0xbfeb7c6ee7714d0dL // 210.80227086274994
        -0x3f3db67008fbc788L, 0xbfabe8b2d272ab60L, 0x3feff3d209aea542L // -9363.12472584493
        -0x3f1c722cc7a95542L, 0xbfeff196035b629bL, 0x3fae5bf8fa6636afL // -40046.60062726353
        0x40ecbf7cae86c564L, 0xbfd18788113f7810L, 0xbfeec6bac36773cbL // 58875.89630449822
        0x40e36edc6038f7b4L, 0xbfd4b77a23d58a24L, 0xbfee46f1e4198459L // 39798.886745914264
        -0x3f170269ab2acc00L, 0xbfebe7d767cd172bL, 0x3fdf5293ac25e90dL // -51180.69785556942
        0x40cd125efb8d1d28L, 0x3fea4b945411ea75L, 0xbfe23c7acd63608cL // 14884.742051734196
        0x40b3af4cfec1e8b0L, 0xbf88fe40881f5b5cL, 0x3fefff63d45b820fL // 5039.300762290295
        0x40f25137fc5fae46L, 0x3fe131aa471a90a6L, 0xbfeafcfea57120d6L // 75027.49911468578
        0x40e66821da379b58L, 0x3fc84bed791fe468L, 0xbfef6b10a69583f2L // 45889.05788784352
        -0x3f1b4a71b1feff34L, 0x3fed93510c26d41aL, 0x3fd86fe2f39f6741L // -42412.4470219627
        0x40f7722f8310e058L, 0xbfefe134b5c33ef0L, 0x3fb62d1520e5c5f3L // 96034.96949851641
        -0x3f0d59d27d8be521L, 0xbfed6c0d69859202L, 0x3fd92aaf9eb58216L // -76386.84434900757
        -0x3f3004483cda06b8L, 0xbfb45f47261387f7L, 0xbfefe6050adb1af4L // -16375.435642954559
        0x40e91c85f2ffeca4L, 0xbfe926fc2a4544c3L, 0x3fe3c86b2a615f63L // 51428.18591304988
        -0x3f0f01ce82cb3c14L, 0xbfeacf22081d4a35L, 0xbfe178d641bf9520L // -69603.09306789906
        -0x3f5ec13d7d1ceae0L, 0xbfe78c2985d5878fL, 0x3fe5ab15202f63a3L // -2207.3799048388755
        -0x3f09801738f927a1L, 0x3f99efe3804fe98bL, 0x3feffd5f2924b9d4L // -92158.54859051247
        0x40e06d657b589060L, 0x3fd28759e25e94ffL, 0xbfeea12cb263e496L // 33643.17130687903
        0x40e9d6458b33fc98L, 0xbfb9fd4614c240f2L, 0x3fefd5acde3d5259L // 52914.173242562625
        0x40cc86981233ad50L, 0xbfdb3ce092d3de1fL, 0xbfecf511f8705e45L // 14605.188055476785
    ]

/// SHA-256 of the bits of sinCosDeg over a fixed set of angles. It pins the exact
/// results: CI runs this test on Linux and Windows, so any platform difference fails.
let private digest () =
    let angles =
        seq {
            for i in -36000 .. 36000 -> float i / 100.0 // 0.01° steps over two turns
            for i in 0..9999 -> float i * 0.0137 + 1e-7 // irregular angles
            for e in -1070 .. 1020 -> Math.ScaleB(1.2345678901234567, e) // tiny to huge, exact
        }

    let bytes =
        angles
        |> Seq.collect (fun a ->
            let struct (s, c) = Builtins.sinCosDeg a
            Seq.append (BitConverter.GetBytes(toBits s)) (BitConverter.GetBytes(toBits c)))
        |> Array.ofSeq

    Convert.ToHexString(SHA256.HashData bytes).ToLowerInvariant()

[<Tests>]
let tests =
    testList
        "Trigonometry"
        [ test "within one ulp of the exact values" {
              for a, s, c in reference do
                  let angle = ofBits a
                  let struct (gs, gc) = Builtins.sinCosDeg angle
                  Expect.isLessThanOrEqual (ulps gs (ofBits s)) 1L $"sin %.17g{angle}"
                  Expect.isLessThanOrEqual (ulps gc (ofBits c)) 1L $"cos %.17g{angle}"
          }

          test "same bits on every platform" {
              Expect.equal (digest ()) "c2ed9fbc0965fff30ee5667bb1cb4ef0a2f88fbf543666bcec79ed7cde2c6a33" "sinCosDeg changed, or differs on this platform"
          }

          test "exact at multiples of 90 degrees, with +0 for zeros" {
              for k in -8 .. 8 do
                  let a = 90.0 * float k
                  let struct (s, c) = Builtins.sinCosDeg a
                  let es, ec = [| 0.0, 1.0; 1.0, 0.0; 0.0, -1.0; -1.0, 0.0 |].[((k % 4) + 4) % 4]
                  Expect.equal (toBits s) (toBits es) $"sin %g{a}"
                  Expect.equal (toBits c) (toBits ec) $"cos %g{a}"
          }

          test "sin is odd and cos is even, exactly" {
              for i in 0..20000 do
                  let a = float i * 0.0731
                  let struct (s, c) = Builtins.sinCosDeg a
                  let struct (ns, nc) = Builtins.sinCosDeg -a
                  Expect.equal (toBits ns) (toBits (-s + 0.0)) $"sin -%g{a}"
                  Expect.equal (toBits nc) (toBits c) $"cos -%g{a}"
          }

          test "sinDeg, cosDeg and tanDeg agree with sinCosDeg" {
              for a in [ 0.0; 18.95; 137.5; -222.5; 1e20 ] do
                  let struct (s, c) = Builtins.sinCosDeg a
                  Expect.equal (toBits (Builtins.sinDeg a)) (toBits s) ""
                  Expect.equal (toBits (Builtins.cosDeg a)) (toBits c) ""
                  Expect.equal (toBits (Builtins.tanDeg a)) (toBits (s / c)) ""
          }

          test "tan is infinite at odd multiples of 90 degrees" {
              Expect.equal (Builtins.tanDeg 90.0) Double.PositiveInfinity ""
              Expect.equal (Builtins.tanDeg -90.0) Double.NegativeInfinity ""
              Expect.equal (Builtins.tanDeg 270.0) Double.NegativeInfinity ""
              Expect.equal (Builtins.tanDeg 0.0) 0.0 ""
              Expect.isLessThanOrEqual (ulps (Builtins.tanDeg 45.0) 1.0) 1L ""
          }

          test "large angles are reduced exactly" {
              for a in [ 1e20; 123456.789; 1e300; 7.5e15 ] do
                  let struct (s, c) = Builtins.sinCosDeg a
                  let struct (rs, rc) = Builtins.sinCosDeg(a % 360.0)
                  Expect.equal (toBits s) (toBits rs) $"sin %g{a}"
                  Expect.equal (toBits c) (toBits rc) $"cos %g{a}"
          }

          test "infinite and NaN angles give NaN" {
              for a in [ Double.PositiveInfinity; Double.NegativeInfinity; nan ] do
                  let struct (s, c) = Builtins.sinCosDeg a
                  Expect.isTrue (Double.IsNaN s && Double.IsNaN c) $"%g{a}"
          } ]
