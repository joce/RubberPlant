/// Command-line interface. All I/O lives here; the core is pure.
module RubberPlant.Cli.Program

open System
open System.Globalization
open System.IO
open RubberPlant
open RubberPlant.Model
open RubberPlant.Backends

let usage =
    """Usage:
  rubberplant render <file.rp> [-o <out.svg>] [--size <px>] [options]
  rubberplant derive <file.rp> [--drawing] [options]
  rubberplant check  <file.rp>

Commands:
  render   Derive, interpret and draw the L-system as SVG.
  derive   Print the derived string (with --drawing: the string after
           interpretation rules, as the turtle sees it).
  check    Parse and check the file; report errors and warnings.

Options (override the file's settings):
  -n, --derivations <n>   number of derivation steps
      --angle <degrees>   default turn angle
      --step <d>          default step length
      --width <w>         initial line width
      --taper <percent>   width decrease applied by `!`
      --seed <n>          random seed (default 0)

Render options:
  -o, --output <file>     output file (default: the input file with .svg)
      --size <px>         size of the longer side of the image (default 800)
"""

type Command =
    | Render
    | Derive
    | Check

type Args =
    { Command: Command
      Input: string
      Output: string option
      Size: float option
      Drawing: bool
      Derivations: int option
      Angle: float option
      Step: float option
      Width: float option
      Taper: float option
      Seed: int64 option }

exception UsageError of string

let private parseFloat (opt: string) (v: string) =
    match Double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture) with
    | true, x when Double.IsFinite x -> x
    | _ -> raise (UsageError $"%s{opt} expects a number, got '%s{v}'")

let private parseArgs (argv: string list) : Args =
    let command, rest =
        match argv with
        | "render" :: rest -> Render, rest
        | "derive" :: rest -> Derive, rest
        | "check" :: rest -> Check, rest
        | c :: _ -> raise (UsageError $"unknown command '%s{c}'")
        | [] -> raise (UsageError "missing command")

    let empty =
        { Command = command
          Input = ""
          Output = None
          Size = None
          Drawing = false
          Derivations = None
          Angle = None
          Step = None
          Width = None
          Taper = None
          Seed = None }

    let rec go args acc =
        match args with
        | [] -> acc
        | ("-o" | "--output") :: v :: rest -> go rest { acc with Output = Some v }
        | "--size" :: v :: rest ->
            let s = parseFloat "--size" v

            let minSize = 2.0 * Svg.defaultOptions.Margin

            if s <= minSize then
                raise (UsageError $"--size must be greater than %g{minSize} (twice the margin)")

            go rest { acc with Size = Some s }
        | "--drawing" :: rest -> go rest { acc with Drawing = true }
        | ("-n" | "--derivations") :: v :: rest ->
            match Int32.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture) with
            | true, n -> go rest { acc with Derivations = Some n }
            | _ -> raise (UsageError $"--derivations expects a non-negative integer, got '%s{v}'")
        | "--angle" :: v :: rest -> go rest { acc with Angle = Some(parseFloat "--angle" v) }
        | "--step" :: v :: rest -> go rest { acc with Step = Some(parseFloat "--step" v) }
        | "--width" :: v :: rest ->
            // Same domain as the `width` setting; overrides are applied after the checker runs.
            let w = parseFloat "--width" v

            if w < 0.0 then
                raise (UsageError "--width must be non-negative")

            go rest { acc with Width = Some w }
        | "--taper" :: v :: rest ->
            let t = parseFloat "--taper" v

            if t < 0.0 || t > 100.0 then
                raise (UsageError "--taper must be between 0 and 100")

            go rest { acc with Taper = Some t }
        | "--seed" :: v :: rest ->
            // Integers a float represents exactly (§4.7).
            match Int64.TryParse(v, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture) with
            | true, n when abs (float n) <= maxSeed -> go rest { acc with Seed = Some n }
            | _ -> raise (UsageError $"--seed expects an integer between -%.0f{maxSeed} and %.0f{maxSeed}, got '%s{v}'")
        | opt :: [] when opt.StartsWith "-" -> raise (UsageError $"%s{opt} expects a value")
        | opt :: _ when opt.StartsWith "-" -> raise (UsageError $"unknown option '%s{opt}'")
        | input :: rest ->
            if acc.Input <> "" then
                raise (UsageError $"unexpected argument '%s{input}'")

            go rest { acc with Input = input }

    let args = go rest empty

    if args.Input = "" then
        raise (UsageError "missing input file")

    args

let private applyOverrides (args: Args) (s: Settings) : Settings =
    { Derivations = defaultArg args.Derivations s.Derivations
      Angle = defaultArg args.Angle s.Angle
      Step = defaultArg args.Step s.Step
      Width = defaultArg args.Width s.Width
      Taper = defaultArg args.Taper s.Taper
      Seed = defaultArg args.Seed s.Seed }

let private report (file: string) (diags: Diagnostic list) =
    for d in diags do
        eprintfn "%s" (Diagnostic.format file d)

let private run (args: Args) : int =
    let source =
        try
            Ok(File.ReadAllText args.Input)
        with e ->
            Error e.Message

    match source with
    | Error msg ->
        eprintfn "%s: error: %s" args.Input msg
        1
    | Ok source ->
        match Pipeline.compile source with
        | Error diags ->
            report args.Input diags
            1
        | Ok(model, warnings) ->
            report args.Input warnings

            let model =
                { model with
                    Settings = applyOverrides args model.Settings }

            match args.Command with
            | Check -> 0
            | Derive ->
                let result =
                    Derivation.derive model
                    |> Result.bind (fun derived ->
                        if args.Drawing then
                            Derivation.interpret model derived
                        else
                            Ok derived)

                match result with
                | Error diags ->
                    report args.Input diags
                    1
                | Ok s ->
                    printfn "%s" (LString.format s)
                    0
            | Render ->
                match Pipeline.run model with
                | Error diags ->
                    report args.Input diags
                    1
                | Ok output ->
                    report args.Input output.Warnings

                    let options =
                        { Svg.defaultOptions with
                            Size = defaultArg args.Size Svg.defaultOptions.Size }

                    let outPath = defaultArg args.Output (Path.ChangeExtension(args.Input, ".svg"))

                    match Svg.render options output.Geometry with
                    | Error msg ->
                        eprintfn "%s: error: %s" args.Input msg
                        1
                    | Ok svg ->
                        try
                            File.WriteAllText(outPath, svg)
                            printfn "wrote %s" outPath
                            0
                        with
                        | :? IOException
                        | :? UnauthorizedAccessException as e ->
                            eprintfn "%s: error: can't write the output: %s" outPath e.Message
                            1

[<EntryPoint>]
let main argv =
    if argv.Length = 0 || argv[0] = "-h" || argv[0] = "--help" then
        printf "%s" usage
        if argv.Length = 0 then 2 else 0
    else
        try
            run (parseArgs (List.ofArray argv))
        with UsageError msg ->
            eprintfn "rubberplant: %s\n" msg
            eprintf "%s" usage
            2
