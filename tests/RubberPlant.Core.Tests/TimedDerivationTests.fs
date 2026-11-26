module RubberPlant.Tests.TimedDerivationTests

open System
open Expecto
open RubberPlant
open RubberPlant.Model
open RubberPlant.Tests.TestUtil
open RubberPlant.Tests.CheckerTests

let private deriveAt (t: float) (src: string) =
    let m, _ = compileOk src
    let m = { m with Settings = { m.Settings with Time = Some t } }

    match TimedDerivation.derive m with
    | Ok s -> LString.format s
    | Error ds -> failtestf "timed derivation failed:\n%s" (formatDiags ds)

let private deriveRawAt (t: float) (src: string) =
    let m, _ = compileOk src
    let m = { m with Settings = { m.Settings with Time = Some t } }

    match TimedDerivation.deriveWithTrace Limits.defaults m with
    | Ok r -> r
    | Error ds -> failtestf "timed derivation failed:\n%s" (formatDiags ds)

let private drawAt (t: float) (src: string) =
    let m, _ = compileOk src
    let m = { m with Settings = { m.Settings with Time = Some t } }

    match Pipeline.run m with
    | Ok o -> LString.format o.Drawing
    | Error ds -> failtestf "pipeline failed:\n%s" (formatDiags ds)

let private expectCloseLString (actual: LString.LString) (expected: LString.LString) (msg: string) =
    Expect.equal actual.Length expected.Length $"%s{msg}: length"

    for i in 0 .. actual.Length - 1 do
        match actual[i], expected[i] with
        | LString.Push, LString.Push
        | LString.Pop, LString.Pop
        | LString.Cut, LString.Cut -> ()
        | LString.Mod(n1, a1), LString.Mod(n2, a2) ->
            Expect.equal n1 n2 $"%s{msg}: module name at %d{i}"
            Expect.equal a1.Length a2.Length $"%s{msg}: arity at %d{i}"

            for j in 0 .. a1.Length - 1 do
                Expect.floatClose Accuracy.veryHigh a1[j] a2[j] $"%s{msg}: arg %d{j} at %d{i}"
        | a, b -> failtestf "%s: mismatch at %d: %A vs %A" msg i a b

let axiomsAndExamples =
    testList
        "ABOP Chapter 6 axioms & models"
        [ test "identity at t = 0: D(omega, 0) = omega including boundary tau = beta" {
              let src =
                  """
axiom: a(1) b(0.25) [ +(45) s(2) ];
a at 1 -> a(0) b(0);
b at 1 -> a(0);
"""

              Expect.equal (deriveAt 0.0 src) "a(1)b(0.25)[+(45)s(2)]" "t = 0 preserves axiom"
              // Just past t = 0, a(1) divides immediately via A3
              let s, trace = deriveRawAt 0.25 src
              Expect.equal (LString.format s) "a(0.25)b(0.25)b(0.5)[+(45)s(2.25)]" "t = 0.25 divides a(1)"
              Expect.equal trace.TotalRuleFirings 1 "one division"
          }

          test "ABOP eq 6.1: Anabaena catenula symmetric timed cell division" {
              let src =
                  """
axiom: ar(0);
ar at 1 -> al(0) br(0);
al at 1 -> bl(0) ar(0);
br at 1 -> ar(0);
bl at 1 -> al(0);
"""

              Expect.equal (deriveAt 0.5 src) "ar(0.5)" "t = 0.5"
              // At tau = 0, t = 1.0 -> tau + t = 1.0 <= 1 -> A2 keeps ar(1)
              Expect.equal (deriveAt 1.0 src) "ar(1)" "boundary tau + t <= beta stays A2"
              Expect.equal (deriveAt 1.5 src) "al(0.5)br(0.5)" "t = 1.5"
              Expect.equal (deriveAt 2.5 src) "bl(0.5)ar(0.5)ar(0.5)" "t = 2.5"
              Expect.equal (deriveAt 3.5 src) "al(0.5)al(0.5)br(0.5)al(0.5)br(0.5)" "t = 3.5"
              Expect.equal (deriveAt 4.5 src) "bl(0.5)ar(0.5)bl(0.5)ar(0.5)ar(0.5)bl(0.5)ar(0.5)ar(0.5)" "t = 4.5"
          }

          test "ABOP eq 6.2: asymmetric terminal ages and non-zero successor ages" {
              let src =
                  """
axiom: a(0);
a at 1 -> a(0) b(0.5);
b at 1 -> a(0);
"""

              let s1, t1 = deriveRawAt 1.25 src
              Expect.equal (LString.format s1) "a(0.25)b(0.75)" "t = 1.25"
              Expect.equal t1.TotalRuleFirings 1 "1 division at t = 1.25"

              let s2, t2 = deriveRawAt 1.75 src
              Expect.equal (LString.format s2) "a(0.75)a(0.25)" "t = 1.75: b(0.5) divided at 1.5"
              Expect.equal t2.TotalRuleFirings 2 "2 divisions at t = 1.75"
              Expect.equal t2.RuleFirings[0].Count 1 "a fired once"
              Expect.equal t2.RuleFirings[1].Count 1 "b fired once"
          }

          test "ABOP §6.2.1-6.2.2: branching, non-dividing internodes s, and continuous as growth" {
              let src =
                  """
settings { time = 2.5; }
axiom: a(0);
a at 1 -> [ +(45) s(0) b(0) ] s(0) a(0);
b at 2 -> s(0);
a(t) as F(t);
s(t) as F(2 * (1 - exp(-t)));
b(t) as F(t);
"""

              let m, _ = compileOk src
              let output = Expect.wantOk (Pipeline.run m) "run"
              // t = 2.5:
              // a(0) divides at 1.0 -> [+(45) s(1.5) b(1.5)] s(1.5) (a(0) at t=1.5)
              // inner a(0) at t=1.5 divides at 1.0 -> [+(45) s(0.5) b(0.5)] s(0.5) a(0.5)
              Expect.equal
                  (LString.format output.Derived)
                  "[+(45)s(1.5)b(1.5)]s(1.5)[+(45)s(0.5)b(0.5)]s(0.5)a(0.5)"
                  "derived string with branching and non-dividing s"

              Expect.equal output.DerivationTrace.Steps 0 "steps is 0"
              Expect.equal output.DerivationTrace.Time (Some 2.5) "time is 2.5"
              Expect.isFalse output.DerivationTrace.FixedPointReached "fixed_point_reached is false"
              Expect.equal output.DerivationTrace.TotalRuleFirings 2 "2 divisions"

              Expect.equal
                  output.DerivationTrace.RuleFirings
                  [ { Line = 4; Predecessor = "a"; Arity = 1; Count = 2 }
                    { Line = 5; Predecessor = "b"; Arity = 1; Count = 0 } ]
                  "rule firings in source order including unused rule b"

              // Verify interpretation uses deterministic exp
              let drawing = LString.format output.Drawing
              Expect.stringStarts drawing "[+(45)F(" "interpreted drawing"
              Expect.stringContains (drawAt 2.5 src) "F(0.5)" "apex age drawn"
          }

          test "empty successor erases timed module cleanly" {
              let src =
                  """
axiom: a(0) b(0);
a at 1 -> empty;
b at 2 -> b(0);
"""

              Expect.equal (deriveAt 1.5 src) "b(1.5)" "a erased after 1"
          }

          test "turtle symbols pass through untimed without aging" {
              let src =
                  """
axiom: !(2) +(30) F(5) a(0) '(3);
a at 1 -> [ -(45) F a(0) ];
"""

              Expect.equal (deriveAt 1.5 src) "!(2)+(30)F(5)[-(45)F a(0.5)]'(3)" "turtle symbols preserved"
          } ]

let semigroupProperty =
    testList
        "semigroup theorem D(D(x, t_a), t_b) = D(x, t_a + t_b)"
        [ test "exact semigroup equality on dyadic floats with branching and non-dividing modules" {
              let src =
                  """
axiom: a(0.5) [ -(30) s(0.25) b(0.75) ];
a at 1 -> a(0) [ +(45) s(0) b(0.25) ];
b at 1.5 -> s(0) a(0.25);
"""

              let m, _ = compileOk src
              let pairs = [ 0.0, 0.0; 0.0, 1.25; 0.5, 0.5; 0.75, 1.5; 1.25, 2.25; 0.125, 3.875 ]

              for ta, tb in pairs do
                  let mA = { m with Settings = { m.Settings with Time = Some ta } }
                  let first = Expect.wantOk (TimedDerivation.derive mA) $"D(x, %g{ta})"

                  let mB = { m with Axiom = first; Settings = { m.Settings with Time = Some tb } }
                  let composed = Expect.wantOk (TimedDerivation.derive mB) $"D(D(x, %g{ta}), %g{tb})"

                  let mSum = { m with Settings = { m.Settings with Time = Some(ta + tb) } }
                  let direct = Expect.wantOk (TimedDerivation.derive mSum) $"D(x, %g{ta + tb})"

                  Expect.equal (LString.format composed) (LString.format direct) $"semigroup (%g{ta}, %g{tb})"
          }

          test "semigroup property on non-dyadic floats within floating-point precision" {
              let src =
                  """
axiom: a(0.1) b(0.2) s(0.3);
a at 0.7 -> b(0.1) a(0.3);
b at 1.1 -> a(0.2) s(0);
"""

              let m, _ = compileOk src
              let pairs = [ 0.3, 0.7; 0.4, 1.3; 1.23, 2.47 ]

              for ta, tb in pairs do
                  let mA = { m with Settings = { m.Settings with Time = Some ta } }
                  let first = Expect.wantOk (TimedDerivation.derive mA) "first"
                  let mB = { m with Axiom = first; Settings = { m.Settings with Time = Some tb } }
                  let composed = Expect.wantOk (TimedDerivation.derive mB) "composed"
                  let mSum = { m with Settings = { m.Settings with Time = Some(ta + tb) } }
                  let direct = Expect.wantOk (TimedDerivation.derive mSum) "direct"

                  expectCloseLString composed direct $"semigroup (%g{ta}, %g{tb})"
          } ]

let budgetsAndSafety =
    testList
        "budgets, stack safety & runtime errors"
        [ test "division-budget DoS guard stops tiny terminal ages with RP0206" {
              let m, _ = compileOk "settings { time = 1.0; }\naxiom: a(0);\na at 1e-12 -> a(0);\n"

              match TimedDerivation.derive m with
              | Error [ d ] -> Expect.equal d.Code Codes.ModuleLimitExceeded "RP0206 on billion-division DoS"
              | r -> failtestf "expected RP0206, got %A" r

              // Exact division boundary check: MaxModules = 3 allows 3 divisions (t = 3.5), rejects 4 (t = 4.5)
              let mLin, _ = compileOk "axiom: a(0);\na at 1 -> a(0);\n"
              let lim3 = { Limits.defaults with MaxModules = Some 3 }

              Expect.isOk
                  (TimedDerivation.deriveWith lim3 { mLin with Settings = { mLin.Settings with Time = Some 3.5 } })
                  "3 divisions fit in MaxModules = 3"

              match TimedDerivation.deriveWith lim3 { mLin with Settings = { mLin.Settings with Time = Some 4.5 } } with
              | Error [ d ] -> Expect.equal d.Code Codes.ModuleLimitExceeded "4 divisions exceed MaxModules = 3"
              | r -> failtestf "expected RP0206, got %A" r
          }

          test "output-length budget and axiom/zero/negative limits" {
              let mGrow, _ = compileOk "settings { time = 3.5; }\naxiom: a(0);\na at 1 -> a(0) a(0);\n"
              // 3 generations -> 7 divisions, 8 symbols
              Expect.isOk (TimedDerivation.deriveWith { Limits.defaults with MaxModules = Some 8 } mGrow) "exact limit 8"

              match TimedDerivation.deriveWith { Limits.defaults with MaxModules = Some 7 } mGrow with
              | Error [ d ] -> Expect.equal d.Code Codes.ModuleLimitExceeded "limit 7 exceeded"
              | r -> failtestf "expected RP0206, got %A" r

              let mEmpty, _ = compileOk "settings { time = 1.0; }\naxiom: empty;"
              Expect.isOk (TimedDerivation.deriveWith { Limits.defaults with MaxModules = Some 0 } mEmpty) "empty fits in 0"

              match TimedDerivation.deriveWith { Limits.defaults with MaxModules = Some -1 } mEmpty with
              | Error [ d ] -> Expect.equal d.Code Codes.ModuleLimitExceeded "negative fails on empty"
              | r -> failtestf "expected RP0206, got %A" r
          }

          test "deep iterative stack does not overflow OS stack" {
              // Head-recursive expansion pushes continuations onto explicit stack (depth = 25,000)
              let mHead, _ = compileOk "settings { time = 25000.5; }\naxiom: a(0);\na at 1 -> a(0) s(0);\n"
              let res = Expect.wantOk (TimedDerivation.deriveWith Limits.unlimited mHead) "deep head recursion"
              Expect.equal res.Length 25001 "1 a + 25000 s"

              // Tail-recursive expansion runs in O(1) stack space
              let mTail, _ = compileOk "settings { time = 50000.5; }\naxiom: a(0);\na at 1 -> s(0) a(0);\n"
              let resTail = Expect.wantOk (TimedDerivation.deriveWith Limits.unlimited mTail) "deep tail recursion"
              Expect.equal resTail.Length 50001 "50000 s + 1 a"
          }

          test "invalid evaluation time or age overflow returns RP0205" {
              let m, _ = compileOk "axiom: s(1e308);"

              for bad in [ -1.0; Double.PositiveInfinity; Double.NaN ] do
                  match TimedDerivation.derive { m with Settings = { m.Settings with Time = Some bad } } with
                  | Error [ d ] -> Expect.equal d.Code Codes.InvalidGeometry $"time=%g{bad} -> RP0205"
                  | r -> failtestf "expected RP0205, got %A" r

              // Age overflow on non-dividing module
              match TimedDerivation.derive { m with Settings = { m.Settings with Time = Some 1e308 } } with
              | Error [ d ] -> Expect.equal d.Code Codes.InvalidGeometry "age overflow -> RP0205"
              | r -> failtestf "expected RP0205, got %A" r
          } ]

[<Tests>]
let tests =
    testList "TimedDerivation" [ axiomsAndExamples; semigroupProperty; budgetsAndSafety ]
