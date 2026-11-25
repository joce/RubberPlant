/// CLI entry point, headless rendering dispatch, atomic writes, and `batch` mode
/// for `rubberplant-viewer` (docs/viewer.md §5–§6). Graphics-API-free (§2.4).
module RubberPlant.Viewer.Program

open System
open System.Collections.Generic
open System.IO
open System.Text
open RubberPlant
open RubberPlant.Backends

type private LoadedViewerModel =
    Protocol.LoadedModel<Scene.MeshScene * Scene.GridGeometry option>

let private utf8NoBom = UTF8Encoding(false)

let private ensureUtf8Stdout () =
    if
        Console.OutputEncoding.CodePage <> Encoding.UTF8.CodePage
        || Console.OutputEncoding.GetPreamble().Length <> 0
    then
        Console.OutputEncoding <- utf8NoBom

let private report (file: string) (diags: Diagnostic list) (stderr: TextWriter) =
    for d in diags do
        stderr.WriteLine(Diagnostic.format file d)

let private loadModel
    (input: string)
    (opts: Protocol.LoadOptions)
    (stderr: TextWriter)
    : Result<LoadedViewerModel, unit> =
    let source =
        try
            Ok(File.ReadAllText input)
        with ex ->
            Error ex.Message

    match source with
    | Error msg ->
        stderr.WriteLine($"%s{input}: error: %s{msg}")
        Error()
    | Ok src ->
        match Pipeline.compile src with
        | Error diags ->
            report input diags stderr
            Error()
        | Ok(model, warnings) ->
            report input warnings stderr

            let model =
                { model with
                    Settings = Protocol.applySettingsOverrides opts model.Settings }

            let limits = Protocol.toLimits opts

            match Pipeline.runWith limits model with
            | Error diags ->
                report input diags stderr
                Error()
            | Ok output ->
                report input output.Warnings stderr

                match
                    Stats.analyze
                        output.Derived
                        output.Drawing
                        output.DerivationTrace
                        output.Geometry
                        output.TurtleTrace
                with
                | Error diags ->
                    report input diags stderr
                    Error()
                | Ok stats ->
                    match Mesh.build (Protocol.toMeshOptions opts) output.Geometry with
                    | Error msg ->
                        stderr.WriteLine($"%s{input}: error: %s{msg}")
                        Error()
                    | Ok mesh ->
                        match Scene.fromMesh mesh with
                        | Error msg ->
                            stderr.WriteLine($"%s{input}: error: %s{msg}")
                            Error()
                        | Ok scene ->
                            let grid = Scene.buildGrid scene

                            Ok
                                { Input = input
                                  Load = opts
                                  Bounds = scene.Bounds
                                  Stats = stats
                                  Scene = (scene, grid) }

let private checkWritable (path: string) =
    using (new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None)) ignore

let private tryDelete (path: string) =
    try
        if File.Exists path then
            File.Delete path
    with _ ->
        ()

/// Atomically writes every `(destPath, bytes)` pair via sibling temp files
/// `<dir>/.rubberplant-viewer.<pid>.<guid>.tmp` and `File.Move(tmp, dest, true)` (§5.6).
let private writeAtomic (files: (string * byte[]) list) : Result<string list, string> =
    let comparer =
        if OperatingSystem.IsWindows() then
            StringComparer.OrdinalIgnoreCase
        else
            StringComparer.Ordinal

    let seen = HashSet<string>(comparer)
    let resolved = ResizeArray<string * string * byte[]>()
    let mutable precheckError: string option = None

    for dest, bytes in files do
        if precheckError.IsNone then
            try
                if Directory.Exists dest then
                    precheckError <- Some $"%s{dest}: is a directory"
                else
                    let fullDest = Path.GetFullPath dest
                    let dir = Path.GetDirectoryName fullDest

                    if String.IsNullOrEmpty dir then
                        precheckError <- Some $"%s{dest}: invalid output path"
                    elif not (seen.Add fullDest) then
                        precheckError <- Some $"conflicting output path '%s{dest}'"
                    else
                        if File.Exists fullDest then
                            checkWritable fullDest

                        resolved.Add(dest, fullDest, bytes)
            with ex ->
                precheckError <- Some $"%s{dest}: %s{ex.Message}"

    match precheckError with
    | Some err -> Error err
    | None ->
        let pid = string Environment.ProcessId
        let staged = ResizeArray<string * string * string>() // (origDest, fullDest, tmp)
        let mutable stageError: string option = None

        for dest, fullDest, bytes in resolved do
            if stageError.IsNone then
                let dir = Path.GetDirectoryName fullDest
                let guid = Guid.NewGuid().ToString("N")
                let tmp = Path.Combine(dir, $".rubberplant-viewer.%s{pid}.%s{guid}.tmp")

                try
                    File.WriteAllBytes(tmp, bytes)
                    staged.Add(dest, fullDest, tmp)
                with ex ->
                    tryDelete tmp
                    stageError <- Some $"%s{dest}: %s{ex.Message}"

        match stageError with
        | Some err ->
            for _, _, tmp in staged do
                tryDelete tmp

            Error err
        | None ->
            let mutable moveError: string option = None

            for dest, fullDest, tmp in staged do
                if moveError.IsNone then
                    try
                        File.Move(tmp, fullDest, overwrite = true)
                    with ex ->
                        moveError <- Some $"%s{dest}: %s{ex.Message}"

            match moveError with
            | Some err ->
                for _, _, tmp in staged do
                    tryDelete tmp

                Error err
            | None -> Ok [ for dest, _, _ in staged -> dest ]

let private renderContactSheet
    (renderer: Renderer.IOffscreenRenderer)
    (bounds: Camera.Bounds)
    (sheetSize: int)
    (showGrid: bool)
    (wireframe: bool)
    : Result<byte[], string> =
    let tileSize = sheetSize / 2
    let dist = Camera.contactSheetDistance bounds
    let tiles = Array.zeroCreate<byte[]> Camera.canonicalViews.Length
    let mutable errMsg: string option = None

    for i = 0 to Camera.canonicalViews.Length - 1 do
        if errMsg.IsNone then
            let view = Camera.canonicalViews[i]

            let cam: Camera.Camera =
                { Target = Geometry.Vec3.zero
                  AzimuthDeg = view.AzimuthDeg
                  ElevationDeg = view.ElevationDeg
                  Distance = dist }

            match renderer.RenderShaded tileSize tileSize cam showGrid wireframe with
            | Ok rgb -> tiles[i] <- rgb
            | Error msg -> errMsg <- Some msg

    match errMsg with
    | Some e -> Error e
    | None -> Ok(Png.encodeContactSheet tileSize tiles[0] tiles[1] tiles[2] tiles[3])

let private writeRenderOutputs
    (pngPath: string)
    (pngBytes: byte[])
    (stats: Stats.Stats)
    (statsOut: Protocol.StatsOutput)
    (stdout: TextWriter)
    (stderr: TextWriter)
    : int =
    let files =
        match statsOut.OutPath with
        | None -> [ pngPath, pngBytes ]
        | Some path ->
            let jsonBytes = utf8NoBom.GetBytes(Stats.toJson statsOut.Pretty stats)
            [ pngPath, pngBytes; path, jsonBytes ]

    match writeAtomic files with
    | Error msg ->
        stderr.WriteLine($"error: can't write the output: %s{msg}")
        1
    | Ok paths ->
        if statsOut.PrintStdout then
            stdout.Write(Stats.toJson statsOut.Pretty stats)
        else
            for p in paths do
                stdout.WriteLine($"wrote %s{p}")

        0

let private runHeadlessOneShot
    (baseSpec: Protocol.HeadlessRenderSpec)
    (renderFn: Renderer.IOffscreenRenderer -> Scene.MeshScene -> Result<byte[], string>)
    (stdout: TextWriter)
    (stderr: TextWriter)
    : int =
    match loadModel baseSpec.Input baseSpec.Load stderr with
    | Error() -> 1
    | Ok loaded ->
        match Renderer.createHeadless () with
        | Error msg ->
            stderr.WriteLine($"rubberplant-viewer: %s{msg}")
            1
        | Ok renderer ->
            use r = renderer
            let scene, grid = loaded.Scene
            r.UploadScene scene grid

            match renderFn r scene with
            | Error msg ->
                stderr.WriteLine($"rubberplant-viewer: %s{msg}")
                1
            | Ok pngBytes -> writeRenderOutputs baseSpec.Output pngBytes loaded.Stats baseSpec.Stats stdout stderr

let private runBatchCmd
    (renderer: Renderer.IOffscreenRenderer)
    (state: Protocol.SessionState<Scene.MeshScene * Scene.GridGeometry option> byref)
    (cmd: Protocol.BatchCommand)
    (stdout: TextWriter)
    (stderr: TextWriter)
    : int =
    let loader path opts = loadModel path opts stderr
    let aspect = Protocol.sessionAspect state

    match cmd with
    | Protocol.Load(input, opts, keepCamera) ->
        match Protocol.tryLoad loader aspect input opts keepCamera state with
        | Error() -> 1
        | Ok nextState ->
            let scene, grid = nextState.Loaded.Value.Scene
            renderer.UploadScene scene grid
            state <- nextState
            0
    | Protocol.Reload keepCamera ->
        match Protocol.tryReload loader aspect keepCamera state with
        | Error(Choice1Of2 msg) ->
            stderr.WriteLine($"rubberplant-viewer: %s{msg}")
            1
        | Error(Choice2Of2()) -> 1
        | Ok nextState ->
            let scene, grid = nextState.Loaded.Value.Scene
            renderer.UploadScene scene grid
            state <- nextState
            0
    | Protocol.SetCamera spec ->
        state <- Protocol.applySetCamera aspect spec state
        0
    | Protocol.Fit ->
        match Protocol.applyFit aspect state with
        | Error msg ->
            stderr.WriteLine($"rubberplant-viewer: %s{msg}")
            1
        | Ok nextState ->
            state <- nextState
            0
    | Protocol.SetSize(w, h) ->
        state <- Protocol.applySetSize w h state
        0
    | Protocol.SetWireframe enabled ->
        state <- Protocol.applySetWireframe enabled state
        0
    | Protocol.SetGrid enabled ->
        state <- Protocol.applySetGrid enabled state
        0
    | Protocol.BatchSnapshot s ->
        match Protocol.requireLoaded state with
        | Error msg ->
            stderr.WriteLine($"rubberplant-viewer: %s{msg}")
            1
        | Ok loaded ->
            let scene, _ = loaded.Scene
            let w, h = defaultArg s.Size state.DefaultSize
            let cam = Protocol.resolveRenderCamera state scene.Bounds s.Camera (float w / float h)

            match renderer.RenderShaded w h cam state.Grid state.Wireframe with
            | Error msg ->
                stderr.WriteLine($"rubberplant-viewer: %s{msg}")
                1
            | Ok rgb -> writeRenderOutputs s.Output (Png.encodeRgb24 w h rgb) loaded.Stats s.Stats stdout stderr
    | Protocol.BatchContactSheet c ->
        match Protocol.requireLoaded state with
        | Error msg ->
            stderr.WriteLine($"rubberplant-viewer: %s{msg}")
            1
        | Ok loaded ->
            let scene, _ = loaded.Scene
            let sheetSize = defaultArg c.Size Protocol.defaultContactSheetSize

            match renderContactSheet renderer scene.Bounds sheetSize state.Grid state.Wireframe with
            | Error msg ->
                stderr.WriteLine($"rubberplant-viewer: %s{msg}")
                1
            | Ok png -> writeRenderOutputs c.Output png loaded.Stats c.Stats stdout stderr
    | Protocol.BatchSilhouette m ->
        match Protocol.requireLoaded state with
        | Error msg ->
            stderr.WriteLine($"rubberplant-viewer: %s{msg}")
            1
        | Ok loaded ->
            let scene, _ = loaded.Scene
            let w, h = defaultArg m.Size state.DefaultSize
            let cam = Protocol.resolveRenderCamera state scene.Bounds m.Camera (float w / float h)

            match renderer.RenderSilhouette w h cam with
            | Error msg ->
                stderr.WriteLine($"rubberplant-viewer: %s{msg}")
                1
            | Ok gray -> writeRenderOutputs m.Output (Png.encodeGray8 w h gray) loaded.Stats m.Stats stdout stderr
    | Protocol.BatchStats pretty ->
        match Protocol.requireLoaded state with
        | Error msg ->
            stderr.WriteLine($"rubberplant-viewer: %s{msg}")
            1
        | Ok loaded ->
            stdout.Write(Stats.toJson pretty loaded.Stats)
            0
    | Protocol.Quit -> 0

/// Runs `rubberplant-viewer batch` against arbitrary readers/writers (testable without
/// mutating global `Console` streams).
let internal executeBatch (stdin: TextReader) (stdout: TextWriter) (stderr: TextWriter) : int =
    match Renderer.createHeadless () with
    | Error msg ->
        stderr.WriteLine($"rubberplant-viewer: %s{msg}")
        1
    | Ok renderer ->
        use r = renderer
        let mutable state = Protocol.initialSession<Scene.MeshScene * Scene.GridGeometry option>
        let mutable keepRunning = true

        let writeExit code =
            stderr.Flush()
            stdout.WriteLine($"exit %s{string code}")
            stdout.Flush()

        while keepRunning do
            let line = stdin.ReadLine()

            if isNull line then
                keepRunning <- false
            else
                match Protocol.parseBatchLine line with
                | Error msg ->
                    stderr.WriteLine($"rubberplant-viewer: %s{msg}")
                    writeExit 2
                | Ok Protocol.Quit ->
                    writeExit 0
                    keepRunning <- false
                | Ok cmd ->
                    let code = runBatchCmd r &state cmd stdout stderr
                    writeExit code

        0

/// Runs the CLI against arbitrary readers/writers.
let internal executeCli (argv: string list) (stdin: TextReader) (stdout: TextWriter) (stderr: TextWriter) : int =
    match Protocol.parseCli argv with
    | Ok(Protocol.Help code) ->
        stdout.Write(Protocol.usage)
        stdout.Flush()
        code
    | Error msg ->
        stderr.WriteLine($"rubberplant-viewer: %s{msg}")
        stderr.WriteLine()
        stderr.Write(Protocol.usage)
        stderr.Flush()
        2
    | Ok Protocol.Batch -> executeBatch stdin stdout stderr
    | Ok(Protocol.ContactSheet cs) ->
        runHeadlessOneShot
            cs.Base
            (fun r scene -> renderContactSheet r scene.Bounds cs.Size cs.Grid cs.Wireframe)
            stdout
            stderr
    | Ok(Protocol.Snapshot sn) ->
        let w, h = sn.Size

        runHeadlessOneShot
            sn.Base
            (fun r scene ->
                let cam = Protocol.resolveOneShotCamera scene.Bounds sn.Camera (float w / float h)
                r.RenderShaded w h cam sn.Grid sn.Wireframe |> Result.map (Png.encodeRgb24 w h))
            stdout
            stderr
    | Ok(Protocol.Silhouette si) ->
        let w, h = si.Size

        runHeadlessOneShot
            si.Base
            (fun r scene ->
                let cam = Protocol.resolveOneShotCamera scene.Bounds si.Camera (float w / float h)
                r.RenderSilhouette w h cam |> Result.map (Png.encodeGray8 w h))
            stdout
            stderr
    | Ok(Protocol.View _) ->
        stderr.WriteLine("rubberplant-viewer: interactive `view` window lands in BP.2 PR 4")
        stderr.Flush()
        1

[<EntryPoint>]
let main argv =
    ensureUtf8Stdout ()

    let stdin =
        match argv with
        | [| "batch" |] -> new StreamReader(Console.OpenStandardInput(), utf8NoBom) :> TextReader
        | _ -> Console.In

    executeCli (List.ofArray argv) stdin Console.Out Console.Error
