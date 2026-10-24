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

let formatNumber (v: float) =
    v.ToString("R", CultureInfo.InvariantCulture)

/// Formats a string so it can be read back as an axiom: names are separated
/// by a space only where needed (`F F`), everything else is concatenated.
let format (s: LString) : string =
    let sb = StringBuilder()
    // Whether the text so far ends with a name character.
    let mutable afterName = false

    for sym in s do
        match sym with
        | Push ->
            sb.Append '[' |> ignore
            afterName <- false
        | Pop ->
            sb.Append ']' |> ignore
            afterName <- false
        | Mod(name, args) ->
            if afterName && Unicode.startsName name then
                sb.Append ' ' |> ignore

            sb.Append name |> ignore

            if args.Length > 0 then
                sb.Append('(').Append(args |> Array.map formatNumber |> String.concat ",").Append(')')
                |> ignore

            afterName <- args.Length = 0 && Unicode.endsName name

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
