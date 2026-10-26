module RubberPlant.Tests.CheckerTests

open Expecto
open RubberPlant
open RubberPlant.Model
open RubberPlant.Tests.TestUtil

let compileOk (src: string) : Model * Diagnostic list =
    match Pipeline.compile src with
    | Ok r -> r
    | Error ds -> failtestf "expected a successful compile, got:\n%s" (formatDiags ds)

let compileErrors (src: string) : Diagnostic list =
    match Pipeline.compile src with
    | Ok(m, _) -> failtestf "expected errors, got a model:\n%A" m
    | Error ds -> ds |> List.filter Diagnostic.isError

/// Asserts that compiling `src` reports exactly these error codes.
let expectCodes (codes: string list) (src: string) =
    let ds = compileErrors src
    Expect.equal (ds |> List.map _.Code) codes (formatDiags ds)

/// Asserts a single error with this code at this line.
let expectError (code: string) (line: int) (src: string) =
    match compileErrors src with
    | [ d ] when d.Code = code && (d.Pos |> Option.map _.Line) = Some line -> ()
    | ds -> failtestf "expected %s at line %d, got:\n%s" code line (formatDiags ds)

/// Asserts a single error with this code whose message mentions every one of `parts`.
let expectMessage (code: string) (parts: string list) (src: string) =
    match compileErrors src with
    | [ d ] when d.Code = code ->
        for p in parts do
            Expect.stringContains d.Message p (formatDiags [ d ])
    | ds -> failtestf "expected one %s, got:\n%s" code (formatDiags ds)

let ax = "axiom: A;\n"

[<Tests>]
let tests =
    testList
        "Checker"
        [ test "all spec examples check" {
              for name, src in ParserTests.specExamples do
                  let src = if src.Contains "axiom:" then src else ax + src

                  match Pipeline.compile src with
                  | Ok _ -> ()
                  | Error ds -> failtestf "%s:\n%s" name (formatDiags ds)
          }

          test "settings and defaults" {
              let m, _ = compileOk "settings { derivations = 4; angle = 360/8; }\naxiom: F;"
              Expect.equal m.Settings.Derivations 4 ""
              Expect.equal m.Settings.Angle 45.0 ""
              Expect.equal m.Settings.Step 1.0 "default step"
              Expect.equal m.Settings.Width 1.0 "default width"
              Expect.equal m.Settings.Taper 30.0 "default taper"
              Expect.equal m.Settings.Seed 0L "default seed (a run parameter)"
          }

          test "lets are evaluated in source order" {
              let m, _ =
                  compileOk "let c = 1;\nlet p = 0.3;\nlet q = c - p;\nlet h = (p*q)^0.5;\naxiom: F(h, q);"

              match m.Axiom with
              | [| LString.Mod("F", [| h; q |]) |] ->
                  Expect.floatClose Accuracy.veryHigh h (sqrt 0.21) ""
                  Expect.floatClose Accuracy.veryHigh q 0.7 ""
              | a -> failtestf "unexpected axiom %A" a
          }

          test "settings can use earlier lets and built-in functions" {
              let m, _ = compileOk "let n = 3;\nsettings { derivations = n + 1; angle = 360 / round(7.2); }\naxiom: F;"
              Expect.equal m.Settings.Derivations 4 ""
              Expect.floatClose Accuracy.veryHigh m.Settings.Angle (360.0 / 7.0) ""
          }

          test "settings can't use parameters" {
              expectError Codes.UnknownName 1 "settings { angle = x; }\naxiom: A(1);\nA(x) -> B;"
          }

          test "settings can't use later lets" {
              expectError Codes.LetUsedBeforeDefinition 1 "settings { angle = a; }\nlet a = 1;\naxiom: F;"
          }

          test "interleaved prelude" {
              let m, _ =
                  compileOk
                      "let a = 1; settings { angle = a * 10; } let b = 2; ignore: F; let c = a + b; axiom: F(c); F(x) -> F(x);"

              Expect.equal m.Settings.Angle 10.0 ""
              Expect.equal m.Context (IgnoreList(set [ "F" ])) ""
              Expect.equal m.Axiom [| LString.Mod("F", [| 3.0 |]) |] ""
          }

          test "interleaved prelude: no forward references" {
              expectError
                  Codes.LetUsedBeforeDefinition
                  1
                  "let a = 1; settings { angle = b; } let b = 2; axiom: F; F -> F;"
          }

          test "seed is an ordinary name" { compileOk "axiom: seed(1);\nseed(x) -> F(x);" |> ignore }
          test "v0.2: empty axiom" {
              let m, _ = compileOk "axiom: empty;"
              Expect.isEmpty m.Axiom ""
          }

          test "v0.2: cut in the axiom" {
              let m, _ = compileOk "axiom: A [ % ] %;"
              Expect.equal m.Axiom [| LString.Mod("A", [||]); LString.Push; LString.Cut; LString.Pop; LString.Cut |] ""
          }

          test "v0.2: tropism and elasticity defaults" {
              let m, _ = compileOk "axiom: F;"
              Expect.equal m.Settings.Tropism (Geometry.Vec3.create 0.0 -1.0 0.0) "gravity"
              Expect.equal m.Settings.Elasticity 0.0 "tropism off"
          }

          test "v0.2: tropism and elasticity values" {
              let m, _ =
                  compileOk "let e = 0.22;\nsettings { tropism = (-0.02, -1 + 0, cos(90)); elasticity = -e; }\naxiom: F;"

              Expect.equal m.Settings.Tropism.X -0.02 ""
              Expect.equal m.Settings.Tropism.Y -1.0 ""
              Expect.floatClose Accuracy.veryHigh m.Settings.Tropism.Z 0.0 ""
              Expect.equal m.Settings.Elasticity -0.22 "negative: bends away from T"
          }

          test "v0.2: tropism is stored as written, not normalized" {
              let m, _ = compileOk "settings { tropism = (0, 0, 3); }\naxiom: F;"
              Expect.equal m.Settings.Tropism (Geometry.Vec3.create 0.0 0.0 3.0) ""
          }

          test "v0.2: tropism can't be the zero vector" {
              expectError Codes.InvalidSetting 1 "settings { tropism = (0, 0, 0); }\naxiom: F;"
              expectError Codes.InvalidSetting 1 "settings { tropism = (0, -0, 0); }\naxiom: F;"
          }

          test "v0.2: tropism components are finite" {
              expectError Codes.InvalidSetting 1 "settings { tropism = (0, exp(1000), 0); }\naxiom: F;"
          }

          test "v0.2: tropism components are constant expressions" {
              expectCodes [ Codes.DivisionByZero; Codes.UnknownName ] "settings { tropism = (x, 1/0, 0); }\naxiom: F;"
              expectError Codes.LetUsedBeforeDefinition 1 "settings { tropism = (0, k, 0); }\nlet k = 1;\naxiom: F;"
          }

          test "v0.2: elasticity is finite" {
              expectError Codes.InvalidSetting 1 "settings { elasticity = -exp(1000); }\naxiom: F;"
          }

          test "v0.2: tropism set twice" {
              expectError Codes.DuplicateSetting 1 "settings { tropism = (0, -1, 0); tropism = (1, 0, 0); }\naxiom: F;"
          }

          test "v0.2: consider:" {
              let m, _ = compileOk "consider: M S;\naxiom: A;"
              Expect.equal m.Context (ConsiderList(set [ "M"; "S" ])) ""
          }

          test "v0.2: no list means every module is considered" {
              let m, _ = compileOk "axiom: A;"
              Expect.equal m.Context (IgnoreList Set.empty) ""
          }

          test "v0.2: not both ignore: and consider:" {
              expectError Codes.IgnoreAndConsider 3 "axiom: A;\nignore: F;\nconsider: G;"
              expectError Codes.IgnoreAndConsider 3 "axiom: A;\nconsider: G;\nignore: F;"
          }

          test "v0.2: duplicate consider:" {
              expectError Codes.DuplicateConsider 3 "axiom: A;\nconsider: F;\nconsider: G;"
          }

          test "v0.2: consider: after rules" {
              expectError Codes.DeclarationAfterRules 3 "axiom: A;\nA -> B;\nconsider: F;"
          }

          test "v0.2: consider: names are modules" {
              expectError Codes.NamespaceClash 1 "let M = 1;\nconsider: M;\naxiom: A;"
              expectError Codes.NamespaceClash 2 "axiom: A;\nconsider: sin;"
          }


          test "missing axiom" { expectCodes [ Codes.MissingAxiom ] "A -> B;" }
          test "duplicate axiom" { expectError Codes.DuplicateAxiom 2 "axiom: A;\naxiom: B;" }

          test "duplicate settings block" {
              expectError Codes.DuplicateSettings 3 "axiom: A;\nsettings { angle = 3; }\nsettings { step = 2; }"
          }

          test "duplicate ignore" { expectError Codes.DuplicateIgnore 3 "axiom: A;\nignore: F;\nignore: G;" }

          test "setting set twice" {
              expectError Codes.DuplicateSetting 4 "axiom: A;\nsettings {\n  angle = 3;\n  angle = 4;\n}"
          }

          test "declaration after rules" {
              for stmt in [ "let k = 1;"; "settings { angle = 3; }"; "ignore: F;" ] do
                  expectError Codes.DeclarationAfterRules 3 $"axiom: A;\nA -> B;\n%s{stmt}"

              expectError Codes.DeclarationAfterRules 2 "A -> B;\naxiom: A;"
              expectError Codes.DeclarationAfterRules 1 "A -> B; axiom: A;"
          }

          test "duplicate let" { expectError Codes.DuplicateLet 3 "axiom: A;\nlet a = 1;\nlet a = 2;" }

          test "let used before its definition" {
              expectError Codes.LetUsedBeforeDefinition 2 "axiom: A;\nlet a = b;\nlet b = 1;"
          }

          test "axiom sees only earlier lets" {
              expectError Codes.LetUsedBeforeDefinition 1 "axiom: F(k);\nlet k = 2;"

              let m, _ = compileOk "let k = 2;\naxiom: F(k);"
              Expect.equal m.Axiom [| LString.Mod("F", [| 2.0 |]) |] "earlier let is visible"
          }

          test "let using itself" { expectError Codes.LetUsedBeforeDefinition 2 "axiom: A;\nlet a = a + 1;" }

          test "rule using a later let" {
              expectCodes
                  [ Codes.LetUsedBeforeDefinition; Codes.DeclarationAfterRules ]
                  "axiom: A;\nA -> B(k);\nlet k = 1;"
          }

          test "unknown name" { expectError Codes.UnknownName 2 "axiom: A;\nA(x) -> B(y);" }
          test "module used as a value" { expectError Codes.NotAValue 2 "axiom: A;\nA(x) -> B(A);" }
          test "function used as a value" { expectError Codes.NotAValue 2 "axiom: A;\nA(x) -> B(sin);" }
          test "unknown function" { expectError Codes.UnknownFunction 2 "axiom: A;\nA(x) -> B(foo(x));" }
          test "parameter called as a function" { expectError Codes.UnknownFunction 2 "axiom: A;\nA(x) -> B(x(1));" }
          test "wrong arity in a constant expression doesn't crash" {
              expectError Codes.WrongArity 2 "axiom: A;\nsettings { angle = sin(); }"
              expectError Codes.WrongArity 1 "axiom: F(atan2(1));"
          }

          test "wrong arity" { expectError Codes.WrongArity 2 "axiom: A;\nA(x) -> B(atan2(x));" }
          test "duplicate parameter" { expectError Codes.DuplicateParameter 2 "axiom: A;\nA(x, x) -> B;" }
          test "duplicate parameter across contexts" { expectError Codes.DuplicateParameter 2 "axiom: A;\nA(x) < B(x) -> C;" }
          test "repeated wildcard is fine" { compileOk "axiom: A;\nA(_, _) -> B;" |> ignore }

          test "same parameter name in different rules is fine" {
              compileOk "axiom: A(1) B(2);\nA(x) -> A(x + 1);\nB(x) -> B(x * 2);" |> ignore
          }

          test "namespace: module and let" {
              expectMessage Codes.NamespaceClash [ "module (line 1)"; "`let` (line 2)" ] "axiom: L;\nlet L = 3;"
          }

          test "namespace: module and let, let first (modules aren't declared)" {
              expectMessage Codes.NamespaceClash [ "module (line 3)"; "`let` (line 1)" ] "let L = 3;\naxiom: A;\nA -> L;"
          }

          test "namespace: module and parameter" {
              expectMessage Codes.NamespaceClash [ "module (line 2)"; "parameter (line 2)" ] "axiom: A;\nA(B) -> B;"
              expectMessage Codes.NamespaceClash [ "module (line 1)"; "parameter (line 2)" ] "axiom: B;\nA(B) -> A;"
          }

          test "namespace: let and parameter" {
              expectMessage Codes.NamespaceClash [ "`let` (line 2)"; "parameter (line 3)" ] "axiom: A;\nlet k = 1;\nA(k) -> A;"
          }

          test "namespace: turtle F as parameter" { expectError Codes.NamespaceClash 2 "axiom: A;\nA(F) -> A;" }

          test "namespace: turtle F as let" {
              expectMessage Codes.NamespaceClash [ "predefined by the turtle"; "`let` (line 2)" ] "axiom: A;\nlet F = 1;"
          }

          test "namespace: built-in as let" { expectError Codes.NamespaceClash 2 "axiom: A;\nlet sin = 1;" }
          test "namespace: built-in as module" { expectError Codes.NamespaceClash 1 "axiom: max;" }
          test "namespace: built-in as parameter" { expectError Codes.NamespaceClash 2 "axiom: A;\nA(cos) -> A;" }
          test "case-sensitive names" { compileOk "axiom: H(1);\nH(h) -> H(h + 1);" |> ignore }

          test "derivations must be a non-negative integer" {
              expectError Codes.InvalidSetting 2 "axiom: A;\nsettings { derivations = 2.5; }"
              expectError Codes.InvalidSetting 2 "axiom: A;\nsettings { derivations = -1; }"
          }

          test "taper must be in [0, 100]" { expectError Codes.InvalidSetting 2 "axiom: A;\nsettings { taper = 120; }" }

          test "width must be finite and non-negative" {
              expectError Codes.InvalidSetting 2 "axiom: A;\nsettings { width = -1; }"
              compileOk "axiom: A;\nsettings { width = 0; }" |> ignore
          }

          test "invalid setting points at its entry" {
              expectError Codes.InvalidSetting 3 "axiom: A;\nsettings {\n  taper = 120;\n}"
          }

          test "axiom evaluation error points at the failing module" {
              match compileErrors "axiom:\n  F\n  G(1/0);" with
              | [ d ] when d.Code = Codes.DivisionByZero ->
                  Expect.equal (d.Pos |> Option.map (fun p -> p.Line, p.Column)) (Some(3, 3)) (formatDiags [ d ])
              | ds -> failtestf "expected one division by zero, got:\n%s" (formatDiags ds)
          }

          test "division by zero in a let" { expectError Codes.DivisionByZero 2 "axiom: A;\nlet a = 1/0;" }
          test "NaN in a setting" { expectError Codes.NotANumber 2 "axiom: A;\nsettings { angle = sqrt(-1); }" }

          test "reports several errors" {
              expectCodes [ Codes.UnknownName; Codes.UnknownName ] "axiom: A;\nA -> B(x);\nB -> C(y);"
          }

          test "unreachable rule warning" {
              let _, warnings = compileOk "axiom: A;\nA -> B;\nA -> C;"

              match warnings with
              | [ w ] ->
                  Expect.equal w.Code Codes.UnreachableRule ""
                  Expect.equal (w.Pos |> Option.map _.Line) (Some 3) ""
              | ws -> failtestf "unexpected warnings:\n%s" (formatDiags ws)
          }

          test "no unreachable warning after a literal pattern, guard or context" {
              let _, warnings =
                  compileOk "axiom: A(0);\nA(0) -> B;\nA(t) : when t > 1 -> C;\nB < A(t) -> D;\nA(t) -> A(t+1);"

              Expect.isEmpty warnings ""
          }

          test "rule order: context rules first" {
              let m, _ = compileOk "axiom: A;\nA -> B;\nC < A -> D;"
              Expect.equal (m.Productions |> List.map _.HasContext) [ false; true ] "source order kept in the model"
          } ]
