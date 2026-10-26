module RubberPlant.Tests.TestUtil

open Expecto
open RubberPlant
open RubberPlant.Ast

let formatDiags (ds: Diagnostic list) =
    ds |> List.map (Diagnostic.format "test.rp") |> String.concat "\n"

let parseOk (src: string) : File =
    match Parser.parse src with
    | Ok f -> f
    | Error ds -> failtestf "expected a successful parse, got:\n%s" (formatDiags ds)

let parseErr (src: string) : Diagnostic =
    match Parser.parse src with
    | Ok f -> failtestf "expected a parse error, got:\n%A" f
    | Error [ d ] -> d
    | Error ds -> failtestf "expected exactly one diagnostic, got:\n%s" (formatDiags ds)

/// Asserts a parse error with the given code at the given line and column.
let expectParseError (code: string) (line: int, col: int) (src: string) =
    let d = parseErr src
    let actual = (d.Code, d.Pos)
    let wanted = (code, Some { Line = line; Column = col })

    if actual <> wanted then
        failtestf "expected %s at (%d,%d), got:\n%s" code line col (Diagnostic.format "test.rp" d)

let rec showExpr (e: Expr) =
    let opName =
        function
        | Or -> "||"
        | And -> "&&"
        | Lt -> "<"
        | Le -> "<="
        | Gt -> ">"
        | Ge -> ">="
        | Eq -> "=="
        | Ne -> "!="
        | Add -> "+"
        | Sub -> "-"
        | Mul -> "*"
        | Div -> "/"
        | Mod -> "%"
        | Pow -> "^"

    match e with
    | Num v -> string v
    | Ident(n, _) -> n
    | Call(n, args, _) -> $"""%s{n}(%s{args |> List.map showExpr |> String.concat ","})"""
    | Neg e -> $"(-%s{showExpr e})"
    | Not e -> $"(!%s{showExpr e})"
    | Binary(o, a, b) -> $"(%s{showExpr a}%s{opName o}%s{showExpr b})"

/// Renders a module string compactly: modules separated by spaces,
/// parameters fully parenthesized.
let rec showActual (s: ActualString) =
    s
    |> List.map (function
        | Module m ->
            if m.Args.IsEmpty then
                m.Name
            else
                $"""%s{m.Name}(%s{m.Args |> List.map showExpr |> String.concat ","})"""
        | Branch b -> $"[%s{showActual b}]"
        | Cut -> "%")
    |> String.concat " "

let axiomOf (f: File) =
    f
    |> List.pick (function
        | Axiom(a, _) -> Some a
        | _ -> None)

let rulesOf (f: File) =
    f
    |> List.choose (function
        | RuleItem r -> Some r
        | _ -> None)

let exprOk (src: string) =
    match Parser.parseExpr src with
    | Ok e -> showExpr e
    | Error ds -> failtestf "expected an expression, got:\n%s" (formatDiags ds)
