module RubberPlant.Tests.DerivationTests

open Expecto
open RubberPlant
open RubberPlant.Model
open RubberPlant.Tests.TestUtil
open RubberPlant.Tests.CheckerTests

/// Every derivation step, formatted.
let steps (src: string) =
    let m, _ = compileOk src

    match Derivation.deriveSteps m with
    | Ok ss -> ss |> List.map LString.format
    | Error ds -> failtestf "derivation failed:\n%s" (formatDiags ds)

let derived src = steps src |> List.last

/// The derived string with this seed, a run parameter set by the host (§4.7).
let derivedWithSeed (seed: int64) (src: string) =
    let m, _ = compileOk src

    match Derivation.derive { m with Settings = { m.Settings with Seed = seed } } with
    | Ok s -> LString.format s
    | Error ds -> failtestf "derivation failed:\n%s" (formatDiags ds)

let drawing (src: string) =
    let m, _ = compileOk src

    match Derivation.derive m |> Result.bind (Derivation.interpret m) with
    | Ok s -> LString.format s
    | Error ds -> failtestf "failed:\n%s" (formatDiags ds)

let runtimeError (src: string) =
    let m, _ = compileOk src

    match Derivation.derive m with
    | Ok s -> failtestf "expected a runtime error, got %s" (LString.format s)
    | Error [ d ] -> d
    | Error ds -> failtestf "expected one error, got:\n%s" (formatDiags ds)

let count (name: string) (s: string) =
    s.Split([| ' '; '['; ']'; '+'; '-' |], System.StringSplitOptions.RemoveEmptyEntries)
    |> Array.filter ((=) name)
    |> Array.length

let d0l =
    testList
        "D0L"
        [ test "Anabaena catenula (ABOP §1.2, Fig 1.3)" {
              let src =
                  "settings { derivations = 4; }\naxiom: ar;\nar -> al br;\nal -> bl ar;\nbr -> ar;\nbl -> al;"

              Expect.equal
                  (steps src)
                  [ "ar"; "al br"; "bl ar ar"; "al al br al br"; "bl ar bl ar ar bl ar ar" ]
                  ""
          }

          test "Koch island, one step" {
              Expect.equal
                  (derived "settings { derivations = 1; }\naxiom: F-F-F-F;\nF -> F-F+F+F F-F-F+F;")
                  "F-F+F+F F-F-F+F-F-F+F+F F-F-F+F-F-F+F+F F-F-F+F-F-F+F+F F-F-F+F"
                  ""
          }

          test "modules without a rule are copied" {
              Expect.equal (derived "settings { derivations = 2; }\naxiom: A[B]C;\nA -> A A;") "A A A A[B]C" ""
          }

          test "identity is name plus parameter count" {
              Expect.equal (derived "settings { derivations = 1; }\naxiom: A A(1) A(1,2);\nA(t) -> B;") "A B A(1,2)" ""
          }

          test "Fig 1.24a growth" {
              let s = derived "settings { derivations = 5; }\naxiom: F;\nF -> F[+F]F[-F]F;"
              Expect.equal (count "F" s) 3125 "5^5 segments"
          } ]

let parametric =
    testList
        "parametric"
        [ test "L-system 1.7 (ABOP Fig 1.32)" {
              let src =
                  "settings { derivations = 4; }\naxiom: B(2)A(4,4);\nA(x,y) :\n when y <= 3 -> A(x*2, x+y)\n else -> B(x) A(x/y, 0);\nB(x) :\n when x < 1 -> C\n else -> B(x-1);"

              Expect.equal
                  (steps src)
                  [ "B(2)A(4,4)"
                    "B(1)B(4)A(1,0)"
                    "B(0)B(3)A(2,1)"
                    "C B(2)A(4,3)"
                    "C B(1)A(8,7)" ]
                  ""
          }

          test "literal patterns" {
              let src =
                  "settings { derivations = 3; }\naxiom: F(1,0);\nF(x, 0) -> F(x,2) G;\nF(x, t) -> F(x, t-1);"

              Expect.equal (steps src) [ "F(1,0)"; "F(1,2)G"; "F(1,1)G"; "F(1,0)G" ] ""
          }

          test "negative literal pattern" {
              Expect.equal (derived "settings { derivations = 1; }\naxiom: A(-1) A(1);\nA(-1) -> B;") "B A(1)" ""
          }

          test "guards: first true branch, then else" {
              let src = "settings { derivations = 1; }\naxiom: A(1) A(5) A(9);\nA(t) : when t < 3 -> L when t < 7 -> M else -> H;"
              Expect.equal (derived src) "L M H" ""
          }

          test "fall through to the next rule when no guard fires" {
              let src = "settings { derivations = 1; }\naxiom: A(1) A(9);\nA(t) : when t > 5 -> B;\nA(t) -> C;"
              Expect.equal (derived src) "C B" ""
          }

          test "defines are folded into rules" {
              Expect.equal (derived "let k = 2;\nsettings { derivations = 1; }\naxiom: A(3);\nA(x) -> A(x*k);") "A(6)" ""
          }

          test "built-in functions in degrees" {
              Expect.equal (derived "settings { derivations = 1; }\naxiom: A;\nA -> B(sin(90), cos(180), atan2(1, 1), round(2.5), max(1, 2), round(-2.5));") "B(1,-1,45,3,2,-3)" ""
          }

          test "boolean operators" {
              let src = "settings { derivations = 1; }\naxiom: A(1, 0);\nA(x, y) -> B(x && y, x || y, !y, x == 1, x != 1);"
              Expect.equal (derived src) "B(0,1,1,1,0)" ""
          }

          test "&& short-circuits" {
              Expect.equal (derived "settings { derivations = 1; }\naxiom: A(0);\nA(x) : when x != 0 && 1/x > 2 -> B else -> C;") "C" ""
          } ]

let context =
    testList
        "context-sensitive"
        [ let book = "settings { derivations = 1; }\naxiom: A B C [D E] [S G [H I [J K] L] M N O];\n"

          test "ABOP §1.8 example: B C < S > G [ H ] M" {
              Expect.equal (derived (book + "B C < S > G [ H ] M -> X;")) "A B C[D E][X G[H I[J K]L]M N O]" ""
          }

          test "left context skips sibling branches, not ancestors" {
              Expect.stringContains (derived (book + "A < S -> X;")) "[S G" "A is not the parent of S"
              Expect.stringContains (derived (book + "C < S -> X;")) "[X G" ""
          }

          test "right context skips unmentioned branches" {
              Expect.stringContains (derived (book + "S > G M -> X;")) "[X G" ""
              Expect.stringContains (derived (book + "S > G N -> X;")) "[S G" ""
          }

          test "right context fails at the end of a branch" {
              Expect.equal (derived "settings { derivations = 1; }\naxiom: A[B]C;\nB > C -> X;") "A[B]C" ""
          }

          test "Fig 1.30a: acropetal signal" {
              let src =
                  "settings { derivations = 3; }\nignore: + -;\naxiom: Fb[+Fa]Fa[-Fa]Fa[+Fa]Fa;\nFb < Fa -> Fb;"

              Expect.equal
                  (steps src)
                  [ "Fb[+Fa]Fa[-Fa]Fa[+Fa]Fa"
                    "Fb[+Fb]Fb[-Fa]Fa[+Fa]Fa"
                    "Fb[+Fb]Fb[-Fb]Fb[+Fa]Fa"
                    "Fb[+Fb]Fb[-Fb]Fb[+Fb]Fb" ]
                  ""
          }

          test "Fig 1.30b: basipetal signal" {
              let src =
                  "settings { derivations = 1; }\nignore: + -;\naxiom: Fa[+Fa]Fa[-Fa]Fa[+Fa]Fb;\nFa > Fb -> Fb;"

              Expect.equal (derived src) "Fa[+Fa]Fa[-Fa]Fb[+Fa]Fb" ""
          }

          test "context parameters bind" {
              Expect.equal (derived "settings { derivations = 1; }\naxiom: A(1) B(2) C(3);\nA(x) < B(y) > C(z) -> B(x+y+z);") "A(1)B(6)C(3)" ""
          }

          test "wildcard matches any module" {
              Expect.equal (derived "settings { derivations = 1; }\naxiom: A + B;\n* < + > * -> -;") "A-B" ""
              Expect.equal (derived "settings { derivations = 1; }\naxiom: + B;\n* < + > * -> -;") "+B" "no left neighbor"
          }

          test "wildcard skips ignored modules" {
              Expect.equal (derived "settings { derivations = 1; }\nignore: F;\naxiom: A F + F B;\n* < + > * -> -;") "A F-F B" ""
              Expect.equal (derived "settings { derivations = 1; }\nignore: F;\naxiom: F + F;\n* < + > * -> -;") "F+F" "only ignored neighbors"
          }

          test "right context: branch order matters (no backtracking)" {
              let src = "settings { derivations = 1; }\naxiom: S [X] [G];\nS > [G] -> Y;"
              Expect.equal (derived src) "S[X][G]" "the formal [ matches the first branch only"
          }

          test "context rules are tried first" {
              Expect.equal (derived "settings { derivations = 1; }\naxiom: C A A;\nA -> B;\nC < A -> D;") "C D B" ""
          }

          test "Fig 1.31a derives" {
              let src = System.IO.File.ReadAllText(System.IO.Path.Combine(__SOURCE_DIRECTORY__, "../../examples/fig1-31a.rp"))
              let s = derived src
              Expect.isGreaterThan s.Length 1000 "a sizable plant"
          } ]

let stochastic =
    let plant seed =
        derivedWithSeed
            seed
            "settings { derivations = 5; }\naxiom: F;\nF : weight 1 -> F[+F]F[-F]F weight 1 -> F[+F]F weight 1 -> F[-F]F;"

    testList
        "stochastic"
        [ test "same seed, same result" { Expect.equal (plant 7L) (plant 7L) "" }
          test "different seeds differ" { Expect.notEqual (plant 1L) (plant 2L) "" }

          test "weights are proportional" {
              let src =
                  "settings { derivations = 14; }\naxiom: X(12);\nX(n) : when n > 0 -> X(n-1) X(n-1) else -> Y;\nY : weight 1 -> B weight 3 -> C;"

              let s = derived src
              let b, c = count "B" s, count "C" s
              Expect.equal (b + c) 4096 ""
              Expect.isTrue (abs (c - 3072) < 120) $"about 3/4 should be C, got %d{c}"
          }

          test "guarded weights: else when nothing is eligible" {
              let src = "settings { derivations = 1; }\naxiom: A(0) A(5);\nA(t) : when t > 3 weight 2 -> B when t > 3 weight 1 -> B else -> E;"
              Expect.equal (derived src) "E B" ""
          }

          test "zero weight never fires" {
              Expect.equal (derived "settings { derivations = 1; }\naxiom: A A A A;\nA : weight 0 -> B weight 1 -> C;") "C C C C" ""
          }

          test "negative weight" {
              let d = runtimeError "settings { derivations = 1; }\naxiom: A;\nA : weight -1 -> B weight 1 -> C;"
              Expect.equal d.Code Codes.InvalidWeight ""
          }

          test "subnormal weights: a zero weight never fires" {
              for seed in 0L..20L do
                  Expect.equal (derivedWithSeed seed "settings { derivations = 1; }\naxiom: A;\nA : weight 5e-324 -> B weight 0 -> C;") "B" ""
          }

          test "huge weights don't overflow" {
              let s = derived "settings { derivations = 1; }\naxiom: A A A A A A A A;\nA : weight 1e308 -> B weight 1e308 -> C;"
              Expect.isTrue (s.Contains "B" && s.Contains "C") s
          }

          test "infinite weight" {
              let d = runtimeError "settings { derivations = 1; }\naxiom: A;\nA : weight exp(1000) -> B;"
              Expect.equal d.Code Codes.InvalidWeight ""
          }

          test "no positive weight" {
              let d = runtimeError "settings { derivations = 1; }\naxiom: A;\nA : weight 0 -> B;"
              Expect.equal d.Code Codes.NoPositiveWeight ""
          } ]

let runtime =
    testList
        "runtime errors"
        [ test "division by zero reports the rule and module" {
              let d = runtimeError "settings { derivations = 1; }\naxiom: A(0);\n\nA(x) -> B(1/x);"
              Expect.equal d.Code Codes.DivisionByZero ""
              Expect.equal (d.Pos |> Option.map _.Line) (Some 4) ""
              Expect.stringContains d.Message "`A(0)`" ""
          }

          test "NaN" {
              let d = runtimeError "settings { derivations = 1; }\naxiom: A(-1);\nA(x) -> B(sqrt(x));"
              Expect.equal d.Code Codes.NotANumber ""
          }

          test "modulo by zero" {
              let d = runtimeError "settings { derivations = 1; }\naxiom: A(3);\nA(x) -> B(x % 0);"
              Expect.equal d.Code Codes.DivisionByZero ""
          } ]

let interpretation =
    testList
        "interpretation (§4.6)"
        [ test "dragon curve (§4.6)" {
              let src = "settings { derivations = 2; }\naxiom: Fl;\nFl -> Fl + Fr +;\nFr -> - Fl - Fr;\nFl as F;\nFr as F;"
              Expect.equal (derived src) "Fl+Fr++-Fl-Fr+" "derived string"
              Expect.equal (drawing src) "F+F++-F-F+" "drawing string"
          }

          test "applied once, never iterated" {
              Expect.equal (drawing "axiom: A B;\nA as B;\nB as F;") "B F" ""
          }

          test "guards and contexts" {
              let src = "axiom: leaf(1) leaf(5) X Y;\nleaf(a) : when a < 3 as F(0.5) else as G;\nX < Y as Z;"
              Expect.equal (drawing src) "F(0.5)G X Z" ""
          }

          test "never feeds back into derivation" {
              let src = "settings { derivations = 2; }\naxiom: A;\nA -> A B;\nB as F;"
              Expect.equal (derived src) "A B B" ""
              Expect.equal (drawing src) "A F F" ""
          } ]

let format =
    testList
        "LString.format"
        [ test "spaces only between names" {
              Expect.equal (derived "axiom: F F + f [ - G(1, 2.5) ] H;") "F F+f[-G(1,2.5)]H" ""
          } ]

let cut =
    testList
        "cut % (v0.2)"
        [ test "the spec's example: one step drops the rest of the branch" {
              let src =
                  "settings { derivations = 1; }\naxiom: A [ B % C [ D ] E ] F;\nA -> a; B -> b; C -> c; D -> d; E -> e; F -> f;"

              Expect.equal (steps src) [ "A[B%C[D]E]F"; "a[b]f" ] ""
          }

          test "a produced % survives one step, then cuts" {
              let src = "settings { derivations = 3; }\naxiom: A;\nA -> B [ C % D ] E;\nC -> X;\nD -> Y;"
              Expect.equal (steps src) [ "A"; "B[C%D]E"; "B[X]E"; "B[X]E" ] ""
          }

          test "nested branches" {
              Expect.equal (derived "settings { derivations = 1; }\naxiom: A [ B [ C % D ] E ] F;") "A[B[C]E]F" ""
          }

          test "complete branches inside the cut part go with it" {
              Expect.equal (derived "settings { derivations = 1; }\naxiom: A [ B % C [ D % ] [ E ] G ] F;") "A[B]F" ""
          }

          test "at the top level, to the end of the string" {
              Expect.equal (derived "settings { derivations = 1; }\naxiom: A % B [ C ] D;") "A" ""
          }

          test "the cut part isn't rewritten" {
              // B(1/0) would be a division by zero if it were rewritten.
              Expect.equal (derived "settings { derivations = 1; }\naxiom: % A(0);\nA(x) -> B(1/x);") "empty" ""
          }

          test "in the axiom: nothing is cut with derivations = 0" {
              Expect.equal (steps "axiom: A [ B % C ] D;") [ "A[B%C]D" ] ""
          }

          test "in the axiom: cut in the first step" {
              Expect.equal (steps "settings { derivations = 1; }\naxiom: A [ B % C ] D;") [ "A[B%C]D"; "A[B]D" ] ""
          }

          test "the cut part draws until the next step: interpretation doesn't cut" {
              let src = "axiom: A [ % B ] % C;\nB as F;\nC as F;"
              Expect.equal (drawing src) "A[%F]%F" ""
          }

          test "only * matches % in a context" {
              let star = "settings { derivations = 1; }\naxiom: A % B;\nA > * -> X;"
              Expect.equal (derived star) "X" "A's right neighbor is %"
              let named = "settings { derivations = 1; }\naxiom: A % B;\nA > B -> X;"
              Expect.equal (derived named) "A" "% hides B"
              let left = "axiom: A % B;\n* < B as X;"
              Expect.equal (drawing left) "A%X" "* to the left of B"
          }

          test "consider: skips %" {
              let src = "settings { derivations = 1; }\nconsider: B;\naxiom: A % B;\nA > B -> X;"
              Expect.equal (derived src) "X" ""
          }

          test "ignore: doesn't skip %" {
              // `*` must consume `%`; then `B` matches after the ignored `C`.
              let star = "settings { derivations = 1; }\nignore: C;\naxiom: A % C B;\nA > * B -> X;"
              Expect.equal (derived star) "X" "* matched %, then B"
              let named = "settings { derivations = 1; }\nignore: C;\naxiom: A % C B;\nA > B -> X;"
              Expect.equal (derived named) "A" "% hides B"
          } ]

let consider =
    testList
        "consider: (v0.2)"
        [ test "only listed modules exist during context matching" {
              let src = "settings { derivations = 1; }\nconsider: S;\naxiom: S A B A;\nS < A -> X;"
              Expect.equal (derived src) "S X B X" "A and B are skipped"
              let ignoreNothing = "settings { derivations = 1; }\naxiom: S A B A;\nS < A -> X;"
              Expect.equal (derived ignoreNothing) "S X B A" "without consider:"
          }

          test "right context" {
              let src = "settings { derivations = 1; }\nconsider: T;\naxiom: A F F [ G ] T;\nA > T -> X;"
              Expect.equal (derived src) "X F F[G]T" "skips modules and branches"
          }

          test "brackets always exist" {
              let src = "settings { derivations = 1; }\nconsider: S;\naxiom: A [ S ];\nA > S -> X;"
              Expect.equal (derived src) "A[S]" "S is in a branch, not on A's axis"
              let branch = "settings { derivations = 1; }\nconsider: S;\naxiom: A B [ C S ];\nA > [ S ] -> X;"
              Expect.equal (derived branch) "X B[C S]" ""
          }

          test "* matches only listed modules" {
              Expect.equal (derived "settings { derivations = 1; }\nconsider: S;\naxiom: B A;\n* < A -> X;") "B A" ""
              Expect.equal (derived "settings { derivations = 1; }\nconsider: S;\naxiom: S B A;\n* < A -> X;") "S B X" ""
          }

          test "the strict predecessor isn't affected" {
              Expect.equal (derived "settings { derivations = 1; }\nconsider: S;\naxiom: A;\nA -> X;") "X" ""
          }

          test "interpretation rules use it too" {
              Expect.equal (drawing "consider: S;\naxiom: S B A;\nS < A as F;") "S B F" ""
          } ]

let emptyString =
    testList
        "empty (v0.2)"
        [ test "erasing a module" {
              Expect.equal (derived "settings { derivations = 1; }\naxiom: A B [ A ];\nA -> empty;") "B[]" ""
          }

          test "an empty string formats as empty" {
              Expect.equal (steps "settings { derivations = 2; }\naxiom: A;\nA -> empty;") [ "A"; "empty"; "empty" ] ""
              Expect.equal (steps "axiom: empty;") [ "empty" ] ""
          }

          test "interpretation to empty" { Expect.equal (drawing "axiom: A B;\nA as empty;") "B" "" } ]

/// Developmental sequences that ABOP prints in full (backends.md §9).
let book =
    testList
        "book derivation sequences (v0.2)"
        [ test "signal model, ABOP §3.1.2 (d = 4, m = 2, u = 1)" {
              // The L-system of book p. 67, as printed (ε is written `empty`).
              //
              // Book erratum: strings 9 and 10 printed on p. 68 don't follow from these
              // productions. At step 9, p6 erases S(1) and p8 rewrites a(2) (left
              // context S(1)) into I[L]A, so the string gains a fourth internode. The
              // book prints `I[L]I[L]I[L]A` and `I[L]I[L]I[L]K`, which would need
              // p8: S(i) < a(j) -> A. We keep the book's productions: strings 1-8 are
              // the book's, strings 9-10 are what its productions derive.
              let src =
                  """
settings { derivations = 9; }
let d = 4;
let m = 2;
let u = 1;
axiom: D(1) a(1);
a(i) :
    when i < m  -> a(i+1)
    when i == m -> I[L]a(1);
D(i) :
    when i < d  -> D(i+1)
    when i == d -> S(1);
S(i) :
    when i < u  -> S(i+1)
    when i == u -> empty;
S(i) < I : when i == u -> I S(1);
S(i) < a(j) -> I[L]A;
A -> K;
"""

              Expect.equal
                  (steps src)
                  [ "D(1)a(1)"
                    "D(2)a(2)"
                    "D(3)I[L]a(1)"
                    "D(4)I[L]a(2)"
                    "S(1)I[L]I[L]a(1)"
                    "I S(1)[L]I[L]a(2)"
                    "I[L]I S(1)[L]I[L]a(1)"
                    "I[L]I[L]I S(1)[L]a(2)"
                    "I[L]I[L]I[L]I[L]A" // book: I[L]I[L]I[L]A (erratum)
                    "I[L]I[L]I[L]I[L]K" ] // book: I[L]I[L]I[L]K (erratum)
                  ""
          }

          test "lily-of-the-valley, ABOP §3.3.1 (K_i as K(i))" {
              let src = "settings { derivations = 4; }\naxiom: A;\nA -> I[I K(0)]A;\nK(i) -> K(i+1);"

              Expect.equal
                  (steps src)
                  [ "A"
                    "I[I K(0)]A"
                    "I[I K(1)]I[I K(0)]A"
                    "I[I K(2)]I[I K(1)]I[I K(0)]A"
                    "I[I K(3)]I[I K(2)]I[I K(1)]I[I K(0)]A" ]
                  ""
          } ]

[<Tests>]
let tests =
    testList "Derivation" [ d0l; parametric; context; stochastic; runtime; interpretation; format; cut; consider; emptyString; book ]
