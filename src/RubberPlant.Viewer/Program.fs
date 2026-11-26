/// CLI entry point, headless rendering dispatch, atomic writes, and `batch` mode
/// for `rubberplant-viewer` (docs/viewer.md §5–§6). Graphics-API-free (§2.4).
module RubberPlant.Viewer.Program

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Diagnostics
open System.Globalization
open System.IO
open System.Text
open System.Threading
open RubberPlant
open RubberPlant.Backends

type private ViewerScenePayload =
    { Scene: Scene.MeshScene
      Grid: Scene.GridGeometry option
      CompiledModel: Model.Model
      TargetFrame: Scene.ReferenceFrame }

type private LoadedViewerModel = Protocol.LoadedModel<ViewerScenePayload>

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

let private readSourceWithRetry (retries: int) (delayMs: int) (path: string) : Result<string, string> =
    let rec attempt remaining =
        try
            Ok(File.ReadAllText path)
        with
        | :? FileNotFoundException as ex -> Error ex.Message
        | :? DirectoryNotFoundException as ex -> Error ex.Message
        | :? IOException when remaining > 0 ->
            Thread.Sleep(delayMs)
            attempt (remaining - 1)
        | :? UnauthorizedAccessException when remaining > 0 ->
            Thread.Sleep(delayMs)
            attempt (remaining - 1)
        | ex -> Error ex.Message

    attempt retries

let private compileAndLoadModelWithRetries
    (retries: int)
    (delayMs: int)
    (input: string)
    (opts: Protocol.LoadOptions)
    (stderr: TextWriter)
    : Result<Model.Model, unit> =
    match readSourceWithRetry retries delayMs input with
    | Error msg ->
        stderr.WriteLine($"%s{input}: error: %s{msg}")
        Error()
    | Ok src ->
        match Pipeline.compile src with
        | Error diags ->
            report input diags stderr
            Error()
        | Ok(model, warnings) ->
            let timed = Model.Model.isTimed model

            if opts.Time.IsSome && not timed then
                stderr.WriteLine($"%s{input}: error: --time cannot be used with a discrete model")
                Error()
            elif opts.Derivations.IsSome && timed then
                stderr.WriteLine($"%s{input}: error: --derivations (-n) cannot be used with a timed model")
                Error()
            else
                report input warnings stderr

                let baseDir = Path.GetDirectoryName input

                let readSurfaceFile (relPath: string) =
                    let normalized =
                        relPath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar)

                    let fullPath =
                        if String.IsNullOrEmpty baseDir then
                            normalized
                        else
                            Path.Combine(baseDir, normalized)

                    readSourceWithRetry retries delayMs fullPath

                match Pipeline.loadSurfaces readSurfaceFile model with
                | Error diags ->
                    report input diags stderr
                    Error()
                | Ok(model, surfaceWarnings) ->
                    report input surfaceWarnings stderr

                    Ok
                        { model with
                            Settings = Protocol.applySettingsOverrides opts model.Settings }

let private buildModelAtStage
    (input: string)
    (opts: Protocol.LoadOptions)
    (compiledModel: Model.Model)
    (stage: Protocol.StageInfo)
    (frameOpt: Scene.ReferenceFrame option)
    (stderr: TextWriter)
    : Result<LoadedViewerModel, unit> =
    let modelAtStage =
        match stage with
        | Protocol.DiscreteStage(n, _) ->
            { compiledModel with
                Settings = { compiledModel.Settings with Derivations = n } }
        | Protocol.TimedStage(t, _) ->
            { compiledModel with
                Settings = { compiledModel.Settings with Time = Some t } }

    let limits = Protocol.toLimits opts

    match Pipeline.runWith limits modelAtStage with
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
            let baseMeshOpts = Protocol.toMeshOptionsWithSettings modelAtStage.Settings opts

            match frameOpt with
            | Some frame when not frame.Bounds.IsEmpty ->
                let meshOpts = { baseMeshOpts with WidthScale = Some frame.WidthScale }

                match Mesh.build meshOpts output.Geometry with
                | Error msg ->
                    stderr.WriteLine($"%s{input}: error: %s{msg}")
                    Error()
                | Ok mesh ->
                    match Scene.fromMeshWithFrame frame mesh with
                    | Error msg ->
                        stderr.WriteLine($"%s{input}: error: %s{msg}")
                        Error()
                    | Ok scene ->
                        let grid = Scene.buildGrid scene

                        Ok
                            { Input = input
                              Load = opts
                              Stage = stage
                              Bounds = scene.Bounds
                              Stats = stats
                              Scene =
                                { Scene = scene
                                  Grid = grid
                                  CompiledModel = compiledModel
                                  TargetFrame = frame } }
            | _ ->
                match Mesh.resolveWidthScale baseMeshOpts output.Geometry with
                | Error msg ->
                    stderr.WriteLine($"%s{input}: error: %s{msg}")
                    Error()
                | Ok widthScale ->
                    let meshOpts = { baseMeshOpts with WidthScale = Some widthScale }

                    match Mesh.build meshOpts output.Geometry with
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

                            let targetFrame: Scene.ReferenceFrame =
                                { Center = scene.Center
                                  Extent = scene.Extent
                                  Bounds = scene.Bounds
                                  WidthScale = widthScale }

                            let preservedFrame =
                                match frameOpt with
                                | Some prev when prev.Bounds.IsEmpty -> targetFrame
                                | Some prev -> prev
                                | None -> targetFrame

                            Ok
                                { Input = input
                                  Load = opts
                                  Stage = stage
                                  Bounds = scene.Bounds
                                  Stats = stats
                                  Scene =
                                    { Scene = scene
                                      Grid = grid
                                      CompiledModel = compiledModel
                                      TargetFrame = preservedFrame } }

let private loadModelWithRetries
    (retries: int)
    (delayMs: int)
    (input: string)
    (opts: Protocol.LoadOptions)
    (stderr: TextWriter)
    : Result<LoadedViewerModel, unit> =
    compileAndLoadModelWithRetries retries delayMs input opts stderr
    |> Result.bind (fun model ->
        let stage =
            if Model.Model.isTimed model then
                let t = defaultArg model.Settings.Time 0.0
                Protocol.TimedStage(t, t)
            else
                Protocol.DiscreteStage(model.Settings.Derivations, model.Settings.Derivations)

        buildModelAtStage input opts model stage None stderr)

let private evaluateStage
    (loaded: LoadedViewerModel)
    (nextStage: Protocol.StageInfo)
    (stderr: TextWriter)
    : Result<LoadedViewerModel, unit> =
    buildModelAtStage
        loaded.Input
        loaded.Load
        loaded.Scene.CompiledModel
        nextStage
        (Some loaded.Scene.TargetFrame)
        stderr

let private loadModel
    (input: string)
    (opts: Protocol.LoadOptions)
    (stderr: TextWriter)
    : Result<LoadedViewerModel, unit> =
    loadModelWithRetries 0 0 input opts stderr

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
            let payload = loaded.Scene
            r.UploadScene payload.Scene payload.Grid

            match renderFn r payload.Scene with
            | Error msg ->
                stderr.WriteLine($"rubberplant-viewer: %s{msg}")
                1
            | Ok pngBytes -> writeRenderOutputs baseSpec.Output pngBytes loaded.Stats baseSpec.Stats stdout stderr

let private frameModelForSchedule (model: Model.Model) (schedule: Protocol.SequenceSchedule) : seq<int * Model.Model> =
    match schedule with
    | Protocol.DiscreteSteps steps ->
        steps
        |> Seq.mapi (fun i n ->
            i, { model with Settings = { model.Settings with Derivations = n } })
    | Protocol.TimedPoints times ->
        times
        |> Seq.mapi (fun i t ->
            i, { model with Settings = { model.Settings with Time = Some t } })

let private executeSequence
    (renderer: Renderer.IOffscreenRenderer)
    (inputPath: string)
    (model: Model.Model)
    (loadOpts: Protocol.LoadOptions)
    (outputPrefix: string)
    (fromVal: float)
    (toVal: float)
    (stepOpt: float option)
    (framesOpt: int option)
    (mode: Protocol.SequenceMode)
    (snapSize: int * int)
    (sheetSize: int)
    (showGrid: bool)
    (wireframe: bool)
    (resolveCam: Camera.Bounds -> float -> Camera.Camera)
    (stdout: TextWriter)
    (stderr: TextWriter)
    : int =
    let isTimed = Model.Model.isTimed model

    match Protocol.resolveSequenceSchedule isTimed fromVal toVal stepOpt framesOpt with
    | Error msg ->
        stderr.WriteLine($"rubberplant-viewer: %s{msg}")
        2
    | Ok schedule ->
        let baseMeshOpts = Protocol.toMeshOptionsWithSettings model.Settings loadOpts

        let precheckError =
            if baseMeshOpts.Sides < 3 then
                Some $"a cylinder needs at least 3 sides, got %s{string baseMeshOpts.Sides}"
            else
                match baseMeshOpts.SurfaceDiv with
                | Some d when d < 1 -> Some $"surface subdivision must be at least 1, got %s{string d}"
                | _ ->
                    match baseMeshOpts.WidthScale with
                    | Some k when not (Double.IsFinite k && k > 0.0) ->
                        Some $"the width scale must be a finite number greater than 0, got %s{string k}"
                    | _ ->
                        try
                            let samplePath = $"%s{outputPrefix}0000.png"

                            if Directory.Exists samplePath then
                                Some $"%s{samplePath}: is a directory"
                            else
                                let fullSample = Path.GetFullPath samplePath
                                let dir = Path.GetDirectoryName fullSample

                                if String.IsNullOrEmpty dir then
                                    Some $"%s{samplePath}: invalid output path"
                                elif not (Directory.Exists dir) then
                                    Some $"Could not find a part of the path '%s{dir}'."
                                else
                                    None
                        with ex ->
                            Some ex.Message

        match precheckError with
        | Some msg ->
            stderr.WriteLine($"%s{inputPath}: error: %s{msg}")
            1
        | None ->
            let limits = Protocol.toLimits loadOpts

            let accumulateExactBounds (fixedScale: float) : Result<Scene.ReferenceFrame, unit> =
                let meshOpts = { baseMeshOpts with WidthScale = Some fixedScale }
                let mutable accBounds: (Geometry.Vec3 * Geometry.Vec3) option = None
                let mutable failed = false

                for _, frameModel in frameModelForSchedule model schedule do
                    if not failed then
                        match Pipeline.runWith limits frameModel with
                        | Error diags ->
                            report inputPath diags stderr
                            failed <- true
                        | Ok output ->
                            match Mesh.build meshOpts output.Geometry with
                            | Error msg ->
                                stderr.WriteLine($"%s{inputPath}: error: %s{msg}")
                                failed <- true
                            | Ok mesh ->
                                match Scene.validateAndAccumulateMeshBounds mesh accBounds with
                                | Error msg ->
                                    stderr.WriteLine($"%s{inputPath}: error: %s{msg}")
                                    failed <- true
                                | Ok nextAcc -> accBounds <- nextAcc

                if failed then
                    Error()
                else
                    match Scene.referenceFrameOfBounds fixedScale accBounds with
                    | Error msg ->
                        stderr.WriteLine($"%s{inputPath}: error: %s{msg}")
                        Error()
                    | Ok frame -> Ok frame

            // Pass 1 (O(1) memory, no retained meshes): derive fixed WidthScale and ReferenceFrame.
            let frameRes =
                match baseMeshOpts.WidthScale with
                | Some k -> accumulateExactBounds k
                | None ->
                    let mutable hasPts = false
                    let mutable minX = infinity
                    let mutable minY = infinity
                    let mutable maxX = -infinity
                    let mutable maxY = -infinity
                    let mutable maxWidest = 0.0
                    let mutable failed = false

                    for _, frameModel in frameModelForSchedule model schedule do
                        if not failed then
                            match Pipeline.runWith limits frameModel with
                            | Error diags ->
                                report inputPath diags stderr
                                failed <- true
                            | Ok output ->
                                let struct (gMin, gMax, gHas, gWidest) =
                                    Mesh.geometryBoundsAndMaxWidth output.Geometry

                                maxWidest <- max maxWidest gWidest

                                if gHas then
                                    hasPts <- true
                                    minX <- min minX gMin.X
                                    minY <- min minY gMin.Y
                                    maxX <- max maxX gMax.X
                                    maxY <- max maxY gMax.Y

                    if failed then
                        Error()
                    else
                        let extentXY = if hasPts then max (maxX - minX) (maxY - minY) else 0.0

                        match Mesh.resolveAutoWidthScale extentXY maxWidest with
                        | Error msg ->
                            stderr.WriteLine($"%s{inputPath}: error: %s{msg}")
                            Error()
                        | Ok autoK -> accumulateExactBounds autoK

            match frameRes with
            | Error() -> 1
            | Ok frame ->
                // Pass 2 (O(1) memory): re-evaluate each frame with fixed WidthScale and ReferenceFrame, render, and write atomically.
                let seqMeshOpts = { baseMeshOpts with WidthScale = Some frame.WidthScale }
                let w, h = snapSize
                let cam = resolveCam frame.Bounds (float w / float h)
                let mutable failed = false

                for i, frameModel in frameModelForSchedule model schedule do
                    if not failed then
                        match Pipeline.runWith limits frameModel with
                        | Error diags ->
                            report inputPath diags stderr
                            failed <- true
                        | Ok output ->
                            report inputPath output.Warnings stderr

                            match Mesh.build seqMeshOpts output.Geometry with
                            | Error msg ->
                                stderr.WriteLine($"%s{inputPath}: error: %s{msg}")
                                failed <- true
                            | Ok mesh ->
                                match Scene.fromMeshWithFrame frame mesh with
                                | Error msg ->
                                    stderr.WriteLine($"%s{inputPath}: error: %s{msg}")
                                    failed <- true
                                | Ok scene ->
                                    let grid =
                                        if mode = Protocol.SeqSilhouette || not showGrid then
                                            None
                                        else
                                            Scene.buildGrid scene

                                    renderer.UploadScene scene grid

                                    let pngRes =
                                        match mode with
                                        | Protocol.SeqSnapshot ->
                                            renderer.RenderShaded w h cam showGrid wireframe
                                            |> Result.map (Png.encodeRgb24 w h)
                                        | Protocol.SeqContactSheet ->
                                            renderContactSheet renderer frame.Bounds sheetSize showGrid wireframe
                                        | Protocol.SeqSilhouette ->
                                            renderer.RenderSilhouette w h cam
                                            |> Result.map (Png.encodeGray8 w h)

                                    match pngRes with
                                    | Error msg ->
                                        stderr.WriteLine($"rubberplant-viewer: %s{msg}")
                                        failed <- true
                                    | Ok pngBytes ->
                                        let idxStr = i.ToString("D4", CultureInfo.InvariantCulture)
                                        let outPath = $"%s{outputPrefix}%s{idxStr}.png"

                                        match writeAtomic [ outPath, pngBytes ] with
                                        | Error msg ->
                                            stderr.WriteLine($"error: can't write the output: %s{msg}")
                                            failed <- true
                                        | Ok paths ->
                                            for p in paths do
                                                stdout.WriteLine($"wrote %s{p}")

                if failed then 1 else 0

let private runBatchCmd
    (renderer: Renderer.IOffscreenRenderer)
    (fitAspect: float)
    (state: Protocol.SessionState<ViewerScenePayload> byref)
    (cmd: Protocol.BatchCommand)
    (stdout: TextWriter)
    (stderr: TextWriter)
    : int =
    let loader path opts = loadModel path opts stderr

    match cmd with
    | Protocol.Load(input, opts, keepCamera) ->
        match Protocol.tryLoad loader fitAspect input opts keepCamera state with
        | Error() -> 1
        | Ok nextState ->
            let p = nextState.Loaded.Value.Scene
            renderer.UploadScene p.Scene p.Grid
            state <- nextState
            0
    | Protocol.Reload keepCamera ->
        match Protocol.tryReload loader fitAspect keepCamera state with
        | Error(Choice1Of2 msg) ->
            stderr.WriteLine($"rubberplant-viewer: %s{msg}")
            1
        | Error(Choice2Of2()) -> 1
        | Ok nextState ->
            let p = nextState.Loaded.Value.Scene
            renderer.UploadScene p.Scene p.Grid
            state <- nextState
            0
    | Protocol.Step n ->
        match Protocol.requireLoaded state with
        | Error msg ->
            stderr.WriteLine($"rubberplant-viewer: %s{msg}")
            1
        | Ok loaded ->
            if Model.Model.isTimed loaded.Scene.CompiledModel then
                stderr.WriteLine($"%s{loaded.Input}: error: step cannot be used with a timed model")
                1
            else
                let target =
                    match loaded.Stage with
                    | Protocol.DiscreteStage(_, tgt) -> tgt
                    | Protocol.TimedStage _ -> loaded.Scene.CompiledModel.Settings.Derivations

                match evaluateStage loaded (Protocol.DiscreteStage(n, target)) stderr with
                | Error() -> 1
                | Ok nextLoaded ->
                    let p = nextLoaded.Scene
                    renderer.UploadScene p.Scene p.Grid
                    state <- { state with Loaded = Some nextLoaded }
                    0
    | Protocol.Time t ->
        match Protocol.requireLoaded state with
        | Error msg ->
            stderr.WriteLine($"rubberplant-viewer: %s{msg}")
            1
        | Ok loaded ->
            if not (Model.Model.isTimed loaded.Scene.CompiledModel) then
                stderr.WriteLine($"%s{loaded.Input}: error: time cannot be used with a discrete model")
                1
            else
                let target =
                    match loaded.Stage with
                    | Protocol.TimedStage(_, tgt) -> tgt
                    | Protocol.DiscreteStage _ -> t

                match evaluateStage loaded (Protocol.TimedStage(t, target)) stderr with
                | Error() -> 1
                | Ok nextLoaded ->
                    let p = nextLoaded.Scene
                    renderer.UploadScene p.Scene p.Grid
                    state <- { state with Loaded = Some nextLoaded }
                    0
    | Protocol.SetCamera spec ->
        state <- Protocol.applySetCamera fitAspect spec state
        0
    | Protocol.Fit ->
        match Protocol.applyFit fitAspect state with
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
            let scene = loaded.Scene.Scene
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
            let scene = loaded.Scene.Scene
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
            let scene = loaded.Scene.Scene
            let w, h = defaultArg m.Size state.DefaultSize
            let cam = Protocol.resolveRenderCamera state scene.Bounds m.Camera (float w / float h)

            match renderer.RenderSilhouette w h cam with
            | Error msg ->
                stderr.WriteLine($"rubberplant-viewer: %s{msg}")
                1
            | Ok gray -> writeRenderOutputs m.Output (Png.encodeGray8 w h gray) loaded.Stats m.Stats stdout stderr
    | Protocol.BatchSequence seq ->
        match Protocol.requireLoaded state with
        | Error msg ->
            stderr.WriteLine($"rubberplant-viewer: %s{msg}")
            1
        | Ok loaded ->
            let curState = state
            let prevPayload = loaded.Scene
            let snapSize = defaultArg seq.SnapshotSize curState.DefaultSize
            let sheetSize = defaultArg seq.ContactSheetSize Protocol.defaultContactSheetSize
            let resolveCam bounds aspect = Protocol.resolveRenderCamera curState bounds seq.Camera aspect

            try
                executeSequence
                    renderer
                    loaded.Input
                    prevPayload.CompiledModel
                    loaded.Load
                    seq.OutputPrefix
                    seq.From
                    seq.To
                    seq.Step
                    seq.Frames
                    seq.Mode
                    snapSize
                    sheetSize
                    curState.Grid
                    curState.Wireframe
                    resolveCam
                    stdout
                    stderr
            finally
                renderer.UploadScene prevPayload.Scene prevPayload.Grid
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
        let mutable state = Protocol.initialSession<ViewerScenePayload>
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
                    let code = runBatchCmd r (Protocol.sessionAspect state) &state cmd stdout stderr
                    writeExit code

        0

/// Watches the parent directory of a target `.rp` file (handling in-place writes and
/// atomic renames) and debounces rapid filesystem events (§6.4).
type private FileWatcher(onDebouncedChange: unit -> unit, stderr: TextWriter) =
    let gate = obj ()
    let mutable watcher: FileSystemWatcher option = None
    let mutable targetFullPath: string option = None
    let mutable disposed = false

    let timer =
        new Timer(
            TimerCallback(fun _ ->
                let shouldFire = lock gate (fun () -> not disposed && watcher.IsSome)

                if shouldFire then
                    onDebouncedChange ()),
            null,
            Timeout.Infinite,
            Timeout.Infinite
        )

    let pathComparison =
        if OperatingSystem.IsWindows() then
            StringComparison.OrdinalIgnoreCase
        else
            StringComparison.Ordinal

    let isTargetPath (candidate: string) =
        if String.IsNullOrEmpty candidate then
            false
        else
            lock gate (fun () ->
                match targetFullPath with
                | None -> false
                | Some target ->
                    try
                        String.Equals(Path.GetFullPath candidate, target, pathComparison)
                    with _ ->
                        false)

    let scheduleReload () =
        lock gate (fun () ->
            if not disposed && watcher.IsSome then
                try
                    timer.Change(60, Timeout.Infinite) |> ignore
                with _ ->
                    ())

    member _.SetTarget(pathOpt: string option) =
        lock gate (fun () ->
            if not disposed then
                watcher
                |> Option.iter (fun w ->
                    try
                        w.EnableRaisingEvents <- false
                        w.Dispose()
                    with _ ->
                        ())

                watcher <- None
                targetFullPath <- None

                try
                    timer.Change(Timeout.Infinite, Timeout.Infinite) |> ignore
                with _ ->
                    ()

                match pathOpt with
                | None -> ()
                | Some path ->
                    try
                        let fullPath = Path.GetFullPath path
                        let dir = Path.GetDirectoryName fullPath

                        if not (String.IsNullOrEmpty dir) && Directory.Exists dir then
                            targetFullPath <- Some fullPath
                            let fsw = new FileSystemWatcher(dir)
                            fsw.IncludeSubdirectories <- false

                            fsw.NotifyFilter <-
                                NotifyFilters.LastWrite
                                ||| NotifyFilters.FileName
                                ||| NotifyFilters.Size
                                ||| NotifyFilters.CreationTime

                            fsw.Changed.Add(fun e -> if isTargetPath e.FullPath then scheduleReload ())
                            fsw.Created.Add(fun e -> if isTargetPath e.FullPath then scheduleReload ())
                            fsw.Renamed.Add(fun e -> if isTargetPath e.FullPath then scheduleReload ())
                            fsw.EnableRaisingEvents <- true
                            watcher <- Some fsw
                    with ex ->
                        targetFullPath <- None
                        stderr.WriteLine($"rubberplant-viewer: warning: cannot watch '%s{path}': %s{ex.Message}")
                        stderr.Flush())

    interface IDisposable with
        member _.Dispose() =
            let shouldDisposeTimer =
                lock gate (fun () ->
                    if not disposed then
                        disposed <- true

                        watcher
                        |> Option.iter (fun w ->
                            try
                                w.EnableRaisingEvents <- false
                                w.Dispose()
                            with _ ->
                                ())

                        watcher <- None
                        targetFullPath <- None
                        true
                    else
                        false)

            if shouldDisposeTimer then
                try
                    use doneWait = new ManualResetEvent(false)

                    if timer.Dispose(doneWait) then
                        doneWait.WaitOne(1000) |> ignore
                with _ ->
                    try
                        timer.Dispose()
                    with _ ->
                        ()

/// Runs interactive `view` mode with configurable window visibility (used headless in GPU tests).
let internal executeViewWithVisibility
    (visible: bool)
    (args: Protocol.ViewArgs)
    (stdin: TextReader)
    (stdout: TextWriter)
    (stderr: TextWriter)
    : int =
    let initialLoadedRes =
        match args.Input with
        | None -> Ok None
        | Some path -> loadModel path args.Load stderr |> Result.map Some

    match initialLoadedRes with
    | Error() ->
        stderr.Flush()
        1
    | Ok initialLoaded ->
        let winW, winH = args.WindowSize

        let winOpts: Renderer.WindowOptions =
            { Width = winW
              Height = winH
              Title = Interaction.windowTitle initialLoaded
              Visible = visible }

        match Renderer.createWindow winOpts with
        | Error msg ->
            stderr.WriteLine($"rubberplant-viewer: %s{msg}")
            stderr.Flush()
            1
        | Ok renderer ->
            use r = renderer
            let struct (fbW, fbH) = r.FramebufferSize()

            let initAspect =
                if fbW > 0 && fbH > 0 then
                    float fbW / float fbH
                else
                    float (max 1 winW) / float (max 1 winH)

            let mutable state =
                { Protocol.initialSession<ViewerScenePayload> with
                    Wireframe = args.Wireframe
                    Grid = args.Grid
                    PendingCamera = args.Camera }

            let mutable interactive = Interaction.createInitialState initAspect args.Camera

            match initialLoaded with
            | Some loaded ->
                state <- Protocol.applyLoad initAspect false loaded state
                let p = loaded.Scene
                r.UploadScene p.Scene p.Grid
            | None ->
                r.UploadScene Scene.emptyScene (Scene.buildGrid Scene.emptyScene)

            let cmdQueue = ConcurrentQueue<string>()
            let mutable stopping = false
            let mutable watchReloadPending = 0

            let watcher =
                if args.Watch then
                    let w =
                        new FileWatcher(
                            (fun () ->
                                Interlocked.Exchange(&watchReloadPending, 1) |> ignore
                                r.PostWakeup()),
                            stderr
                        )

                    initialLoaded |> Option.iter (fun loaded -> w.SetTarget(Some loaded.Input))
                    Some w
                else
                    None

            let readerThread =
                Thread(
                    ThreadStart(fun () ->
                        try
                            let mutable doneReading = false

                            while not doneReading && not (Volatile.Read(&stopping)) do
                                let line = stdin.ReadLine()

                                if isNull line then
                                    doneReading <- true
                                elif not (Volatile.Read(&stopping)) then
                                    cmdQueue.Enqueue(line)
                                    r.PostWakeup()
                        with
                        | :? ObjectDisposedException -> ()
                        | :? IOException -> ())
                )

            readerThread.IsBackground <- true
            readerThread.Name <- "rubberplant-viewer-stdin"
            readerThread.Start()

            let writeExit code =
                stderr.Flush()
                stdout.WriteLine($"exit %s{string code}")
                stdout.Flush()

            let loader path opts = loadModel path opts stderr
            let watchLoader path opts = loadModelWithRetries 4 25 path opts stderr

            let mutable dirty = true
            let mutable keepRunning = true
            let mutable lastPlaybackTick = Stopwatch.GetTimestamp()

            let applyInteractiveStage (nextStage: Protocol.StageInfo) =
                match state.Loaded with
                | None -> ()
                | Some loaded ->
                    match evaluateStage loaded nextStage stderr with
                    | Ok nextLoaded ->
                        stderr.Flush()
                        let p = nextLoaded.Scene
                        r.UploadScene p.Scene p.Grid
                        state <- { state with Loaded = Some nextLoaded }
                        r.SetTitle(Interaction.windowTitle state.Loaded)
                        dirty <- true
                    | Error() ->
                        stderr.Flush()
                        interactive <- { interactive with Playing = false }

            let handleInteractiveEffect (eff: Interaction.InteractiveEffect) =
                match eff with
                | Interaction.NoEffect -> ()
                | Interaction.Redraw -> dirty <- true
                | Interaction.Close ->
                    keepRunning <- false
                    r.RequestClose()
                | Interaction.Reload ->
                    match Protocol.tryReload loader interactive.LastValidAspect true state with
                    | Ok nextState ->
                        stderr.Flush()
                        let p = nextState.Loaded.Value.Scene
                        r.UploadScene p.Scene p.Grid
                        state <- nextState
                        r.SetTitle(Interaction.windowTitle state.Loaded)
                        dirty <- true
                    | Error(Choice1Of2 _) -> ()
                    | Error(Choice2Of2()) -> stderr.Flush()
                | Interaction.StepDiscrete n ->
                    match state.Loaded with
                    | Some loaded ->
                        let tgt =
                            match loaded.Stage with
                            | Protocol.DiscreteStage(_, t) -> t
                            | Protocol.TimedStage _ -> loaded.Scene.CompiledModel.Settings.Derivations

                        applyInteractiveStage (Protocol.DiscreteStage(n, tgt))
                    | None -> ()
                | Interaction.StepTimed t ->
                    match state.Loaded with
                    | Some loaded ->
                        let tgt =
                            match loaded.Stage with
                            | Protocol.TimedStage(_, target) -> target
                            | Protocol.DiscreteStage _ -> t

                        applyInteractiveStage (Protocol.TimedStage(t, tgt))
                    | None -> ()

            try
                while keepRunning && not r.ShouldClose do
                    for ev in r.DrainEvents() do
                        if keepRunning && not r.ShouldClose then
                            let wasPlaying = interactive.Playing
                            let nextSession, nextInteractive, effect = Interaction.handleEvent ev state interactive
                            state <- nextSession
                            interactive <- nextInteractive

                            if not wasPlaying && interactive.Playing then
                                lastPlaybackTick <- Stopwatch.GetTimestamp()

                            handleInteractiveEffect effect

                    if
                        keepRunning
                        && not r.ShouldClose
                        && Interlocked.Exchange(&watchReloadPending, 0) = 1
                    then
                        match Protocol.tryReload watchLoader interactive.LastValidAspect true state with
                        | Ok nextState ->
                            stderr.Flush()
                            let p = nextState.Loaded.Value.Scene
                            r.UploadScene p.Scene p.Grid
                            state <- nextState
                            interactive <- { interactive with Playing = false }
                            r.SetTitle(Interaction.windowTitle state.Loaded)
                            dirty <- true
                        | Error(Choice1Of2 _) -> ()
                        | Error(Choice2Of2()) -> stderr.Flush()

                    let mutable line = null

                    while keepRunning && not r.ShouldClose && cmdQueue.TryDequeue(&line) do
                        match Protocol.parseBatchLine line with
                        | Error msg ->
                            stderr.WriteLine($"rubberplant-viewer: %s{msg}")
                            writeExit 2
                        | Ok Protocol.Quit ->
                            writeExit 0
                            keepRunning <- false
                            r.RequestClose()
                        | Ok cmd ->
                            let code = runBatchCmd r interactive.LastValidAspect &state cmd stdout stderr

                            if code = 0 then
                                match cmd with
                                | Protocol.Load _ ->
                                    interactive <- { interactive with Playing = false }
                                    r.SetTitle(Interaction.windowTitle state.Loaded)
                                    watcher |> Option.iter (fun w -> w.SetTarget(state.Loaded |> Option.map _.Input))
                                    dirty <- true
                                | Protocol.Reload _
                                | Protocol.Step _
                                | Protocol.Time _ ->
                                    interactive <- { interactive with Playing = false }
                                    r.SetTitle(Interaction.windowTitle state.Loaded)
                                    dirty <- true
                                | Protocol.Fit
                                | Protocol.SetWireframe _
                                | Protocol.SetGrid _ -> dirty <- true
                                | Protocol.SetCamera _ ->
                                    interactive <- Interaction.syncPreviewCamera state interactive
                                    dirty <- true
                                | Protocol.SetSize _
                                | Protocol.BatchSnapshot _
                                | Protocol.BatchContactSheet _
                                | Protocol.BatchSilhouette _
                                | Protocol.BatchSequence _
                                | Protocol.BatchStats _
                                | Protocol.Quit -> ()

                            writeExit code

                    if keepRunning && not r.ShouldClose && interactive.Playing then
                        let now = Stopwatch.GetTimestamp()
                        let elapsed = float (now - lastPlaybackTick) / float Stopwatch.Frequency

                        if elapsed >= Interaction.playbackIntervalSeconds then
                            lastPlaybackTick <- now
                            let nextInteractive, eff = Interaction.advancePlayback state interactive
                            interactive <- nextInteractive
                            handleInteractiveEffect eff

                    if keepRunning && not r.ShouldClose && dirty then
                        let struct (curW, curH) = r.FramebufferSize()

                        if curW > 0 && curH > 0 then
                            interactive <-
                                { interactive with
                                    LastValidAspect = float curW / float curH }

                            r.RenderToWindow (Interaction.effectiveCamera state interactive) state.Grid state.Wireframe

                        dirty <- false

                    if
                        keepRunning
                        && not r.ShouldClose
                        && cmdQueue.IsEmpty
                        && Volatile.Read(&watchReloadPending) = 0
                    then
                        if interactive.Playing then
                            let elapsed = float (Stopwatch.GetTimestamp() - lastPlaybackTick) / float Stopwatch.Frequency
                            let remaining = max 0.001 (Interaction.playbackIntervalSeconds - elapsed)
                            r.WaitEventsTimeout(remaining)
                        else
                            r.WaitEvents()
            finally
                Volatile.Write(&stopping, true)
                watcher |> Option.iter (fun w -> (w :> IDisposable).Dispose())

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
    | Ok(Protocol.Sequence seq) ->
        match compileAndLoadModelWithRetries 0 0 seq.Input seq.Load stderr with
        | Error() -> 1
        | Ok model ->
            match Renderer.createHeadless () with
            | Error msg ->
                stderr.WriteLine($"rubberplant-viewer: %s{msg}")
                1
            | Ok renderer ->
                use r = renderer

                executeSequence
                    r
                    seq.Input
                    model
                    seq.Load
                    seq.OutputPrefix
                    seq.From
                    seq.To
                    seq.Step
                    seq.Frames
                    seq.Mode
                    seq.SnapshotSize
                    seq.ContactSheetSize
                    seq.Grid
                    seq.Wireframe
                    (fun bounds aspect -> Protocol.resolveOneShotCamera bounds seq.Camera aspect)
                    stdout
                    stderr
    | Ok(Protocol.View args) -> executeViewWithVisibility true args stdin stdout stderr

[<EntryPoint>]
let main argv =
    ensureUtf8Stdout ()

    let isBatch = argv = [| "batch" |]

    let isView =
        argv.Length > 0
        && (argv[0] = "view" || argv[0].EndsWith(".rp", StringComparison.OrdinalIgnoreCase))

    let stdin =
        if isBatch then
            new StreamReader(Console.OpenStandardInput(), utf8NoBom) :> TextReader
        elif isView then
            if Console.IsInputRedirected then
                new StreamReader(Console.OpenStandardInput(), utf8NoBom) :> TextReader
            else
                try
                    if
                        Console.InputEncoding.CodePage <> Encoding.UTF8.CodePage
                        || Console.InputEncoding.GetPreamble().Length <> 0
                    then
                        Console.InputEncoding <- utf8NoBom
                with _ ->
                    ()

                Console.In
        else
            Console.In

    executeCli (List.ofArray argv) stdin Console.Out Console.Error
