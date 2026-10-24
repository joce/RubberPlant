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

let ax = "#axiom A\n"

[<Tests>]
let tests =
    testList
        "Checker"
        [ test "all spec examples check" {
              for name, src in ParserTests.specExamples do
                  let src =
                      if src.Contains "#axiom" then src else ax + src

                  match Pipeline.compile src with
                  | Ok _ -> ()
                  | Error ds -> failtestf "%s:\n%s" name (formatDiags ds)
          }

          test "settings and defaults" {
              let m, _ = compileOk "#derivations 4\n#angle 360/8\n#axiom F"
              Expect.equal m.Settings.Derivations 4 ""
              Expect.equal m.Settings.Angle 45.0 ""
              Expect.equal m.Settings.Step 1.0 "default step"
              Expect.equal m.Settings.Width 1.0 "default width"
              Expect.equal m.Settings.Taper 30.0 "default taper"
              Expect.equal m.Settings.Seed 0L "default seed"
          }

          test "defines are evaluated in source order" {
              let m, _ =
                  compileOk "#define c 1\n#define p 0.3\n#define q c - p\n#define h (p*q)^0.5\n#axiom F(h, q)"

              match m.Axiom with
              | [| LString.Mod("F", [| h; q |]) |] ->
                  Expect.floatClose Accuracy.veryHigh h (sqrt 0.21) ""
                  Expect.floatClose Accuracy.veryHigh q 0.7 ""
              | a -> failtestf "unexpected axiom %A" a
          }

          test "defines can be used in settings" {
              let m, _ = compileOk "#define n 3\n#derivations n + 1\n#axiom F"
              Expect.equal m.Settings.Derivations 4 ""
          }

          test "missing axiom" { expectCodes [ Codes.MissingAxiom ] "A -> B;" }
          test "duplicate axiom" { expectError Codes.DuplicateAxiom 2 "#axiom A\n#axiom B" }
          test "duplicate setting" { expectError Codes.DuplicateSetting 3 "#axiom A\n#angle 3\n#angle 4" }
          test "duplicate define" { expectError Codes.DuplicateDefine 3 "#axiom A\n#define a 1\n#define a 2" }

          test "define used before its definition" {
              expectError Codes.DefineUsedBeforeDefinition 2 "#axiom A\n#define a b\n#define b 1"
          }

          test "rule using a later define" {
              expectError Codes.DefineUsedBeforeDefinition 2 "#axiom A\nA -> B(k);\n#define k 1"
          }

          test "unknown name" { expectError Codes.UnknownName 2 "#axiom A\nA(x) -> B(y);" }
          test "module used as a value" { expectError Codes.NotAValue 2 "#axiom A\nA(x) -> B(A);" }
          test "function used as a value" { expectError Codes.NotAValue 2 "#axiom A\nA(x) -> B(sin);" }
          test "unknown function" { expectError Codes.UnknownFunction 2 "#axiom A\nA(x) -> B(foo(x));" }
          test "parameter called as a function" { expectError Codes.UnknownFunction 2 "#axiom A\nA(x) -> B(x(1));" }
          test "wrong arity in a constant expression doesn't crash" {
              expectError Codes.WrongArity 2 "#axiom A\n#angle sin()"
              expectError Codes.WrongArity 1 "#axiom F(atan2(1))"
          }

          test "wrong arity" { expectError Codes.WrongArity 2 "#axiom A\nA(x) -> B(atan2(x));" }
          test "duplicate parameter" { expectError Codes.DuplicateParameter 2 "#axiom A\nA(x, x) -> B;" }
          test "duplicate parameter across contexts" { expectError Codes.DuplicateParameter 2 "#axiom A\nA(x) < B(x) -> C;" }
          test "repeated wildcard is fine" { compileOk "#axiom A\nA(_, _) -> B;" |> ignore }

          test "namespace: module and define" {
              let ds = compileErrors "#axiom L\n#define L 3"

              match ds with
              | [ d ] ->
                  Expect.equal d.Code Codes.NamespaceClash ""
                  Expect.stringContains d.Message "module (line 1)" ""
                  Expect.stringContains d.Message "define (line 2)" ""
              | _ -> failtestf "unexpected:\n%s" (formatDiags ds)
          }

          test "namespace: module and parameter" { expectError Codes.NamespaceClash 2 "#axiom A\nA(B) -> B;" }
          test "namespace: turtle F as parameter" { expectError Codes.NamespaceClash 2 "#axiom A\nA(F) -> A;" }
          test "namespace: define and parameter" { expectError Codes.NamespaceClash 3 "#axiom A\n#define k 1\nA(k) -> A;" }
          test "namespace: built-in as define" { expectError Codes.NamespaceClash 2 "#axiom A\n#define sin 1" }
          test "namespace: built-in as module" { expectError Codes.NamespaceClash 1 "#axiom max" }
          test "namespace: built-in as parameter" { expectError Codes.NamespaceClash 2 "#axiom A\nA(cos) -> A;" }
          test "case-sensitive names" { compileOk "#axiom H(1)\nH(h) -> H(h + 1);" |> ignore }

          test "derivations must be a non-negative integer" {
              expectError Codes.InvalidSetting 2 "#axiom A\n#derivations 2.5"
              expectError Codes.InvalidSetting 2 "#axiom A\n#derivations -1"
          }

          test "taper must be in [0, 100]" { expectError Codes.InvalidSetting 2 "#axiom A\n#taper 120" }
          test "seed must be an integer" { expectError Codes.InvalidSetting 2 "#axiom A\n#seed 0.5" }

          test "seed range: exactly representable integers" {
              let m, _ = compileOk "#axiom A\n#seed 9007199254740991"
              Expect.equal m.Settings.Seed 9007199254740991L ""
              expectError Codes.InvalidSetting 2 "#axiom A\n#seed 9007199254740993"
          }

          test "width must be finite and non-negative" {
              expectError Codes.InvalidSetting 2 "#axiom A\n#width -1"
              compileOk "#axiom A\n#width 0" |> ignore
          }
          test "division by zero in a define" { expectError Codes.DivisionByZero 2 "#axiom A\n#define a 1/0" }
          test "NaN in a setting" { expectError Codes.NotANumber 2 "#axiom A\n#angle sqrt(-1)" }

          test "reports several errors" {
              expectCodes [ Codes.UnknownName; Codes.UnknownName ] "#axiom A\nA -> B(x);\nB -> C(y);"
          }

          test "unreachable rule warning" {
              let _, warnings = compileOk "#axiom A\nA -> B;\nA -> C;"

              match warnings with
              | [ w ] ->
                  Expect.equal w.Code Codes.UnreachableRule ""
                  Expect.equal (w.Pos |> Option.map _.Line) (Some 3) ""
              | ws -> failtestf "unexpected warnings:\n%s" (formatDiags ws)
          }

          test "no unreachable warning after a literal pattern, guard or context" {
              let _, warnings =
                  compileOk "#axiom A(0)\nA(0) -> B;\nA(t) : when t > 1 -> C;\nB < A(t) -> D;\nA(t) -> A(t+1);"

              Expect.isEmpty warnings ""
          }

          test "rule order: context rules first" {
              let m, _ = compileOk "#axiom A\nA -> B;\nC < A -> D;"
              Expect.equal (m.Productions |> List.map _.HasContext) [ false; true ] "source order kept in the model"
          } ]
