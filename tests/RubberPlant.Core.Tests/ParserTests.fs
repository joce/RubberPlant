module RubberPlant.Tests.ParserTests

open Expecto
open RubberPlant
open RubberPlant.Ast
open RubberPlant.Tests.TestUtil

let axiomText src = parseOk $"#axiom %s{src}\n" |> axiomOf |> showActual

/// Every complete example in docs/language.md must parse.
let specExamples =
    [ "Koch island (§1)",
      """
(* Quadratic Koch island *)
#derivations 3
#angle 90

#axiom F-F-F-F
F -> F-F+F+F F-F-F+F;          (* book: F-F+F+FF-F-F+F *)
"""

      "Fig 1.26 plant with flowers (§1)",
      """
#derivations 5
#angle 18

#axiom plant
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
#axiom Fl
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
#derivations 4
#axiom B(2)A(4,4)
A(x,y) :
    when y <= 3 -> A(x*2, x+y)
    else        -> B(x) A(x/y, 0);
B(x) :
    when x < 1  -> C
    else        -> B(x-1);
"""

      "Fig 1.30a (§6)",
      """
#derivations 6
#angle 22.5
#ignore + -
#axiom Fb[+Fa]Fa[-Fa]Fa[+Fa]Fa
Fb < Fa -> Fb;
Fa as F;
Fb as F;
"""

      "Fig 1.31a (§6)",
      """
#derivations 30
#angle 22.5
#ignore + - F
#axiom F b F b F b
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
#derivations 10
#angle 86
#define c 1
#define p 0.3
#define q c - p
#define h (p*q)^0.5
#axiom F(1,0)
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
          test "empty axiom" { Expect.equal (axiomText "") "" "" }
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
              let rules = parseOk "#angle 3\n\n  A -> B;\nC -> D;" |> rulesOf
              Expect.equal (rules |> List.map Rule.pos) [ { Line = 3; Column = 3 }; { Line = 4; Column = 1 } ] ""
          }
          test "directive names are not keywords" {
              parseOk "#axiom axiom angle\naxiom -> step seed;" |> ignore
          }
          test "settings" {
              let f = parseOk "#derivations 5\n#angle 360/7\n#step 2\n#width 3\n#taper 10\n#seed 42\n"

              let keys =
                  f
                  |> List.choose (function
                      | Setting(k, _, _) -> Some k
                      | _ -> None)

              Expect.equal keys SettingKey.all ""
          }
          test "define" {
              match parseOk "#define h (p*q)^0.5" with
              | [ Define("h", e, _) ] -> Expect.equal (showExpr e) "((p*q)^0.5)" ""
              | r -> failtestf "unexpected %A" r
          }
          test "ignore" {
              match parseOk "#ignore + - F seg" with
              | [ Ignore(names, _) ] -> Expect.equal (names |> List.map fst) [ "+"; "-"; "F"; "seg" ] ""
              | r -> failtestf "unexpected %A" r
          } ]

let lexical =
    testList
        "comments, lines, unicode"
        [ test "nested comments" { parseOk "(* a (* b *) c *) A -> B;" |> ignore }
          test "comment inside a successor" {
              Expect.equal (parseOk "A -> B (* x *) C;" |> rulesOf |> List.length) 1 ""
          }
          test "multi-line comment doesn't end a directive" {
              match parseOk "#define a 1 + (* one\n two *) 2\nA -> B;" with
              | [ Define("a", e, _); RuleItem _ ] -> Expect.equal (showExpr e) "(1+2)" ""
              | r -> failtestf "unexpected %A" r
          }
          test "directive ends at newline" {
              match parseOk "#define q c - p\n#define r q" with
              | [ Define("q", e, _); Define("r", _, _) ] -> Expect.equal (showExpr e) "(c-p)" ""
              | r -> failtestf "unexpected %A" r
          }
          test "CRLF line endings" {
              Expect.equal (parseOk "#angle 90\r\n#axiom F\r\nF -> F F;\r\n" |> List.length) 3 ""
          }
          test "leading whitespace before a directive" { parseOk "   #angle 90" |> ignore }
          test "unicode arrow and minus" {
              match parseOk "F → F−F;" |> rulesOf with
              | [ Production(_, ProdSimple s, _) ] -> Expect.equal (showActual s) "F - F" ""
              | r -> failtestf "unexpected %A" r
          }
          test "unicode caret, tilde, apostrophe" {
              Expect.equal (axiomText "∧∼’") "^ ~ '" ""
          }
          test "byte order mark" { parseOk "\uFEFF#angle 90" |> ignore } ]

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
          test "missing ; before a directive" {
              expectParseError Codes.MissingSemicolon (2, 1) "A -> B\n#angle 3"
          }
          test "missing ; at end of file" { expectParseError Codes.MissingSemicolon (1, 7) "A -> B" }
          test "= suggests ==" { expectParseError Codes.UseCOperator (1, 15) "A(t) : when t = 0 -> B;" }
          test "& suggests &&" { expectParseError Codes.UseCOperator (1, 15) "A(t) : when t & 1 -> B;" }
          test "| suggests ||" { expectParseError Codes.UseCOperator (1, 15) "A(t) : when t | 1 -> B;" }
          test "# inside a rule" { expectParseError Codes.ReservedChar (1, 8) "A -> B #C;" }
          test "# inside a directive value" { expectParseError Codes.ReservedChar (1, 10) "#axiom F #" }
          test "backtick" { expectParseError Codes.ReservedChar (1, 6) "A -> `B;" }
          test "book left quote" { expectParseError Codes.ReservedChar (1, 6) "A -> ‘B;" }
          test "= in a module string" { expectParseError Codes.ReservedChar (1, 7) "A -> B=C;" }
          test "* in a successor" { expectParseError Codes.ReservedChar (1, 6) "A -> *;" }
          test "unknown directive" { expectParseError Codes.UnknownDirective (1, 1) "#foo 3" }
          test "reserved directive #include" { expectParseError Codes.ReservedDirective (1, 1) "#include x" }
          test "reserved directive #module" { expectParseError Codes.ReservedDirective (1, 1) "#module A" }
          test "directive after a rule on the same line" {
              expectParseError Codes.DirectiveNotAtLineStart (1, 9) "A -> B; #angle 3"
          }
          test "trailing junk after a directive" { expectParseError Codes.Syntax (1, 11) "#angle 90 deg" }
          test "directive can't continue on the next line" {
              expectParseError Codes.Syntax (1, 12) "#axiom F(1,\n2)"
          }
          test "number in module mode" { expectParseError Codes.NumberInModuleMode (1, 8) "A -> F 2;" }
          test "number in axiom" { expectParseError Codes.NumberInModuleMode (1, 8) "#axiom 0 1" }
          test "bracket in left context" { expectParseError Codes.BracketNotAllowed (1, 1) "[A] < B -> C;" }
          test "bracket in ignore" { expectParseError Codes.BracketNotAllowed (1, 11) "#ignore + [" }
          test "else after an unguarded weight" {
              expectParseError Codes.ElseAfterUnguardedWeight (1, 40) "A : when x weight 1 -> B weight 1 -> C else -> D;"
          }
          test "mixed when and weight branches" {
              expectParseError Codes.MixedBranches (1, 5) "A : when x -> B weight 1 -> C;"
          }
          test "mixed -> and as" { expectParseError Codes.MixedBranches (1, 17) "A : when x -> B when y as C;" }
          test "else with the wrong arrow" { expectParseError Codes.MixedBranches (1, 17) "A : when x as B else -> C;" }
          test "interpretation weights" { expectParseError Codes.MixedBranches (1, 14) "A : weight 1 as B;" }
          test "unterminated comment points at its start" {
              expectParseError Codes.UnterminatedComment (2, 3) "A -> B;\n  (* a (* b *) c"
          }
          test "lone _ as a module" { expectParseError Codes.ReservedName (1, 6) "A -> _;" }
          test "lone _ in an expression" { expectParseError Codes.ReservedName (1, 8) "A -> B(_);" }
          test "keyword as a module name" { expectParseError Codes.Syntax (1, 1) "when -> B;" }
          test "predecessor is a single module" { expectParseError Codes.Syntax (1, 5) "A B -> C;" }
          test "* can't be a predecessor" { expectParseError Codes.Syntax (1, 3) "* -> C;" }
          test "unclosed [" { expectParseError Codes.Syntax (1, 9) "A -> [ B;" }
          test "unmatched ]" { expectParseError Codes.Syntax (1, 8) "A -> B ];" }
          test "empty parameter list" { expectParseError Codes.Syntax (1, 8) "A -> F();" }
          test "branch list needs a branch" { expectParseError Codes.Syntax (1, 5) "A : else -> B;" }
          test "rule needs an arrow" { expectParseError Codes.Syntax (1, 3) "A ;" } ]

[<Tests>]
let tests =
    testList "Parser" [ specExampleTests; tokenization; expressions; structure; lexical; errors ]
