/// Pure CLI parsing, batch/interactive line protocol, option validation, and
/// transactional session state machine (docs/viewer.md §6).
module RubberPlant.Viewer.Protocol

open System
open System.Globalization
open RubberPlant
open RubberPlant.Model
open RubberPlant.Backends

let maxImageDimension = 8192
let minContactSheetSize = 128
let defaultContactSheetSize = 1024
let defaultSnapshotSize = (512, 512)
let defaultWindowSize = (1024, 768)

let usage =
    """Usage:
  rubberplant-viewer view [file.rp] [viewer options] [load options]
  rubberplant-viewer <file.rp> [viewer options] [load options]
  rubberplant-viewer contact-sheet <file.rp> -o <out.png> [options]
  rubberplant-viewer snapshot      <file.rp> -o <out.png> [options]
  rubberplant-viewer silhouette    <file.rp> -o <out.png> [options]
  rubberplant-viewer batch

Commands:
  view           Open an interactive 3D inspection window (with live stdin commands).
  contact-sheet  Render a 2x2 shared-scale PNG (Front, Side +90, 3/4 Iso, Top).
  snapshot       Render a single shaded PNG.
  silhouette     Render a 1x non-MSAA binary 8-bit grayscale mask PNG.
  batch          Stateful headless protocol over stdin/stdout (`exit <code>` framing).

Load options (override the file's settings; parity with `rubberplant`):
  -n, --derivations <n>           number of derivation steps
      --angle <degrees>           default turn angle
      --step <d>                  default step length
      --width <w>                 initial line width
      --taper <percent>           width decrease applied by `!`
      --tropism <x,y,z>           tropism vector T (direction only)
      --elasticity <e>            how strongly segments bend toward T (0: off)
      --seed <n>                  random seed (default 0)
      --sides <n>                 facets around each cylinder, at least 3 (default 12)
      --width-scale <k|auto>      world units per line width unit, or auto (default)
      --max-modules <n|unlimited>     max L-string symbols (default 1000000)
      --max-primitives <n|unlimited>  max turtle primitives (default 250000)

Viewer & render options:
  -o, --output <out.png>          output PNG path (required on one-shot headless commands)
      --size <px>                 contact-sheet: even integer in [128, 8192] (default 1024)
      --size <w>[x<h>]            snapshot/silhouette: [1, 8192] (default 512x512);
                                  view: initial window size (default 1024x768)
      --camera <az,el[,dist|fit]> camera orbit angles and optional distance or fit
      --wireframe                 draw wireframe overlay (view, one-shot contact-sheet/snapshot)
      --no-grid                   hide ground grid (view, one-shot contact-sheet/snapshot)
      --stats                     write Stats JSON to stdout instead of `wrote <path>`
      --stats-out <file.json>     write Stats JSON atomically to file
      --pretty                    2-space indented JSON (with --stats, --stats-out, or batch stats)
"""

/// Load overrides shared by `view <file.rp>`, one-shot renders, and batch `load` (§6.2).
type LoadOptions =
    { Derivations: int option
      Angle: float option
      Step: float option
      Width: float option
      Taper: float option
      Tropism: Geometry.Vec3 option
      Elasticity: float option
      Seed: int64 option
      Sides: int option
      /// Some None: auto; Some (Some k): explicit scale.
      WidthScale: float option option
      /// Some None: unlimited.
      MaxModules: int option option
      /// Some None: unlimited.
      MaxPrimitives: int option option }

let emptyLoadOptions =
    { Derivations = None
      Angle = None
      Step = None
      Width = None
      Taper = None
      Tropism = None
      Elasticity = None
      Seed = None
      Sides = None
      WidthScale = None
      MaxModules = None
      MaxPrimitives = None }

let hasAnyLoadOption (o: LoadOptions) : bool =
    o.Derivations.IsSome
    || o.Angle.IsSome
    || o.Step.IsSome
    || o.Width.IsSome
    || o.Taper.IsSome
    || o.Tropism.IsSome
    || o.Elasticity.IsSome
    || o.Seed.IsSome
    || o.Sides.IsSome
    || o.WidthScale.IsSome
    || o.MaxModules.IsSome
    || o.MaxPrimitives.IsSome

let applySettingsOverrides (o: LoadOptions) (s: Settings) : Settings =
    { s with
        Derivations = defaultArg o.Derivations s.Derivations
        Angle = defaultArg o.Angle s.Angle
        Step = defaultArg o.Step s.Step
        Width = defaultArg o.Width s.Width
        Taper = defaultArg o.Taper s.Taper
        Tropism = defaultArg o.Tropism s.Tropism
        Elasticity = defaultArg o.Elasticity s.Elasticity
        Seed = defaultArg o.Seed s.Seed }

let toMeshOptions (o: LoadOptions) : Mesh.MeshOptions =
    { Mesh.defaultMeshOptions with
        Sides = defaultArg o.Sides Mesh.defaultMeshOptions.Sides
        WidthScale = defaultArg o.WidthScale Mesh.defaultMeshOptions.WidthScale }

let toLimits (o: LoadOptions) : Limits =
    { MaxModules = defaultArg o.MaxModules Limits.defaults.MaxModules
      MaxPrimitives = defaultArg o.MaxPrimitives Limits.defaults.MaxPrimitives }

type CameraDist =
    | KeepOrAutoFit
    | AutoFit
    | Fixed of float

type CameraSpec =
    { AzimuthDeg: float
      ElevationDeg: float
      Distance: CameraDist }

type StatsOutput =
    { PrintStdout: bool
      OutPath: string option
      Pretty: bool }

type HeadlessRenderSpec =
    { Input: string
      Load: LoadOptions
      Output: string
      Stats: StatsOutput }

type ViewArgs =
    { Input: string option
      Load: LoadOptions
      WindowSize: int * int
      Camera: CameraSpec option
      Wireframe: bool
      Grid: bool }

type ContactSheetArgs =
    { Base: HeadlessRenderSpec
      Size: int
      Wireframe: bool
      Grid: bool }

type SnapshotArgs =
    { Base: HeadlessRenderSpec
      Size: int * int
      Camera: CameraSpec option
      Wireframe: bool
      Grid: bool }

type SilhouetteArgs =
    { Base: HeadlessRenderSpec
      Size: int * int
      Camera: CameraSpec option }

type CliCommand =
    | Help of exitCode: int
    | View of ViewArgs
    | ContactSheet of ContactSheetArgs
    | Snapshot of SnapshotArgs
    | Silhouette of SilhouetteArgs
    | Batch

type BatchSnapshotArgs =
    { Output: string
      Camera: CameraSpec option
      Size: (int * int) option
      Stats: StatsOutput }

type BatchContactSheetArgs =
    { Output: string
      Size: int option
      Stats: StatsOutput }

type BatchSilhouetteArgs =
    { Output: string
      Camera: CameraSpec option
      Size: (int * int) option
      Stats: StatsOutput }

type BatchCommand =
    | Load of input: string * load: LoadOptions * keepCamera: bool
    | Reload of keepCamera: bool
    | SetCamera of CameraSpec
    | Fit
    | SetSize of width: int * height: int
    | SetWireframe of enabled: bool
    | SetGrid of enabled: bool
    | BatchSnapshot of BatchSnapshotArgs
    | BatchContactSheet of BatchContactSheetArgs
    | BatchSilhouette of BatchSilhouetteArgs
    | BatchStats of pretty: bool
    | Quit

// ---------------------------------------------------------------- line splitter

/// Splits a batch/interactive stdin line into arguments with exact parity to
/// `RubberPlant.Cli.Program.splitLine` (§6.3).
let splitLine (line: string) : Result<string list, string> =
    let args = ResizeArray<string>()
    let current = Text.StringBuilder()
    let mutable quoted = false
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

    if line.Contains '\000' then
        Error "a batch line can't contain a NUL character"
    elif quoted then
        Error "unclosed double quote"
    else
        if started then
            args.Add(current.ToString())

        Ok(List.ofSeq args)

// -------------------------------------------------------------- value parsers

exception private ParseError of string

let private fail msg = raise (ParseError msg)

let private parseFloat (opt: string) (v: string) : float =
    match Double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture) with
    | true, x when Double.IsFinite x -> x
    | _ -> fail $"%s{opt} expects a number, got '%s{v}'"

let private parsePositiveIntBounded (opt: string) (minVal: int) (maxVal: int) (v: string) : int =
    match Int32.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture) with
    | true, n when n >= minVal && n <= maxVal -> n
    | _ -> fail $"%s{opt} expects an integer between %s{string minVal} and %s{string maxVal}, got '%s{v}'"

let private parseLimit (opt: string) (v: string) : int option =
    if v = "unlimited" then
        None
    else
        match Int32.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture) with
        | true, n when n >= 1 -> Some n
        | _ -> fail $"%s{opt} expects a positive integer or 'unlimited', got '%s{v}'"

let private parseSize (opt: string) (v: string) : int * int =
    match v.Split 'x' with
    | [| s |] ->
        let n = parsePositiveIntBounded opt 1 maxImageDimension s
        n, n
    | [| ws; hs |] ->
        let w = parsePositiveIntBounded opt 1 maxImageDimension ws
        let h = parsePositiveIntBounded opt 1 maxImageDimension hs
        w, h
    | _ -> fail $"%s{opt} expects <w> or <w>x<h> between 1 and %s{string maxImageDimension}, got '%s{v}'"

let private parseContactSheetSize (opt: string) (v: string) : int =
    match Int32.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture) with
    | true, n when n >= minContactSheetSize && n <= maxImageDimension && n % 2 = 0 -> n
    | _ ->
        fail
            $"%s{opt} expects an even integer between %s{string minContactSheetSize} and %s{string maxImageDimension}, got '%s{v}'"

let private parseCameraParts (opt: string) (azStr: string) (elStr: string) (distOpt: string option) : CameraSpec =
    let az = parseFloat opt (azStr.Trim())
    let el = parseFloat opt (elStr.Trim())

    if el < -90.0 || el > 90.0 then
        fail $"%s{opt} elevation must be between -90 and 90, got '%s{elStr.Trim()}'"

    let dist =
        match distOpt with
        | None -> KeepOrAutoFit
        | Some raw ->
            let trimmed = raw.Trim()

            if trimmed = "fit" then
                AutoFit
            else
                let d = parseFloat opt trimmed

                if d <= 0.0 then
                    fail $"%s{opt} distance must be greater than 0, got '%s{trimmed}'"

                Fixed d

    { AzimuthDeg = az
      ElevationDeg = el
      Distance = dist }

let private parseCameraFlag (v: string) : CameraSpec =
    match v.Split ',' with
    | [| az; el |] -> parseCameraParts "--camera" az el None
    | [| az; el; dist |] -> parseCameraParts "--camera" az el (Some dist)
    | _ -> fail $"--camera expects 'az,el' or 'az,el,dist|fit', got '%s{v}'"

let private tryParseLoadOption (args: string list) (o: LoadOptions) : (LoadOptions * string list) option =
    match args with
    | ("-n" | "--derivations") :: v :: rest ->
        match Int32.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture) with
        | true, n -> Some({ o with Derivations = Some n }, rest)
        | _ -> fail $"--derivations expects a non-negative integer, got '%s{v}'"
    | "--angle" :: v :: rest -> Some({ o with Angle = Some(parseFloat "--angle" v) }, rest)
    | "--step" :: v :: rest -> Some({ o with Step = Some(parseFloat "--step" v) }, rest)
    | "--width" :: v :: rest ->
        let w = parseFloat "--width" v

        if w < 0.0 then
            fail "--width must be non-negative"

        Some({ o with Width = Some w }, rest)
    | "--taper" :: v :: rest ->
        let t = parseFloat "--taper" v

        if t < 0.0 || t > 100.0 then
            fail "--taper must be between 0 and 100"

        Some({ o with Taper = Some t }, rest)
    | "--tropism" :: v :: rest ->
        match v.Split ',' with
        | [| x; y; z |] ->
            let parse (c: string) = parseFloat "--tropism" (c.Trim())
            let t = Geometry.Vec3.create (parse x) (parse y) (parse z)

            if t = Geometry.Vec3.zero then
                fail "--tropism gives a direction, so it can't be the zero vector"

            Some({ o with Tropism = Some t }, rest)
        | _ -> fail $"--tropism expects three numbers 'x,y,z', got '%s{v}'"
    | "--elasticity" :: v :: rest -> Some({ o with Elasticity = Some(parseFloat "--elasticity" v) }, rest)
    | "--seed" :: v :: rest ->
        match Int64.TryParse(v, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture) with
        | true, n when abs (float n) <= maxSeed -> Some({ o with Seed = Some n }, rest)
        | _ ->
            let maxStr = maxSeed.ToString("F0", CultureInfo.InvariantCulture)
            fail $"--seed expects an integer between -%s{maxStr} and %s{maxStr}, got '%s{v}'"
    | "--sides" :: v :: rest ->
        match Int32.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture) with
        | true, n when n >= 3 -> Some({ o with Sides = Some n }, rest)
        | _ -> fail $"--sides expects an integer of at least 3, got '%s{v}'"
    | "--width-scale" :: v :: rest ->
        if v = "auto" then
            Some({ o with WidthScale = Some None }, rest)
        else
            match Double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture) with
            | true, k when Double.IsFinite k && k > 0.0 -> Some({ o with WidthScale = Some(Some k) }, rest)
            | _ -> fail $"--width-scale expects a number greater than 0 or 'auto', got '%s{v}'"
    | "--max-modules" :: v :: rest -> Some({ o with MaxModules = Some(parseLimit "--max-modules" v) }, rest)
    | "--max-primitives" :: v :: rest -> Some({ o with MaxPrimitives = Some(parseLimit "--max-primitives" v) }, rest)
    | ("-n"
    | "--derivations"
    | "--angle"
    | "--step"
    | "--width"
    | "--taper"
    | "--tropism"
    | "--elasticity"
    | "--seed"
    | "--sides"
    | "--width-scale"
    | "--max-modules"
    | "--max-primitives") as opt :: [] -> fail $"%s{opt} expects a value"
    | _ -> None

let private validateNonEmptyPath (label: string) (path: string) =
    if String.IsNullOrEmpty path then
        fail $"%s{label} can't be empty"

let private validateStatsOutput (stats: StatsOutput) =
    if stats.Pretty && not stats.PrintStdout && stats.OutPath.IsNone then
        fail "--pretty requires --stats or --stats-out"

// ------------------------------------------------------------------ CLI parser

let private parseViewArgs (rest: string list) : ViewArgs =
    let rec go args input load size cam wireframe grid =
        match args with
        | [] ->
            if input = None && hasAnyLoadOption load then
                fail "load options require an input file"

            { Input = input
              Load = load
              WindowSize = defaultArg size defaultWindowSize
              Camera = cam
              Wireframe = wireframe
              Grid = grid }
        | "--size" :: v :: tail -> go tail input load (Some(parseSize "--size" v)) cam wireframe grid
        | "--camera" :: v :: tail -> go tail input load size (Some(parseCameraFlag v)) wireframe grid
        | "--wireframe" :: tail -> go tail input load size cam true grid
        | "--no-grid" :: tail -> go tail input load size cam wireframe false
        | ("--size" | "--camera") as opt :: [] -> fail $"%s{opt} expects a value"
        | _ ->
            match tryParseLoadOption args load with
            | Some(load', tail) -> go tail input load' size cam wireframe grid
            | None ->
                match args with
                | opt :: _ when opt.StartsWith "-" -> fail $"unknown option '%s{opt}'"
                | file :: tail ->
                    validateNonEmptyPath "input file" file

                    if input.IsSome then
                        fail $"unexpected argument '%s{file}'"

                    go tail (Some file) load size cam wireframe grid
                | [] -> fail "unexpected end of arguments"

    go rest None emptyLoadOptions None None false true

type private HeadlessKind =
    | KindContactSheet
    | KindSnapshot
    | KindSilhouette

let private parseHeadlessCli (kind: HeadlessKind) (rest: string list) : CliCommand =
    let rec go args input output load sheetSize snapSize (cam: CameraSpec option) wireframe noGrid statsStdout statsOut pretty =
        match args with
        | [] ->
            let inputPath =
                match input with
                | Some p -> p
                | None -> fail "missing input file"

            let outPath =
                match output with
                | Some p -> p
                | None -> fail "missing required option -o / --output"

            let stats =
                { PrintStdout = statsStdout
                  OutPath = statsOut
                  Pretty = pretty }

            validateStatsOutput stats

            let baseSpec =
                { Input = inputPath
                  Load = load
                  Output = outPath
                  Stats = stats }

            match kind with
            | KindContactSheet ->
                ContactSheet
                    { Base = baseSpec
                      Size = defaultArg sheetSize defaultContactSheetSize
                      Wireframe = wireframe
                      Grid = not noGrid }
            | KindSnapshot ->
                Snapshot
                    { Base = baseSpec
                      Size = defaultArg snapSize defaultSnapshotSize
                      Camera = cam
                      Wireframe = wireframe
                      Grid = not noGrid }
            | KindSilhouette ->
                Silhouette
                    { Base = baseSpec
                      Size = defaultArg snapSize defaultSnapshotSize
                      Camera = cam }
        | ("-o" | "--output") :: v :: tail ->
            validateNonEmptyPath "--output" v
            go tail input (Some v) load sheetSize snapSize cam wireframe noGrid statsStdout statsOut pretty
        | "--size" :: v :: tail ->
            match kind with
            | KindContactSheet ->
                let sz = parseContactSheetSize "--size" v
                go tail input output load (Some sz) snapSize cam wireframe noGrid statsStdout statsOut pretty
            | KindSnapshot
            | KindSilhouette ->
                let sz = parseSize "--size" v
                go tail input output load sheetSize (Some sz) cam wireframe noGrid statsStdout statsOut pretty
        | "--camera" :: _ when kind = KindContactSheet ->
            fail "--camera doesn't apply to contact-sheet"
        | "--camera" :: v :: tail ->
            go tail input output load sheetSize snapSize (Some(parseCameraFlag v)) wireframe noGrid statsStdout statsOut pretty
        | "--wireframe" :: tail ->
            if kind = KindSilhouette then
                fail "--wireframe doesn't apply to silhouette"

            go tail input output load sheetSize snapSize cam true noGrid statsStdout statsOut pretty
        | "--no-grid" :: tail ->
            if kind = KindSilhouette then
                fail "--no-grid doesn't apply to silhouette"

            go tail input output load sheetSize snapSize cam wireframe true statsStdout statsOut pretty
        | "--stats" :: tail -> go tail input output load sheetSize snapSize cam wireframe noGrid true statsOut pretty
        | "--stats-out" :: v :: tail ->
            validateNonEmptyPath "--stats-out" v
            go tail input output load sheetSize snapSize cam wireframe noGrid statsStdout (Some v) pretty
        | "--pretty" :: tail -> go tail input output load sheetSize snapSize cam wireframe noGrid statsStdout statsOut true
        | ("-o" | "--output" | "--size" | "--camera" | "--stats-out") as opt :: [] -> fail $"%s{opt} expects a value"
        | _ ->
            match tryParseLoadOption args load with
            | Some(load', tail) ->
                go tail input output load' sheetSize snapSize cam wireframe noGrid statsStdout statsOut pretty
            | None ->
                match args with
                | opt :: _ when opt.StartsWith "-" -> fail $"unknown option '%s{opt}'"
                | file :: tail ->
                    validateNonEmptyPath "input file" file

                    if input.IsSome then
                        fail $"unexpected argument '%s{file}'"

                    go tail (Some file) output load sheetSize snapSize cam wireframe noGrid statsStdout statsOut pretty
                | [] -> fail "unexpected end of arguments"

    go rest None None emptyLoadOptions None None None false false false None false

/// Parses top-level CLI arguments (`rubberplant-viewer ...`) (§6.1–§6.2).
let parseCli (argv: string list) : Result<CliCommand, string> =
    try
        match argv with
        | [] -> Ok(Help 2)
        | "-h" :: _
        | "--help" :: _ -> Ok(Help 0)
        | "batch" :: [] -> Ok Batch
        | "batch" :: _ -> fail "batch takes no arguments"
        | "view" :: rest -> Ok(View(parseViewArgs rest))
        | "contact-sheet" :: rest -> Ok(parseHeadlessCli KindContactSheet rest)
        | "snapshot" :: rest -> Ok(parseHeadlessCli KindSnapshot rest)
        | "silhouette" :: rest -> Ok(parseHeadlessCli KindSilhouette rest)
        | first :: _ when first.EndsWith(".rp", StringComparison.OrdinalIgnoreCase) -> Ok(View(parseViewArgs argv))
        | c :: _ -> fail $"unknown command '%s{c}'"
    with ParseError msg ->
        Error msg

// --------------------------------------------------------------- batch parser

let private parseBatchLoad (rest: string list) : BatchCommand =
    let rec go args input load keepCam =
        match args with
        | [] ->
            match input with
            | Some file -> Load(file, load, keepCam)
            | None -> fail "missing input file"
        | "--keep-camera" :: tail -> go tail input load true
        | _ ->
            match tryParseLoadOption args load with
            | Some(load', tail) -> go tail input load' keepCam
            | None ->
                match args with
                | opt :: _ when opt.StartsWith "-" -> fail $"unknown option '%s{opt}'"
                | file :: tail ->
                    validateNonEmptyPath "input file" file

                    if input.IsSome then
                        fail $"unexpected argument '%s{file}'"

                    go tail (Some file) load keepCam
                | [] -> fail "unexpected end of arguments"

    go rest None emptyLoadOptions false

let private parseBatchReload (rest: string list) : BatchCommand =
    match rest with
    | [] -> Reload false
    | [ "--keep-camera" ] -> Reload true
    | "--keep-camera" :: arg :: _ -> fail $"unexpected argument '%s{arg}'"
    | opt :: _ when opt.StartsWith "-" -> fail $"unknown option '%s{opt}'"
    | arg :: _ -> fail $"unexpected argument '%s{arg}'"

let private parseBatchCamera (rest: string list) : BatchCommand =
    match rest with
    | [ az; el ] -> SetCamera(parseCameraParts "camera" az el None)
    | [ az; el; dist ] -> SetCamera(parseCameraParts "camera" az el (Some dist))
    | _ -> fail "camera expects '<az> <el> [dist|fit]'"

let private parseBatchSize (rest: string list) : BatchCommand =
    match rest with
    | [ wStr ] ->
        let w = parsePositiveIntBounded "size" 1 maxImageDimension wStr
        SetSize(w, w)
    | [ wStr; hStr ] ->
        let w = parsePositiveIntBounded "size" 1 maxImageDimension wStr
        let h = parsePositiveIntBounded "size" 1 maxImageDimension hStr
        SetSize(w, h)
    | _ -> fail "size expects '<w> [h]'"

let private parseToggle (cmd: string) (rest: string list) : bool =
    match rest with
    | [ "on" ] -> true
    | [ "off" ] -> false
    | _ -> fail $"%s{cmd} expects 'on' or 'off'"

let private parseBatchRender (kind: HeadlessKind) (rest: string list) : BatchCommand =
    let rec go args output cam sheetSize snapSize statsStdout statsOut pretty =
        match args with
        | [] ->
            let outPath =
                match output with
                | Some p -> p
                | None -> fail "missing output file"

            let stats =
                { PrintStdout = statsStdout
                  OutPath = statsOut
                  Pretty = pretty }

            validateStatsOutput stats

            match kind with
            | KindContactSheet ->
                BatchContactSheet
                    { Output = outPath
                      Size = sheetSize
                      Stats = stats }
            | KindSnapshot ->
                BatchSnapshot
                    { Output = outPath
                      Camera = cam
                      Size = snapSize
                      Stats = stats }
            | KindSilhouette ->
                BatchSilhouette
                    { Output = outPath
                      Camera = cam
                      Size = snapSize
                      Stats = stats }
        | "--wireframe" :: _ -> fail "--wireframe is not allowed here; use `wireframe on|off`"
        | "--no-grid" :: _ -> fail "--no-grid is not allowed here; use `grid on|off`"
        | "--camera" :: _ when kind = KindContactSheet ->
            fail "--camera doesn't apply to contact-sheet"
        | "--camera" :: v :: tail ->
            go tail output (Some(parseCameraFlag v)) sheetSize snapSize statsStdout statsOut pretty
        | "--size" :: v :: tail ->
            match kind with
            | KindContactSheet ->
                let sz = parseContactSheetSize "--size" v
                go tail output cam (Some sz) snapSize statsStdout statsOut pretty
            | KindSnapshot
            | KindSilhouette ->
                let sz = parseSize "--size" v
                go tail output cam sheetSize (Some sz) statsStdout statsOut pretty
        | "--stats" :: tail -> go tail output cam sheetSize snapSize true statsOut pretty
        | "--stats-out" :: v :: tail ->
            validateNonEmptyPath "--stats-out" v
            go tail output cam sheetSize snapSize statsStdout (Some v) pretty
        | "--pretty" :: tail -> go tail output cam sheetSize snapSize statsStdout statsOut true
        | ("--camera" | "--size" | "--stats-out") as opt :: [] -> fail $"%s{opt} expects a value"
        | opt :: _ when opt.StartsWith "-" -> fail $"unknown option '%s{opt}'"
        | file :: tail ->
            validateNonEmptyPath "output file" file

            if output.IsSome then
                fail $"unexpected argument '%s{file}'"

            go tail (Some file) cam sheetSize snapSize statsStdout statsOut pretty

    go rest None None None None false None false

let private parseBatchStats (rest: string list) : BatchCommand =
    match rest with
    | [] -> BatchStats false
    | [ "--pretty" ] -> BatchStats true
    | "--pretty" :: arg :: _ -> fail $"unexpected argument '%s{arg}'"
    | opt :: _ when opt.StartsWith "-" -> fail $"unknown option '%s{opt}'"
    | arg :: _ -> fail $"unexpected argument '%s{arg}'"

/// Parses a single split command from `batch` or interactive `stdin` (§6.3).
let parseBatchCommand (args: string list) : Result<BatchCommand, string> =
    try
        match args with
        | [] -> fail "missing command"
        | "batch" :: _ -> fail "batch can't run inside batch or interactive stdin"
        | "view" :: _ -> fail "view can't run inside batch or interactive stdin"
        | "load" :: rest -> Ok(parseBatchLoad rest)
        | "reload" :: rest -> Ok(parseBatchReload rest)
        | "camera" :: rest -> Ok(parseBatchCamera rest)
        | "fit" :: [] -> Ok Fit
        | "fit" :: _ -> fail "fit takes no arguments"
        | "size" :: rest -> Ok(parseBatchSize rest)
        | "wireframe" :: rest -> Ok(SetWireframe(parseToggle "wireframe" rest))
        | "grid" :: rest -> Ok(SetGrid(parseToggle "grid" rest))
        | "snapshot" :: rest -> Ok(parseBatchRender KindSnapshot rest)
        | "contact-sheet" :: rest -> Ok(parseBatchRender KindContactSheet rest)
        | "silhouette" :: rest -> Ok(parseBatchRender KindSilhouette rest)
        | "stats" :: rest -> Ok(parseBatchStats rest)
        | "quit" :: [] -> Ok Quit
        | "quit" :: _ -> fail "quit takes no arguments"
        | c :: _ -> fail $"unknown command '%s{c}'"
    with ParseError msg ->
        Error msg

/// Splits and parses a raw `batch` / interactive `stdin` line (§6.3).
let parseBatchLine (line: string) : Result<BatchCommand, string> =
    splitLine line |> Result.bind parseBatchCommand

// --------------------------------------------------- pure session state machine

/// Cached loaded model metadata and scene payload for a viewer session (§6.3).
type LoadedModel<'scene> =
    { Input: string
      Load: LoadOptions
      Bounds: Camera.Bounds
      Stats: Stats.Stats
      Scene: 'scene }

/// Pure stateful session shared by `batch` and interactive `view` modes (§6.3).
type SessionState<'scene> =
    { Loaded: LoadedModel<'scene> option
      Camera: Camera.Camera option
      /// Camera angles/distance configured before the first `load` (§6.3).
      PendingCamera: CameraSpec option
      DefaultSize: int * int
      Wireframe: bool
      Grid: bool }

let initialSession<'scene> : SessionState<'scene> =
    { Loaded = None
      Camera = None
      PendingCamera = None
      DefaultSize = defaultSnapshotSize
      Wireframe = false
      Grid = true }

let sessionAspect (state: SessionState<'scene>) : float =
    let w, h = state.DefaultSize
    float w / float h

/// Resolves a camera for one-shot CLI commands (`snapshot`, `silhouette`, `view`) (§6.2).
/// Omitting `dist` auto-fits for `(az, el)`; omitting `--camera` auto-fits at 3/4 ISO.
let resolveOneShotCamera (bounds: Camera.Bounds) (spec: CameraSpec option) (aspect: float) : Camera.Camera =
    match spec with
    | None -> Camera.fit bounds Camera.defaultAzimuthDeg Camera.defaultElevationDeg aspect
    | Some s ->
        match s.Distance with
        | Fixed d ->
            { Target = Geometry.Vec3.zero
              AzimuthDeg = s.AzimuthDeg
              ElevationDeg = s.ElevationDeg
              Distance = d }
        | KeepOrAutoFit
        | AutoFit -> Camera.fit bounds s.AzimuthDeg s.ElevationDeg aspect

/// Resolves a camera for a non-mutating `batch`/interactive render command (§6.2–§6.3).
let resolveRenderCamera
    (state: SessionState<'scene>)
    (bounds: Camera.Bounds)
    (overrideSpec: CameraSpec option)
    (aspect: float)
    : Camera.Camera =
    match overrideSpec with
    | None ->
        match state.Camera with
        | Some cam -> cam
        | None -> Camera.fit bounds Camera.defaultAzimuthDeg Camera.defaultElevationDeg aspect
    | Some s ->
        match s.Distance with
        | AutoFit -> Camera.fit bounds s.AzimuthDeg s.ElevationDeg aspect
        | Fixed d ->
            let target =
                match state.Camera with
                | Some cam -> cam.Target
                | None -> Geometry.Vec3.zero

            { Target = target
              AzimuthDeg = s.AzimuthDeg
              ElevationDeg = s.ElevationDeg
              Distance = d }
        | KeepOrAutoFit ->
            match state.Camera with
            | Some cam ->
                { cam with
                    AzimuthDeg = s.AzimuthDeg
                    ElevationDeg = s.ElevationDeg }
            | None -> Camera.fit bounds s.AzimuthDeg s.ElevationDeg aspect

let requireLoaded (state: SessionState<'scene>) : Result<LoadedModel<'scene>, string> =
    match state.Loaded with
    | Some loaded -> Ok loaded
    | None -> Error "no model loaded"

/// Applies a successfully loaded model to session state (§6.3).
let applyLoad
    (aspect: float)
    (keepCamera: bool)
    (loaded: LoadedModel<'scene>)
    (state: SessionState<'scene>)
    : SessionState<'scene> =
    let nextCam =
        if keepCamera && state.Camera.IsSome then
            state.Camera.Value
        elif state.Loaded.IsNone && state.PendingCamera.IsSome then
            resolveOneShotCamera loaded.Bounds state.PendingCamera aspect
        else
            Camera.fit loaded.Bounds Camera.defaultAzimuthDeg Camera.defaultElevationDeg aspect

    { state with
        Loaded = Some loaded
        Camera = Some nextCam
        PendingCamera = None }

/// Transactionally executes `load`: on failure, returns `Error` and leaves `state` untouched (§6.3).
let tryLoad
    (loader: string -> LoadOptions -> Result<LoadedModel<'scene>, 'err>)
    (aspect: float)
    (input: string)
    (opts: LoadOptions)
    (keepCamera: bool)
    (state: SessionState<'scene>)
    : Result<SessionState<'scene>, 'err> =
    match loader input opts with
    | Error e -> Error e
    | Ok loaded -> Ok(applyLoad aspect keepCamera loaded state)

/// Transactionally executes `reload`: fails if no model is loaded, or preserves `state` on error (§6.3).
let tryReload
    (loader: string -> LoadOptions -> Result<LoadedModel<'scene>, 'err>)
    (aspect: float)
    (keepCamera: bool)
    (state: SessionState<'scene>)
    : Result<SessionState<'scene>, Choice<string, 'err>> =
    match state.Loaded with
    | None -> Error(Choice1Of2 "no model loaded")
    | Some prev ->
        match loader prev.Input prev.Load with
        | Error e -> Error(Choice2Of2 e)
        | Ok loaded -> Ok(applyLoad aspect keepCamera loaded state)

/// Applies `camera <az> <el> [dist|fit]`, allowed both before and after `load` (§6.3).
let applySetCamera (aspect: float) (spec: CameraSpec) (state: SessionState<'scene>) : SessionState<'scene> =
    match state.Loaded with
    | None ->
        let mergedDist =
            match spec.Distance, state.PendingCamera with
            | KeepOrAutoFit, Some prev -> prev.Distance
            | d, _ -> d

        { state with
            PendingCamera =
                Some
                    { AzimuthDeg = spec.AzimuthDeg
                      ElevationDeg = spec.ElevationDeg
                      Distance = mergedDist } }
    | Some loaded ->
        let cam = resolveRenderCamera state loaded.Bounds (Some spec) aspect
        { state with Camera = Some cam }

/// Applies `fit`: resets target to `(0, 0, 0)` and auto-fits at current `(az, el)` (§6.3).
let applyFit (aspect: float) (state: SessionState<'scene>) : Result<SessionState<'scene>, string> =
    match state.Loaded with
    | None -> Error "no model loaded"
    | Some loaded ->
        let az, el =
            match state.Camera with
            | Some cam -> cam.AzimuthDeg, cam.ElevationDeg
            | None -> Camera.defaultAzimuthDeg, Camera.defaultElevationDeg

        Ok { state with Camera = Some(Camera.fit loaded.Bounds az el aspect) }

let applySetSize (width: int) (height: int) (state: SessionState<'scene>) : SessionState<'scene> =
    { state with DefaultSize = (width, height) }

let applySetWireframe (enabled: bool) (state: SessionState<'scene>) : SessionState<'scene> =
    { state with Wireframe = enabled }

let applySetGrid (enabled: bool) (state: SessionState<'scene>) : SessionState<'scene> =
    { state with Grid = enabled }
