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
  rubberplant check  <file.rp> [options]

Commands:
  render   Derive, interpret and draw the L-system, as SVG or as a 3D mesh (OBJ).
  derive   Print the derived string (with --drawing: the string after
           interpretation rules, as the turtle sees it).
  check    Parse and check the file; report errors and warnings.

Options (override the file's settings):
  -n, --derivations <n>   number of derivation steps
      --angle <degrees>   default turn angle
      --step <d>          default step length
      --width <w>         initial line width
      --taper <percent>   width decrease applied by `!`
      --tropism <x,y,z>   tropism vector T (direction only)
      --elasticity <e>    how strongly segments bend toward T (0: off)
      --seed <n>          random seed (default 0)

Render options:
  -o, --output <file>     output file; its extension (.svg, .obj) picks the format
                          (default: the input file, with the extension of --format)
      --format <f>        svg (default) or obj, when there's no -o
      --size <px>         SVG: size of the longer side of the image (default 800)
      --sides <n>         OBJ: facets around each cylinder, at least 3 (default 12)
      --width-scale <k>   OBJ: world units per line width unit, or auto (default)
                          to match the SVG's proportions

OBJ output writes two files: the .obj, and its colors in a .mtl next to it.
"""

type Command =
    | Render
    | Derive
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
      /// Some None: auto.
      WidthScale: float option option
      Drawing: bool
      Derivations: int option
      Angle: float option
      Step: float option
      Width: float option
      Taper: float option
      Tropism: Geometry.Vec3 option
      Elasticity: float option
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
          Format = None
          Size = None
          Sides = None
          WidthScale = None
          Drawing = false
          Derivations = None
          Angle = None
          Step = None
          Width = None
          Taper = None
          Tropism = None
          Elasticity = None
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
        | "--format" :: v :: rest ->
            match formatOfName v with
            | Some f -> go rest { acc with Format = Some f }
            | None -> raise (UsageError $"--format expects svg or obj, got '%s{v}'")
        | "--sides" :: v :: rest ->
            match Int32.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture) with
            | true, n when n >= 3 -> go rest { acc with Sides = Some n }
            | _ -> raise (UsageError $"--sides expects an integer of at least 3, got '%s{v}'")
        | "--width-scale" :: v :: rest ->
            if v = "auto" then
                go rest { acc with WidthScale = Some None }
            else
                match Double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture) with
                | true, k when Double.IsFinite k && k > 0.0 -> go rest { acc with WidthScale = Some(Some k) }
                | _ -> raise (UsageError $"--width-scale expects a number greater than 0 or 'auto', got '%s{v}'")
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

    // Options that don't apply to the command are errors, not silently ignored (§8).
    let given =
        [ "-o", args.Output.IsSome
          "--format", args.Format.IsSome
          "--size", args.Size.IsSome
          "--sides", args.Sides.IsSome
          "--width-scale", args.WidthScale.IsSome ]

    if args.Command <> Render then
        match given |> List.tryFind snd with
        | Some(opt, _) -> raise (UsageError $"%s{opt} only applies to render")
        | None -> ()

    if args.Drawing && args.Command <> Derive then
        raise (UsageError "--drawing only applies to derive")

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
            | None -> raise (UsageError $"can't tell the output format from '%s{out}': use .svg or .obj")
        | None -> defaultArg args.Format SvgFormat

    // Options that don't apply to the format are errors too (§8).
    match format with
    | SvgFormat ->
        if args.Sides.IsSome then raise (UsageError "--sides doesn't apply to SVG output")
        if args.WidthScale.IsSome then raise (UsageError "--width-scale doesn't apply to SVG output")
    | ObjFormat
    | GltfFormat
    | GlbFormat ->
        if args.Size.IsSome then
            raise (UsageError $"--size doesn't apply to %s{(formatName format).ToUpperInvariant()} output")

    match format with
    | GltfFormat
    | GlbFormat -> raise (UsageError "glTF output isn't available yet; use .svg or .obj")
    | f -> f

let private applyOverrides (args: Args) (s: Settings) : Settings =
    { s with
        Derivations = defaultArg args.Derivations s.Derivations
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

/// Writes files, printing `wrote <path>` for each; false (with an error printed)
/// if one can't be written.
let private writeAll (files: (string * string) list) =
    files
    |> List.forall (fun (path, text) ->
        try
            File.WriteAllText(path, text)
            printfn "wrote %s" path
            true
        with
        | :? IOException
        | :? UnauthorizedAccessException as e ->
            eprintfn "%s: error: can't write the output: %s" path e.Message
            false)

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

                    let format = outputFormat args

                    let outPath =
                        defaultArg args.Output (Path.ChangeExtension(args.Input, "." + formatName format))

                    let meshOptions =
                        { Mesh.defaultMeshOptions with
                            Sides = defaultArg args.Sides Mesh.defaultMeshOptions.Sides
                            WidthScale = defaultArg args.WidthScale Mesh.defaultMeshOptions.WidthScale }

                    let files =
                        match format with
                        | SvgFormat ->
                            let options =
                                { Svg.defaultOptions with
                                    Size = defaultArg args.Size Svg.defaultOptions.Size }

                            Svg.render options output.Geometry |> Result.map (fun svg -> [ outPath, svg ])
                        | ObjFormat
                        | GltfFormat
                        | GlbFormat ->
                            let mtlPath = Path.ChangeExtension(outPath, ".mtl")

                            Obj.render meshOptions (Path.GetFileName mtlPath) output.Geometry
                            |> Result.map (fun o -> [ outPath, o.Obj; mtlPath, o.Mtl ])

                    match files with
                    | Error msg ->
                        eprintfn "%s: error: %s" args.Input msg
                        1
                    | Ok files -> if writeAll files then 0 else 1

[<EntryPoint>]
let main argv =
    if argv.Length = 0 || argv[0] = "-h" || argv[0] = "--help" then
        printf "%s" usage
        if argv.Length = 0 then 2 else 0
    else
        try
            let args = parseArgs (List.ofArray argv)

            // Format errors are usage errors: report them before doing any work.
            if args.Command = Render then
                outputFormat args |> ignore

            run args
        with UsageError msg ->
            eprintfn "rubberplant: %s\n" msg
            eprintf "%s" usage
            2
