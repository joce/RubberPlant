/// Expression evaluation (§4.1). All values are 64-bit floats; true is 1.
module RubberPlant.Eval

open RubberPlant.Ast
open RubberPlant.Model

/// Raised inside the core and turned into a diagnostic at the API boundary.
exception EvalError of code: string * message: string

let inline private truth b = if b then 1.0 else 0.0

let private check (v: float) =
    if System.Double.IsNaN v then
        raise (EvalError(Codes.NotANumber, "the result is not a number (NaN)"))

    v

/// Evaluates `e` with the given parameter bindings.
let rec eval (args: float[]) (e: CExpr) : float =
    match e with
    | CNum v -> v
    | CParam i -> args[i]
    | CNeg e -> -(eval args e)
    | CNot e -> truth (eval args e = 0.0)
    | CCall(f, xs) -> Builtins.apply f (Array.map (eval args) xs) |> check
    // && and || short-circuit, so `x != 0 && 1/x > 2` is safe.
    | CBin(And, a, b) -> truth (eval args a <> 0.0 && eval args b <> 0.0)
    | CBin(Or, a, b) -> truth (eval args a <> 0.0 || eval args b <> 0.0)
    | CBin(op, a, b) ->
        let x = eval args a
        let y = eval args b

        match op with
        | Lt -> truth (x < y)
        | Le -> truth (x <= y)
        | Gt -> truth (x > y)
        | Ge -> truth (x >= y)
        | Eq -> truth (x = y)
        | Ne -> truth (x <> y)
        | Add -> x + y |> check
        | Sub -> x - y |> check
        | Mul -> x * y |> check
        | Div
        | Mod when y = 0.0 -> raise (EvalError(Codes.DivisionByZero, "division by zero"))
        | Div -> x / y |> check
        | Mod -> x % y |> check
        | Pow -> x ** y |> check
        | And
        | Or -> failwith "unreachable"

let isTrue (v: float) = v <> 0.0
