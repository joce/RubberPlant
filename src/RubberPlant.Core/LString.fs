/// Runtime module strings: the axiom, derived strings and drawing strings.
module RubberPlant.LString

open System.Globalization
open System.Text

/// One element of a module string. Brackets are kept flat, as in the book,
/// which makes bracket-aware context matching a walk over an array.
type Sym =
    | Push
    | Pop
    | Mod of name: string * args: float[]

type LString = Sym[]

let private isNameChar c =
    System.Char.IsLetterOrDigit c || c = '_'

let formatNumber (v: float) =
    v.ToString("R", CultureInfo.InvariantCulture)

/// Formats a string so it can be read back as an axiom: names are separated
/// by a space only where needed (`F F`), everything else is concatenated.
let format (s: LString) : string =
    let sb = StringBuilder()

    for sym in s do
        match sym with
        | Push -> sb.Append '[' |> ignore
        | Pop -> sb.Append ']' |> ignore
        | Mod(name, args) ->
            if sb.Length > 0 && isNameChar sb[sb.Length - 1] && isNameChar name[0] then
                sb.Append ' ' |> ignore

            sb.Append name |> ignore

            if args.Length > 0 then
                sb.Append('(').Append(args |> Array.map formatNumber |> String.concat ",").Append(')')
                |> ignore

    sb.ToString()

/// For each bracket, the index of its partner (-1 for modules).
let matchBrackets (s: LString) : int[] =
    let partner = Array.create s.Length -1
    let stack = System.Collections.Generic.Stack<int>()

    for i in 0 .. s.Length - 1 do
        match s[i] with
        | Push -> stack.Push i
        | Pop ->
            let j = stack.Pop()
            partner[i] <- j
            partner[j] <- i
        | Mod _ -> ()

    partner
