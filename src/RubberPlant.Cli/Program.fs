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
  rubberplant render <file.rp> [-o <out>] [--format <f>] [render options] [options]
  rubberplant derive <file.rp> [--drawing] [options]
  rubberplant stats  <file.rp> [--pretty] [options]
  rubberplant check  <file.rp> [options]
  rubberplant batch

Commands:
  render   Derive, interpret and draw the L-system, as SVG or as a 3D mesh
           (OBJ, glTF).
  derive   Print the derived string (with --drawing: the string after
           interpretation rules, as the turtle sees it).
  stats    Print derivation and 3D geometry morphology statistics as compact
           single-line JSON (with --pretty: 2-space indented JSON).
  check    Parse and check the file; report errors and warnings.
  batch    Run many commands in one process, which saves the startup time of
           each: reads one command per line on stdin, as you'd type it after
           `rubberplant` (e.g. render "my tree.rp" -o tree.svg). Double quotes
           keep spaces in an argument; backslashes are plain characters. Each
           command prints what it would on its own, then `exit <code>` on stdout:
           match that whole line (a derived string can start with `exit `).
           Input and output are UTF-8. See the README for details.

Options (override the file's settings):
  -n, --derivations <n>   number of derivation steps
      --time <t>          evaluation time for timed models
      --angle <degrees>   default turn angle
      --step <d>          default step length
      --width <w>         initial line width
      --taper <percent>   width decrease applied by `!`
      --tropism <x,y,z>   tropism vector T (direction only)
      --elasticity <e>    how strongly segments bend toward T (0: off)
      --seed <n>          random seed (default 0)

Budget options (guard against runaway grammars):
      --max-modules <n|unlimited>     max L-string symbols (default 1000000;
                                      render, derive, stats)
      --max-primitives <n|unlimited>  max turtle primitives (default 250000;
                                      render, stats)

Render options:
  -o, --output <file>     output file; its extension (.svg, .obj, .gltf, .glb)
                          picks the format (default: the input file, with the
                          extension of --format)
      --format <f>        svg (default), obj, gltf or glb, when there's no -o
      --size <px>         SVG: size of the longer side of the image (default 800)
      --sides <n>         meshes: facets around each cylinder, at least 3
                          (default 12)
      --surface-div <n>   meshes: bicubic patch subdivision per axis, at least 1
                          (default: .s file PRECISION or 6)
      --width-scale <k>   meshes: world units per line width unit, or auto
                          (default) to match the SVG's proportions

OBJ output writes two files: the .obj, and its colors in a .mtl next to it.
glTF output is one file: .gltf (JSON, data embedded) or .glb (binary).
"""

type Command =
    | Render
    | Derive
    | Stats
    | Check

/// Output formats (backends.md §8).
type Format =
    | SvgFormat
    | ObjFormat
    | GltfFormat
    | GlbFormat

let private formatName =
    function
    | SvgFormat -> "svg"
    | ObjFormat -> "obj"
    | GltfFormat -> "gltf"
    | GlbFormat -> "glb"

let private formatOfName (name: string) =
    match name.ToLowerInvariant() with
    | "svg" -> Some SvgFormat
    | "obj" -> Some ObjFormat
    | "gltf" -> Some GltfFormat
    | "glb" -> Some GlbFormat
    | _ -> None

type Args =
    { Command: Command
      Input: string
      Output: string option
      Format: Format option
      Size: float option
      Sides: int option
      SurfaceDiv: int option
      /// Some None: auto.
      WidthScale: float option option
      Drawing: bool
      Pretty: bool
      Derivations: int option
      Time: float option
      Angle: float option
      Step: float option
      Width: float option
      Taper: float option
      Tropism: Geometry.Vec3 option
      Elasticity: float option
      Seed: int64 option
      /// Some None: unlimited.
      MaxModules: int option option
      /// Some None: unlimited.
      MaxPrimitives: int option option }

exception UsageError of string

let private parseFloat (opt: string) (v: string) =
    match Double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture) with
    | true, x when Double.IsFinite x -> x
    | _ -> raise (UsageError $"%s{opt} expects a number, got '%s{v}'")

let private parseLimit (opt: string) (v: string) =
    if v = "unlimited" then
        None
    else
        match Int32.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture) with
        | true, n when n >= 1 -> Some n
        | _ -> raise (UsageError $"%s{opt} expects a positive integer or 'unlimited', got '%s{v}'")

let private parseArgs (argv: string list) : Args =
    let command, rest =
        match argv with
        | "render" :: rest -> Render, rest
        | "derive" :: rest -> Derive, rest
        | "stats" :: rest -> Stats, rest
        | "check" :: rest -> Check, rest
        | "batch" :: _ -> raise (UsageError "batch takes no arguments and can't run inside a batch")
        | c :: _ -> raise (UsageError $"unknown command '%s{c}'")
        | [] -> raise (UsageError "missing command")

    let empty =
        { Command = command
          Input = ""
          Output = None
          Format = None
          Size = None
          Sides = None
          SurfaceDiv = None
          WidthScale = None
          Drawing = false
          Pretty = false
          Derivations = None
          Time = None
          Angle = None
          Step = None
          Width = None
          Taper = None
          Tropism = None
          Elasticity = None
          Seed = None
          MaxModules = None
          MaxPrimitives = None }

    let rec go args acc =
        match args with
        | [] -> acc
        | ("-o" | "--output") :: v :: rest -> go rest { acc with Output = Some v }
        | "--size" :: v :: rest ->
            let s = parseFloat "--size" v

            let minSize = 2.0 * Svg.defaultOptions.Margin

            if s <= minSize then
                raise (UsageError $"--size must be greater than %s{string minSize} (twice the margin)")

            go rest { acc with Size = Some s }
        | "--format" :: v :: rest ->
            match formatOfName v with
            | Some f -> go rest { acc with Format = Some f }
            | None -> raise (UsageError $"--format expects svg, obj, gltf or glb, got '%s{v}'")
        | "--sides" :: v :: rest ->
            match Int32.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture) with
            | true, n when n >= 3 -> go rest { acc with Sides = Some n }
            | _ -> raise (UsageError $"--sides expects an integer of at least 3, got '%s{v}'")
        | "--surface-div" :: v :: rest ->
            match Int32.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture) with
            | true, n when n >= 1 -> go rest { acc with SurfaceDiv = Some n }
            | _ -> raise (UsageError $"--surface-div expects an integer of at least 1, got '%s{v}'")
        | "--width-scale" :: v :: rest ->
            if v = "auto" then
                go rest { acc with WidthScale = Some None }
            else
                match Double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture) with
                | true, k when Double.IsFinite k && k > 0.0 -> go rest { acc with WidthScale = Some(Some k) }
                | _ -> raise (UsageError $"--width-scale expects a number greater than 0 or 'auto', got '%s{v}'")
        | "--drawing" :: rest -> go rest { acc with Drawing = true }
        | "--pretty" :: rest -> go rest { acc with Pretty = true }
        | ("-n" | "--derivations") :: v :: rest ->
            match Int32.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture) with
            | true, n -> go rest { acc with Derivations = Some n }
            | _ -> raise (UsageError $"--derivations expects a non-negative integer, got '%s{v}'")
        | "--time" :: v :: rest ->
            let t = parseFloat "--time" v

            if t < 0.0 then
                raise (UsageError "--time must be non-negative")

            go rest { acc with Time = Some t }
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
        | "--tropism" :: v :: rest ->
            // Same domain as the `tropism` setting: three finite numbers, not all zero.
            match v.Split ',' with
            | [| x; y; z |] ->
                let parse (c: string) = parseFloat "--tropism" (c.Trim())
                let t = Geometry.Vec3.create (parse x) (parse y) (parse z)

                if t = Geometry.Vec3.zero then
                    raise (UsageError "--tropism gives a direction, so it can't be the zero vector")

                go rest { acc with Tropism = Some t }
            | _ -> raise (UsageError $"--tropism expects three numbers 'x,y,z', got '%s{v}'")
        | "--elasticity" :: v :: rest -> go rest { acc with Elasticity = Some(parseFloat "--elasticity" v) }
        | "--seed" :: v :: rest ->
            // Integers a float represents exactly (§4.7).
            match Int64.TryParse(v, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture) with
            | true, n when abs (float n) <= maxSeed -> go rest { acc with Seed = Some n }
            | _ ->
                let max = maxSeed.ToString("F0", CultureInfo.InvariantCulture)
                raise (UsageError $"--seed expects an integer between -%s{max} and %s{max}, got '%s{v}'")
        | "--max-modules" :: v :: rest -> go rest { acc with MaxModules = Some(parseLimit "--max-modules" v) }
        | "--max-primitives" :: v :: rest -> go rest { acc with MaxPrimitives = Some(parseLimit "--max-primitives" v) }
        | opt :: [] when opt.StartsWith "-" -> raise (UsageError $"%s{opt} expects a value")
        | opt :: _ when opt.StartsWith "-" -> raise (UsageError $"unknown option '%s{opt}'")
        | input :: rest ->
            if acc.Input <> "" then
                raise (UsageError $"unexpected argument '%s{input}'")

            go rest { acc with Input = input }

    let args = go rest empty

    if args.Input = "" then
        raise (UsageError "missing input file")

    // Options that don't apply to the command are errors, not silently ignored (§8).
    let given =
        [ "-o", args.Output.IsSome
          "--format", args.Format.IsSome
          "--size", args.Size.IsSome
          "--sides", args.Sides.IsSome
          "--surface-div", args.SurfaceDiv.IsSome
          "--width-scale", args.WidthScale.IsSome ]

    if args.Command <> Render then
        match given |> List.tryFind snd with
        | Some(opt, _) -> raise (UsageError $"%s{opt} only applies to render")
        | None -> ()

    if args.Drawing && args.Command <> Derive then
        raise (UsageError "--drawing only applies to derive")

    if args.Pretty && args.Command <> Stats then
        raise (UsageError "--pretty only applies to stats")

    if args.MaxModules.IsSome && args.Command = Check then
        raise (UsageError "--max-modules doesn't apply to check")

    if args.MaxPrimitives.IsSome && args.Command <> Render && args.Command <> Stats then
        raise (UsageError "--max-primitives only applies to render and stats")

    if args.Derivations.IsSome && args.Time.IsSome then
        raise (UsageError "--derivations and --time cannot be used together")

    args

/// The output format: from the extension of -o, or --format, or SVG (§8).
let private outputFormat (args: Args) : Format =
    let format =
        match args.Output with
        | Some out ->
            let ext = Path.GetExtension(out).TrimStart('.')

            match formatOfName ext with
            | Some f ->
                match args.Format with
                | Some g when g <> f ->
                    raise (UsageError $"--format %s{formatName g} disagrees with the output file '%s{out}'")
                | _ -> f
            | None -> raise (UsageError $"can't tell the output format from '%s{out}': use .svg, .obj, .gltf or .glb")
        | None -> defaultArg args.Format SvgFormat

    // Options that don't apply to the format are errors too (§8).
    match format with
    | SvgFormat ->
        if args.Sides.IsSome then raise (UsageError "--sides doesn't apply to SVG output")
        if args.SurfaceDiv.IsSome then raise (UsageError "--surface-div doesn't apply to SVG output")
        if args.WidthScale.IsSome then raise (UsageError "--width-scale doesn't apply to SVG output")
    | ObjFormat
    | GltfFormat
    | GlbFormat ->
        if args.Size.IsSome then
            raise (UsageError $"--size doesn't apply to %s{(formatName format).ToUpperInvariant()} output")

    format

let private applyOverrides (args: Args) (s: Settings) : Settings =
    { s with
        Derivations = defaultArg args.Derivations s.Derivations
        Time =
            match args.Time with
            | Some t -> Some t
            | None -> s.Time
        Angle = defaultArg args.Angle s.Angle
        Step = defaultArg args.Step s.Step
        Width = defaultArg args.Width s.Width
        Taper = defaultArg args.Taper s.Taper
        Tropism = defaultArg args.Tropism s.Tropism
        Elasticity = defaultArg args.Elasticity s.Elasticity
        Seed = defaultArg args.Seed s.Seed }

let private report (file: string) (diags: Diagnostic list) =
    for d in diags do
        eprintfn "%s" (Diagnostic.format file d)

/// Writes all the files or none (§8), and prints `wrote <path>` for each.
let private writeAll (files: (string * byte[]) list) =
    match Output.writeAll Output.realFileSystem files with
    | Ok paths ->
        for path in paths do
            printfn "wrote %s" path

        true
    | Error messages ->
        for m in messages do
            eprintfn "error: can't write the output: %s" m

        false

/// UTF-8 without a byte order mark, as File.WriteAllText writes.
let private utf8 (text: string) = Text.UTF8Encoding(false).GetBytes text

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
            let timed = Model.isTimed model

            if args.Time.IsSome && not timed then
                eprintfn "%s: error: --time cannot be used with a discrete model" args.Input
                1
            elif args.Derivations.IsSome && timed then
                eprintfn "%s: error: --derivations (-n) cannot be used with a timed model" args.Input
                1
            else
                report args.Input warnings

                let baseDir = Path.GetDirectoryName args.Input

                let readSurfaceFile (relPath: string) =
                    let normalized =
                        relPath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar)

                    let fullPath =
                        if String.IsNullOrEmpty baseDir then
                            normalized
                        else
                            Path.Combine(baseDir, normalized)

                    try
                        Ok(File.ReadAllText fullPath)
                    with e ->
                        Error e.Message

                match Pipeline.loadSurfaces readSurfaceFile model with
                | Error diags ->
                    report args.Input diags
                    1
                | Ok(model, surfaceWarnings) ->
                    report args.Input surfaceWarnings

                    let model =
                        { model with
                            Settings = applyOverrides args model.Settings }

                    let limits =
                        { MaxModules = defaultArg args.MaxModules Limits.defaults.MaxModules
                          MaxPrimitives = defaultArg args.MaxPrimitives Limits.defaults.MaxPrimitives }

                    match args.Command with
                    | Check -> 0
                    | Derive ->
                        let derivedResult =
                            if timed then
                                TimedDerivation.deriveWith limits model
                            else
                                Derivation.deriveWith limits model

                        let result =
                            derivedResult
                            |> Result.bind (fun derived ->
                                if args.Drawing then
                                    Derivation.interpretWith limits model derived
                                else
                                    Ok derived)

                        match result with
                        | Error diags ->
                            report args.Input diags
                            1
                        | Ok s ->
                            printfn "%s" (LString.format s)
                            0
                    | Stats ->
                        match Pipeline.runWith limits model with
                        | Error diags ->
                            report args.Input diags
                            1
                        | Ok output ->
                            report args.Input output.Warnings

                            match
                                Stats.analyze
                                    output.Derived
                                    output.Drawing
                                    output.DerivationTrace
                                    output.Geometry
                                    output.TurtleTrace
                            with
                            | Error diags ->
                                report args.Input diags
                                1
                            | Ok stats ->
                                Console.Out.Write(Stats.toJson args.Pretty stats)
                                0
                    | Render ->
                        match Pipeline.runWith limits model with
                        | Error diags ->
                            report args.Input diags
                            1
                        | Ok output ->
                            report args.Input output.Warnings

                            let format = outputFormat args

                            let outPath =
                                defaultArg args.Output (Path.ChangeExtension(args.Input, "." + formatName format))

                            let meshOptions =
                                { Mesh.defaultMeshOptions with
                                    Sides = defaultArg args.Sides Mesh.defaultMeshOptions.Sides
                                    WidthScale = defaultArg args.WidthScale model.Settings.WidthScale
                                    SurfaceDiv = args.SurfaceDiv }

                            let files =
                                match format with
                                | SvgFormat ->
                                    let options =
                                        { Svg.defaultOptions with
                                            Size = defaultArg args.Size Svg.defaultOptions.Size }

                                    Svg.render options output.Geometry |> Result.map (fun svg -> [ outPath, utf8 svg ])
                                | ObjFormat ->
                                    let mtlPath = Path.ChangeExtension(outPath, ".mtl")

                                    Obj.render meshOptions (Path.GetFileName mtlPath) output.Geometry
                                    |> Result.map (fun o -> [ outPath, utf8 o.Obj; mtlPath, utf8 o.Mtl ])
                                | GltfFormat ->
                                    Gltf.renderText meshOptions output.Geometry
                                    |> Result.map (fun text -> [ outPath, utf8 text ])
                                | GlbFormat ->
                                    Gltf.renderBinary meshOptions output.Geometry
                                    |> Result.map (fun bytes -> [ outPath, bytes ])

                            match files with
                            | Error msg ->
                                eprintfn "%s: error: %s" args.Input msg
                                1
                            | Ok files -> if writeAll files then 0 else 1

/// Runs one command and returns its exit code. A usage error prints the message, and the
/// usage text too when `withUsage`.
let private runCommand (withUsage: bool) (argv: string list) : int =
    try
        let args = parseArgs argv

        // Format errors are usage errors: report them before doing any work.
        if args.Command = Render then
            outputFormat args |> ignore

        run args
    with UsageError msg ->
        eprintfn "rubberplant: %s" msg

        if withUsage then
            eprintf "\n%s" usage

        2

/// Splits a batch line into arguments, separated by spaces or tabs. Double quotes keep spaces in
/// an argument (`"a b.rp"`; `""` is an empty argument). Backslashes are plain characters, so
/// Windows paths need no escaping; an argument can't contain a double quote.
let splitLine (line: string) : Result<string list, string> =
    let args = ResizeArray<string>()
    let current = Text.StringBuilder()
    let mutable quoted = false
    // An argument has begun, even if empty (`""`).
    let mutable started = false

    for c in line do
        match c with
        | '"' ->
            quoted <- not quoted
            started <- true
        | ' '
        | '\t' when not quoted ->
            if started then
                args.Add(current.ToString())
                current.Clear() |> ignore
                started <- false
        | c ->
            current.Append c |> ignore
            started <- true

    // An OS argument can't contain NUL; file APIs throw on it rather than fail with an IOException.
    if line.Contains '\000' then
        Error "a batch line can't contain a NUL character"
    elif quoted then
        Error "unclosed double quote"
    else
        if started then
            args.Add(current.ToString())

        Ok(List.ofSeq args)

/// `rubberplant batch`: one command per input line (see `splitLine`). After each command, flushes
/// stderr and prints `exit <code>` on stdout. Returns 0 at the end of the input.
let batch (input: TextReader) : int =
    let mutable line = input.ReadLine()

    while not (isNull line) do
        let code =
            match splitLine line with
            | Ok args -> runCommand false args
            | Error msg ->
                eprintfn "rubberplant: %s" msg
                2

        stderr.Flush()
        printfn "exit %s" (string code)
        stdout.Flush()
        line <- input.ReadLine()

    0

[<EntryPoint>]
let main argv =
    if argv.Length = 0 || argv[0] = "-h" || argv[0] = "--help" then
        printf "%s" usage
        if argv.Length = 0 then 2 else 0
    elif argv = [| "batch" |] then
        // UTF-8 both ways, whatever the console's code page, so paths aren't mangled.
        Console.OutputEncoding <- Text.UTF8Encoding(false)
        batch (new StreamReader(Console.OpenStandardInput(), Text.UTF8Encoding(false)))
    else
        // UTF-8 on stdout, whatever the console's code page, so Unicode module names aren't mangled.
        if Console.OutputEncoding.CodePage <> Text.Encoding.UTF8.CodePage || Console.OutputEncoding.GetPreamble().Length <> 0 then
            Console.OutputEncoding <- Text.UTF8Encoding(false)

        runCommand true (List.ofArray argv)
