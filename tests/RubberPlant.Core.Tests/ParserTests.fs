module RubberPlant.Tests.ParserTests

open Expecto
open RubberPlant
open RubberPlant.Ast
open RubberPlant.Tests.TestUtil

let axiomText src = parseOk $"axiom: %s{src};" |> axiomOf |> showActual

/// Every complete example in docs/language.md must parse.
let specExamples =
    [ "Koch island (§1)",
      """
(* Quadratic Koch island *)
settings {
  derivations = 3;
  angle = 90;
}

axiom: F-F-F-F;
F -> F-F+F+F F-F-F+F;          (* book: F-F+F+FF-F-F+F *)
"""

      "Fig 1.26 plant with flowers (§1)",
      """
settings { derivations = 5; angle = 18; }

axiom: plant;
plant     -> internode + [ plant + flower ] - - //
             [ - - leaf ] internode [ + + leaf ] -
             [ plant flower ] + + plant flower;
internode -> F seg [ // & & leaf ] [ // ^ ^ leaf ] F seg;
seg       -> seg F seg;
leaf      -> [ ' { +f-f f-f+ | +f-f f-f } ];
flower    -> [ & & & pedicel / wedge //// wedge ////
             wedge //// wedge //// wedge ];
pedicel   -> F F;
wedge     -> [ ^F ] [ { & & & & -f+f | -f+f } ];
"""

      "rule examples (§3.4)",
      """
F -> F[+F]F[-F]F;                        (* D0L *)
b < a -> b;                              (* 1L, left context *)
Fu < Fa > Fa -> Fu;                      (* 2L, multi-letter names *)
* < + > * -> -;                          (* wildcard contexts *)
B(0) -> C;                               (* literal pattern *)
A(_, y) -> A(1, y);                      (* wildcard pattern *)

A(t) : when t > 5 -> B(t+1);             (* one guard *)
A(x, y) :
    when y <= 3 -> A(x*2, x+y)
    when y <= 9 -> A(x, y-1)
    else        -> B(x) A(x/y, 0);
A(x) < B(y) > C(z) : when x+y+z > 10 -> E((x+y)/2) F((y+z)/2);

F :                                      (* stochastic (no else: branches unguarded) *)
    weight 1 -> F[+F]F[-F]F
    weight 1 -> F[+F]F
    weight 1 -> F[-F]F;
A(t) :                                   (* guarded + weighted *)
    when t > 3 weight 2 -> B(t)
    when t > 3 weight 1 -> C(t)
    else                -> A(t+1);

Fl as F;                                 (* interpretation *)
leaf(s) :
    when s < 1 as F
    else       as [ { +f-f f-f+ } ];
"""

      "dragon curve (§4.6)",
      """
axiom: Fl;
Fl -> Fl + Fr +;
Fr -> - Fl - Fr;
Fl as F;
Fr as F;
"""

      "interpretation with state (§4.6)",
      """
leaf(age) :
    when age < 3 as F(0.5)                         (* young leaf: short stub *)
    else         as [ { +f-f f-f+ | +f-f f-f } ];  (* mature leaf: polygon *)
"""

      "non-iterated interpretation (§4.6)",
      """
A as B;
B as F;
"""

      "L-system 1.7 (§6)",
      """
settings { derivations = 4; }
axiom: B(2)A(4,4);
A(x,y) :
    when y <= 3 -> A(x*2, x+y)
    else        -> B(x) A(x/y, 0);
B(x) :
    when x < 1  -> C
    else        -> B(x-1);
"""

      "Fig 1.30a (§6)",
      """
settings { derivations = 6; angle = 22.5; }
ignore: + -;
axiom: Fb[+Fa]Fa[-Fa]Fa[+Fa]Fa;
Fb < Fa -> Fb;
Fa as F;
Fb as F;
"""

      "Fig 1.31a (§6)",
      """
settings { derivations = 30; angle = 22.5; }
ignore: + - F;
axiom: F b F b F b;
a < a > a -> a;
a < a > b -> b[+F b F b];
a < b > a -> b;
a < b > b -> b;
b < a > a -> a;
b < a > b -> b F b;
b < b > a -> a;
b < b > b -> a;
* < + > * -> -;
* < - > * -> +;
"""

      "Fig 1.37b (§6)",
      """
settings { derivations = 10; angle = 86; }
let c = 1;
let p = 0.3;
let q = c - p;
let h = (p*q)^0.5;
axiom: F(1,0);
F(x, 0) -> F(x*p,2) + F(x*h,1) - - F(x*h,1) + F(x*q,0);   (* literal pattern for t = 0 *)
F(x, t) -> F(x, t-1);
""" ]

let specExampleTests =
    testList
        "spec examples parse"
        [ for name, src in specExamples -> test name { parseOk src |> ignore } ]

let tokenization =
    testList
        "tokenization (§2.4)"
        [ test "operators between names" { Expect.equal (axiomText "F-F+F") "F - F + F" "" }
          test "maximal munch: FF is one name" { Expect.equal (axiomText "FF") "FF" "" }
          test "space separates names" { Expect.equal (axiomText "F F") "F F" "" }
          test "brackets" { Expect.equal (axiomText "Fb[+Fa]Fa") "Fb [+ Fa] Fa" "" }
          test "// is two symbols" { Expect.equal (axiomText "//") "/ /" "" }
          test "parameters end a module" { Expect.equal (axiomText "B(2)A(4,4)") "B(2) A(4,4)" "" }
          test "word names" { Expect.equal (axiomText "seg F seg") "seg F seg" "" }
          test "tilde then name" { Expect.equal (axiomText "~H(1)") "~ H(1)" "" }
          test "underscore names" { Expect.equal (axiomText "_tmp apex2") "_tmp apex2" "" }
          test "unicode letters" { Expect.equal (axiomText "été F") "été F" "" }
          test "symbols" { Expect.equal (axiomText "+-&^\\/|{}!'~$%.@?") "+ - & ^ \\ / | { } ! ' ~ $ % . @ ?" "" }
          test "symbol with parameter" { Expect.equal (axiomText "+(30)!(2)") "+(30) !(2)" "" }
          test "empty axiom is legal" { Expect.equal (axiomText "") "" "" }
          test "supplementary-plane letters are one name" { Expect.equal (axiomText "𝐀𝐁 F") "𝐀𝐁 F" "" }
          test "supplementary-plane symbol is one module" { Expect.equal (axiomText "F🌱F") "F 🌱 F" "" }
          test "trivia before a parameter list (Appendix A is token-based)" {
              Expect.equal (axiomText "F (1) G (* c *) (2)") "F(1) G(2)" ""
          }
          test "supplementary-plane predecessor" {
              match parseOk "axiom: 𝐀;\n𝐀 -> F;" |> rulesOf with
              | [ Production(h, _, _) ] -> Expect.equal h.Pred.Name "𝐀" ""
              | r -> failtestf "unexpected %A" r
          }
          test "minus then arrow" {
              let f = parseOk "- -> +;"

              match rulesOf f with
              | [ Production(h, ProdSimple succ, _) ] ->
                  Expect.equal h.Pred.Name "-" ""
                  Expect.equal (showActual succ) "+" ""
              | r -> failtestf "unexpected %A" r
          } ]

let expressions =
    testList
        "expressions (§3.5)"
        [ test "unary minus binds looser than ^" { Expect.equal (exprOk "-2^2") "(-(2^2))" "" }
          test "^ is right-associative" { Expect.equal (exprOk "2^3^2") "(2^(3^2))" "" }
          test "^ takes a unary exponent" { Expect.equal (exprOk "2^-1") "(2^(-1))" "" }
          test "* before +" { Expect.equal (exprOk "1+2*3") "(1+(2*3))" "" }
          test "left-associative -" { Expect.equal (exprOk "1-2-3") "((1-2)-3)" "" }
          test "&& before ||" { Expect.equal (exprOk "a || b && c") "(a||(b&&c))" "" }
          test "! before &&" { Expect.equal (exprOk "!a && b") "((!a)&&b)" "" }
          test "! applies to a relation" { Expect.equal (exprOk "!a < b") "(!(a<b))" "" }
          test "relations" { Expect.equal (exprOk "x+1 <= y*2") "((x+1)<=(y*2))" "" }
          test "!=" { Expect.equal (exprOk "a != b") "(a!=b)" "" }
          test "leading-dot number" { Expect.equal (exprOk ".33") "0.33" "" }
          test "exponent" { Expect.equal (exprOk "1.5e3") "1500" "" }
          test "call" { Expect.equal (exprOk "atan2(y, x)") "atan2(y,x)" "" }
          test "call without args" { Expect.equal (exprOk "f()") "f()" "" }
          test "parentheses" { Expect.equal (exprOk "(p*q)^0.5") "((p*q)^0.5)" "" }
          test "unicode minus in exponents" {
              Expect.equal (exprOk "1e−3") (exprOk "1e-3") ""
              Expect.equal (exprOk "1E−3") "0.001" ""
              Expect.equal (axiomText "F(2.5e−1)") "F(0.25)" ""
          }
          test "unicode operators" { Expect.equal (exprOk "a ≤ b") "(a<=b)" "" }
          test "unicode minus and caret" { Expect.equal (exprOk "a − b ∧ 2") "(a-(b^2))" "" }
          test "non-associative relations" {
              match Parser.parseExpr "1 < 2 < 3" with
              | Ok e -> failtestf "should fail, got %s" (showExpr e)
              | Error _ -> ()
          } ]

let structure =
    testList
        "structure"
        [ test "context head" {
              match parseOk "A(x) < B(y) > C(z) [ D ] * -> E;" |> rulesOf with
              | [ Production(h, ProdSimple _, _) ] ->
                  Expect.equal h.Left [ LModule { Name = "A"; Params = [ PVar("x", { Line = 1; Column = 3 }) ]; Pos = { Line = 1; Column = 1 } } ] ""
                  Expect.equal h.Pred.Name "B" ""

                  match h.Right with
                  | [ RModule c; RBranch [ RModule d ]; RAny ] ->
                      Expect.equal c.Name "C" ""
                      Expect.equal d.Name "D" ""
                  | r -> failtestf "unexpected right context %A" r
              | r -> failtestf "unexpected %A" r
          }
          test "patterns" {
              match parseOk "A(x, 0, -1.5, _, _) -> B;" |> rulesOf with
              | [ Production(h, _, _) ] ->
                  match h.Pred.Params with
                  | [ PVar("x", _); PNum 0.0; PNum -1.5; PWild; PWild ] -> ()
                  | p -> failtestf "unexpected patterns %A" p
              | r -> failtestf "unexpected %A" r
          }
          test "guarded rule" {
              match parseOk "A(t) : when t > 1 -> B when t > 0 -> C else -> D;" |> rulesOf with
              | [ Production(_, ProdGuarded([ b1; b2 ], Some e), _) ] ->
                  Expect.equal (showExpr b1.Guard) "(t>1)" ""
                  Expect.equal (showActual b2.Successor) "C" ""
                  Expect.equal (showActual e) "D" ""
              | r -> failtestf "unexpected %A" r
          }
          test "weighted rule" {
              match parseOk "F : weight .5 -> A when x weight 1 -> B;" |> rulesOf with
              | [ Production(_, ProdWeighted([ w1; w2 ], None), _) ] ->
                  Expect.isNone w1.Guard ""
                  Expect.equal (showExpr w1.Weight) "0.5" ""
                  Expect.equal (w2.Guard |> Option.map showExpr) (Some "x") ""
              | r -> failtestf "unexpected %A" r
          }
          test "guarded weighted rule with else" {
              match parseOk "A(t) : when t > 3 weight 2 -> B(t) else -> C;" |> rulesOf with
              | [ Production(_, ProdWeighted([ _ ], Some _), _) ] -> ()
              | r -> failtestf "unexpected %A" r
          }
          test "interpretation rule" {
              match parseOk "leaf(s) : when s < 1 as F else as G;" |> rulesOf with
              | [ Interpretation(_, InterpGuarded([ _ ], Some _), _) ] -> ()
              | r -> failtestf "unexpected %A" r
          }
          test "free layout: several rules on one line" {
              Expect.equal (parseOk "A -> B; B -> A;" |> rulesOf |> List.length) 2 ""
          }
          test "successor spans lines" {
              match parseOk "A -> B\n  C\n  D;" |> rulesOf with
              | [ Production(_, ProdSimple s, _) ] -> Expect.equal (showActual s) "B C D" ""
              | r -> failtestf "unexpected %A" r
          }
          test "rule positions" {
              let rules = parseOk "settings { angle = 3; }\n\n  A -> B;\nC -> D;" |> rulesOf
              Expect.equal (rules |> List.map Rule.pos) [ { Line = 3; Column = 3 }; { Line = 4; Column = 1 } ] ""
          }
          test "seed is an ordinary name" { parseOk "axiom: seed;\nseed -> F(seed);" |> ignore } ]

let prelude =
    testList
        "prelude (§3.2)"
        [ test "settings, with a position per entry" {
              match parseOk "settings { derivations = 5; angle = 360/7;\n  step = 2; width = 3; taper = 10; }" with
              | [ Settings(entries, p) ] ->
                  Expect.equal p { Line = 1; Column = 1 } "block"
                  Expect.equal (entries |> List.map (fun (k, _, _) -> k)) SettingKey.all ""

                  Expect.equal
                      (entries |> List.map (fun (_, _, p) -> p.Line, p.Column))
                      [ 1, 12; 1, 29; 2, 3; 2, 13; 2, 24 ]
                      "entry positions"

                  let _, angle, _ = entries[1]
                  Expect.equal (showExpr angle) "(360/7)" ""
              | r -> failtestf "unexpected %A" r
          }
          test "empty settings block" {
              match parseOk "settings { }" with
              | [ Settings([], _) ] -> ()
              | r -> failtestf "unexpected %A" r
          }
          test "let" {
              match parseOk "let h = (p*q)^0.5;" with
              | [ Let("h", e, _) ] -> Expect.equal (showExpr e) "((p*q)^0.5)" ""
              | r -> failtestf "unexpected %A" r
          }
          test "ignore" {
              match parseOk "ignore: + - F seg;" with
              | [ Ignore(names, _) ] -> Expect.equal (names |> List.map fst) [ "+"; "-"; "F"; "seg" ] ""
              | r -> failtestf "unexpected %A" r
          }
          test "trivia before the colon" { Expect.equal (parseOk "axiom : F;" |> axiomOf |> showActual) "F" "" }
          test "braces in an axiom are turtle symbols" {
              Expect.equal (axiomText "{f+f}") "{ f + f }" ""
          }
          test "interleaved prelude items" {
              let f =
                  parseOk
                      "let a = 1; settings { angle = a; } let b = 2; ignore: F; let c = a + b; axiom: F(c); F -> F;"

              let kinds =
                  f
                  |> List.map (function
                      | Settings _ -> "settings"
                      | Let(n, _, _) -> n
                      | Ignore _ -> "ignore"
                      | Axiom _ -> "axiom"
                      | RuleItem _ -> "rule")

              Expect.equal kinds [ "a"; "settings"; "b"; "ignore"; "c"; "axiom"; "rule" ] ""
          }
          test "statements spread over lines" {
              match parseOk "let\n  a\n  =\n  1\n  +\n  2\n;\nignore:\n  +\n  -\n;\nsettings\n{\n angle\n =\n 3\n ;\n}\naxiom:\nF\n;" with
              | [ Let("a", e, _); Ignore(names, _); Settings([ (Angle, _, p) ], _); Axiom(a, _) ] ->
                  Expect.equal (showExpr e) "(1+2)" ""
                  Expect.equal (names |> List.map fst) [ "+"; "-" ] ""
                  Expect.equal p { Line = 14; Column = 2 } "angle"
                  Expect.equal (showActual a) "F" ""
              | r -> failtestf "unexpected %A" r
          }
          test "several statements on one line" {
              Expect.equal (parseOk "settings { angle = 3; } let a = 1; ignore: F; axiom: F; F -> F; G -> G;" |> List.length) 6 ""
          }
          test "comments between any tokens" {
              let src =
                  "let (* a *) a (* b *) = (* c *) 1 (* d *) ; (* e *) axiom (* f *) : (* g *) F (* h *) ;"
                  + " settings (* i *) { (* j *) angle (* k *) = (* l *) 3 (* m *) ; (* n *) }"

              Expect.equal (parseOk src |> List.length) 3 ""
          }
          test "a prelude statement after a rule parses (the checker rejects it)" {
              Expect.equal (parseOk "A -> B; axiom: A;" |> List.length) 2 ""
          } ]

let lexical =
    testList
        "comments, lines, unicode"
        [ test "nested comments" { parseOk "(* a (* b *) c *) A -> B;" |> ignore }
          test "comment inside a successor" {
              Expect.equal (parseOk "A -> B (* x *) C;" |> rulesOf |> List.length) 1 ""
          }
          test "multi-line comment inside a let" {
              match parseOk "let a = 1 + (* one\n two *) 2;\nA -> B;" with
              | [ Let("a", e, _); RuleItem _ ] -> Expect.equal (showExpr e) "(1+2)" ""
              | r -> failtestf "unexpected %A" r
          }
          test "CRLF line endings" {
              Expect.equal (parseOk "settings { angle = 90; }\r\naxiom: F;\r\nF -> F F;\r\n" |> List.length) 3 ""
          }
          test "leading whitespace" { parseOk "   settings { angle = 90; }" |> ignore }
          test "unicode arrow and minus" {
              match parseOk "F → F−F;" |> rulesOf with
              | [ Production(_, ProdSimple s, _) ] -> Expect.equal (showActual s) "F - F" ""
              | r -> failtestf "unexpected %A" r
          }
          test "unicode caret, tilde, apostrophe" {
              Expect.equal (axiomText "∧∼’") "^ ~ '" ""
          }
          test "comment before a prelude statement" {
              Expect.equal (parseOk "(* note *) axiom: F;" |> List.length) 1 ""
          }
          test "statement after a multi-line comment" {
              Expect.equal (parseOk "(* one\n two *) settings { angle = 90; } axiom: F;" |> List.length) 2 ""
          }
          test "statement after a nested comment" {
              Expect.equal (parseOk "(* a (* b *)\n *)   settings { angle = 90; }\naxiom: F;" |> List.length) 2 ""
          }
          test "byte order mark" { parseOk "﻿settings { angle = 90; }" |> ignore } ]

/// Every reserved word (§2.6, A.1), none of which can be used as a name.
let reservedWords =
    [ "settings"; "let"; "ignore"; "axiom"; "include"; "module" ] // declaration keywords
    @ [ "as"; "when"; "else"; "weight" ] // rule keywords
    @ (SettingKey.all |> List.map SettingKey.name)

let errors =
    testList
        "errors"
        [ test "missing ; before the next rule" {
              expectParseError Codes.MissingSemicolon (2, 3) "A -> B C\nD -> E;"
          }
          test "missing ; before a context rule" {
              expectParseError Codes.MissingSemicolon (2, 3) "A -> B\nb < a -> b;"
          }
          test "missing ; before a branch rule" {
              expectParseError Codes.MissingSemicolon (2, 6) "A -> B\nC(x) : when x -> D;"
          }
          test "missing ; before a prelude keyword" {
              expectParseError Codes.MissingSemicolon (2, 1) "A -> B\nlet a = 3;"
          }
          test "missing ; before any prelude statement or interpretation rule" {
              expectParseError Codes.MissingSemicolon (2, 1) "A -> B\nsettings { angle = 3; }"
              expectParseError Codes.MissingSemicolon (2, 1) "A -> B\naxiom: F;"
              expectParseError Codes.MissingSemicolon (2, 1) "A -> B\nignore: F;"
              expectParseError Codes.MissingSemicolon (2, 3) "A -> B\nC as F;"
          }
          test "misplaced keyword in a module string" {
              expectParseError Codes.MisplacedKeyword (1, 8) "A -> B let;"
              expectParseError Codes.MisplacedKeyword (1, 6) "A -> settings;"
              expectParseError Codes.MisplacedKeyword (1, 8) "A -> B axiom;"
              expectParseError Codes.MisplacedKeyword (1, 10) "axiom: A ignore;"
              expectParseError Codes.MisplacedKeyword (1, 8) "A -> B when x -> C;"
              expectParseError Codes.MisplacedKeyword (1, 8) "A -> B weight 1 -> C;"
              expectParseError Codes.MisplacedKeyword (1, 8) "A -> B else -> C;"
              expectParseError Codes.MisplacedKeyword (1, 27) "A : when 1 -> B else -> C else -> D;"
              let d = parseErr "A -> B let;"
              Expect.stringContains d.Message "keyword `let` can't appear in a module string" ""
          }
          test "misplaced keyword in an expression" {
              expectParseError Codes.MisplacedKeyword (1, 11) "let a = 1 let;"
              expectParseError Codes.MisplacedKeyword (1, 11) "let a = 1 when;"
              expectParseError Codes.MisplacedKeyword (1, 11) "let a = 1 as F;"
              expectParseError Codes.MisplacedKeyword (1, 22) "settings { angle = 3 axiom; }"
          }
          test "missing ; at end of file" { expectParseError Codes.MissingSemicolon (1, 7) "A -> B" }
          test "missing ; after let" {
              expectParseError Codes.MissingSemicolon (2, 1) "let a = 1\naxiom: F;"
              expectParseError Codes.MissingSemicolon (2, 1) "let a = 1\nF -> F;"
              expectParseError Codes.MissingSemicolon (1, 10) "let a = 1"
          }
          test "missing ; after ignore:" {
              expectParseError Codes.MissingSemicolon (2, 1) "ignore: F\naxiom: F;"
          }
          test "missing ; after axiom:" {
              expectParseError Codes.MissingSemicolon (2, 3) "axiom: F\nF -> F F;"
              expectParseError Codes.MissingSemicolon (2, 1) "axiom: F\nlet a = 1;"
          }
          test "missing ; after a settings entry" {
              expectParseError Codes.MissingSemicolon (1, 22) "settings { angle = 3 step = 1; }"
              expectParseError Codes.MissingSemicolon (1, 22) "settings { angle = 3 }"
          }
          test "missing }" {
              expectParseError Codes.MissingBrace (2, 1) "settings { angle = 3;\naxiom: F;"
              expectParseError Codes.MissingBrace (2, 1) "settings { angle = 3;\nF -> F;"
              expectParseError Codes.MissingBrace (1, 22) "settings { angle = 3;"
          }
          test "settings needs {" { expectParseError Codes.Syntax (1, 10) "settings angle = 3;" }
          test "unknown setting" { expectParseError Codes.UnknownSetting (1, 12) "settings { foo = 1; }" }
          test "seed isn't a setting" {
              let d = parseErr "settings { seed = 1; }"
              Expect.equal (d.Code, d.Pos) (Codes.UnknownSetting, Some { Line = 1; Column = 12 }) ""
              Expect.stringContains d.Message "--seed" "points at the CLI"
          }
          test "empty ignore:" {
              expectParseError Codes.EmptyIgnore (1, 1) "ignore: ;"
              expectParseError Codes.EmptyIgnore (1, 1) "ignore:;"
          }
          test ": at the start of a statement" { expectParseError Codes.Syntax (1, 1) ": F;" }
          test "axiom without :" { expectParseError Codes.Syntax (1, 7) "axiom F;" }
          test "ignore without :" { expectParseError Codes.Syntax (1, 8) "ignore F;" }
          test "let without =" { expectParseError Codes.Syntax (1, 7) "let x 3;" }
          test "settings entry without =" { expectParseError Codes.Syntax (1, 18) "settings { angle 3; }" }
          test "let with : suggests =" { expectParseError Codes.WrongSeparator (1, 6) "let x: 3;" }
          test "settings entry with : suggests =" {
              expectParseError Codes.WrongSeparator (1, 17) "settings { angle: 3; }"
          }
          test "axiom with = suggests :" { expectParseError Codes.WrongSeparator (1, 7) "axiom = F;" }
          test "ignore with = suggests :" { expectParseError Codes.WrongSeparator (1, 8) "ignore = F;" }
          test "= suggests ==" { expectParseError Codes.UseCOperator (1, 15) "A(t) : when t = 0 -> B;" }
          test "& suggests &&" { expectParseError Codes.UseCOperator (1, 15) "A(t) : when t & 1 -> B;" }
          test "| suggests ||" { expectParseError Codes.UseCOperator (1, 15) "A(t) : when t | 1 -> B;" }
          test "# at the start of the file" { expectParseError Codes.ReservedChar (1, 1) "#angle 3" }
          test "# at the start of a line" { expectParseError Codes.ReservedChar (2, 1) "axiom: F;\n#derivations 3" }
          test "# inside a rule" { expectParseError Codes.ReservedChar (1, 8) "A -> B #C;" }
          test "# inside an axiom" { expectParseError Codes.ReservedChar (1, 10) "axiom: F #;" }
          test "# inside an expression" { expectParseError Codes.ReservedChar (1, 11) "let a = 1 # 2;" }
          test "# inside a settings block" { expectParseError Codes.ReservedChar (1, 12) "settings { # }" }
          test "backtick" { expectParseError Codes.ReservedChar (1, 6) "A -> `B;" }
          test "book left quote" { expectParseError Codes.ReservedChar (1, 6) "A -> ‘B;" }
          test "= in a module string" { expectParseError Codes.ReservedChar (1, 7) "A -> B=C;" }
          test "* in a successor" { expectParseError Codes.ReservedChar (1, 6) "A -> *;" }
          test "reserved words can't be module names" {
              for w in reservedWords do
                  expectParseError Codes.ReservedName (1, 1) $"%s{w} -> B;"
          }
          test "reserved words can't be let names" {
              for w in reservedWords do
                  expectParseError Codes.ReservedName (1, 5) $"let %s{w} = 1;"
          }
          test "reserved words can't be parameter names" {
              for w in reservedWords do
                  expectParseError Codes.ReservedName (1, 3) $"A(%s{w}) -> B;"
          }
          test "keyword in a context" {
              expectParseError Codes.ReservedName (1, 5) "A > when -> B;"
              expectParseError Codes.ReservedName (1, 5) "A < let -> B;"
          }
          test "setting name in a successor or an axiom" {
              expectParseError Codes.ReservedName (1, 8) "A -> B angle;"
              expectParseError Codes.ReservedName (1, 8) "axiom: step;"
          }
          test "setting name in an expression: settings aren't values" {
              expectParseError Codes.ReservedName (1, 31) "settings { angle = 10; step = angle; }"
              expectParseError Codes.ReservedName (1, 11) "A(x) -> B(angle);"
          }
          test "include and module are reserved everywhere" {
              expectParseError Codes.ReservedName (1, 1) "include x;"
              expectParseError Codes.ReservedName (1, 1) "module A;"
              expectParseError Codes.ReservedName (1, 6) "A -> include;"
              expectParseError Codes.ReservedName (1, 9) "let a = module;"
              expectParseError Codes.ReservedName (1, 9) "ignore: module;"
          }
          test "number in module mode" { expectParseError Codes.NumberInModuleMode (1, 8) "A -> F 2;" }
          test "number in axiom" { expectParseError Codes.NumberInModuleMode (1, 8) "axiom: 0 1;" }
          test "bracket in left context" { expectParseError Codes.BracketNotAllowed (1, 1) "[A] < B -> C;" }
          test "bracket in ignore" { expectParseError Codes.BracketNotAllowed (1, 11) "ignore: + [;" }
          test "else after an unguarded weight" {
              expectParseError Codes.ElseAfterUnguardedWeight (1, 40) "A : when x weight 1 -> B weight 1 -> C else -> D;"
          }
          test "mixed when and weight branches" {
              expectParseError Codes.MixedBranches (1, 5) "A : when x -> B weight 1 -> C;"
          }
          test "mixed -> and as" { expectParseError Codes.MixedBranches (1, 17) "A : when x -> B when y as C;" }
          test "else with the wrong arrow" { expectParseError Codes.MixedBranches (1, 17) "A : when x as B else -> C;" }
          test "weighted branch after an interpretation branch" {
              expectParseError Codes.MixedBranches (1, 17) "A : when 0 as F weight 1 -> X;"
          }
          test "guarded weighted branch after an interpretation branch" {
              expectParseError Codes.MixedBranches (1, 17) "A : when 0 as F when 1 weight 1 -> X;"
          }
          test "interpretation weights" { expectParseError Codes.MixedBranches (1, 14) "A : weight 1 as B;" }
          test "unterminated comment points at its start" {
              expectParseError Codes.UnterminatedComment (2, 3) "A -> B;\n  (* a (* b *) c"
          }
          test "lone _ as a module" { expectParseError Codes.ReservedName (1, 6) "A -> _;" }
          test "lone _ in an expression" { expectParseError Codes.ReservedName (1, 8) "A -> B(_);" }
          test "lone _ as a let name" { expectParseError Codes.ReservedName (1, 5) "let _ = 1;" }
          test "predecessor is a single module" { expectParseError Codes.Syntax (1, 5) "A B -> C;" }
          test "* can't be a predecessor" { expectParseError Codes.Syntax (1, 3) "* -> C;" }
          test "unclosed [" { expectParseError Codes.Syntax (1, 9) "A -> [ B;" }
          test "unmatched ]" { expectParseError Codes.Syntax (1, 8) "A -> B ];" }
          test "empty parameter list" { expectParseError Codes.Syntax (1, 8) "A -> F();" }
          test "branch list needs a branch" { expectParseError Codes.Syntax (1, 5) "A : else -> B;" }
          test "rule needs an arrow" { expectParseError Codes.Syntax (1, 3) "A ;" } ]

[<Tests>]
let tests =
    testList "Parser" [ specExampleTests; tokenization; expressions; structure; prelude; lexical; errors ]
