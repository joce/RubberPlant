module RubberPlant.Viewer.Tests.ProtocolTests

open Expecto
open RubberPlant
open RubberPlant.Geometry
open RubberPlant.Backends
open RubberPlant.Viewer
open RubberPlant.Viewer.Protocol

let private sampleStats (source: string) =
    let model, _ = Expect.wantOk (Pipeline.compile source) "compile"
    let output = Expect.wantOk (Pipeline.run model) "run"

    Expect.wantOk
        (Stats.analyze output.Derived output.Drawing output.DerivationTrace output.Geometry output.TurtleTrace)
        "stats"

let private makeLoaded (name: string) (opts: LoadOptions) (half: Vec3) (stats: Stats.Stats) : LoadedModel<string> =
    { Input = name
      Load = opts
      Stage = DiscreteStage(0, 0)
      Bounds = Camera.boundsOfHalfExtents half
      Stats = stats
      Scene = name }

[<Tests>]
let tests =
    testList
        "Protocol"
        [ test "splitLine matches RubberPlant.Cli.Program.splitLine on all edge cases" {
              let cases =
                  [ ""
                    "   \t  "
                    "load tree.rp -n 4"
                    "load \"my trees/fig 1.rp\" --tropism 0,-1,0"
                    "snapshot C:\\temp\\out.png --size 512x256"
                    "load \"\" --seed -42"
                    "camera\t35\t25\tfit"
                    "load \"unclosed quote"
                    "load bad\000char.rp"
                    "\"a b\"\"c d\"" ]

              for line in cases do
                  let viewerRes = Protocol.splitLine line
                  let cliRes = RubberPlant.Cli.Program.splitLine line
                  Expect.equal viewerRes cliRes $"splitLine parity on '%s{line}'"
          }

          test "CLI dispatch handles empty args, help, explicit subcommands, .rp shorthand, and unknown commands" {
              Expect.equal (parseCli []) (Ok(Help 2)) "empty argv -> Help 2"
              Expect.equal (parseCli [ "-h" ]) (Ok(Help 0)) "-h -> Help 0"
              Expect.equal (parseCli [ "--help" ]) (Ok(Help 0)) "--help -> Help 0"
              Expect.equal (parseCli [ "batch" ]) (Ok Batch) "batch ok"
              Expect.isError (parseCli [ "batch"; "extra" ]) "batch rejects arguments"
              Expect.isError (parseCli [ "snaphsot"; "a.rp" ]) "unknown command rejected"

              // Shorthand .rp (case-insensitive) dispatches to view
              match Expect.wantOk (parseCli [ "examples/tree.RP"; "--wireframe"; "--watch"; "-n"; "3" ]) ".rp shorthand" with
              | View v ->
                  Expect.equal v.Input (Some "examples/tree.RP") "input"
                  Expect.equal v.Load.Derivations (Some 3) "load option"
                  Expect.isTrue v.Wireframe "wireframe"
                  Expect.isTrue v.Grid "grid default true"
                  Expect.isTrue v.Watch "watch flag"
                  Expect.equal v.WindowSize (1024, 768) "default window size"
              | other -> failwith $"expected View, got %A{other}"

              // Bare view without file is allowed with viewer options, rejected with load options.
              match Expect.wantOk (parseCli [ "view"; "--size"; "800x600"; "--no-grid"; "--watch" ]) "bare view" with
              | View v ->
                  Expect.equal v.Input None "no file"
                  Expect.equal v.WindowSize (800, 600) "window size"
                  Expect.isFalse v.Grid "no-grid"
                  Expect.isTrue v.Watch "bare view watch"
              | other -> failwith $"expected View, got %A{other}"

              match Expect.wantOk (parseCli [ "view"; "a.rp" ]) "default view" with
              | View v -> Expect.isFalse v.Watch "watch defaults to false"
              | other -> failwith $"expected View, got %A{other}"

              Expect.isError (parseCli [ "view"; "-n"; "2" ]) "load options without file rejected"
              Expect.isError (parseCli [ "view"; "--sides"; "8" ]) "mesh load option without file rejected"
              Expect.isError (parseCli [ "view"; "a.rp"; "-o"; "out.png" ]) "-o rejected on view"
              Expect.isError (parseCli [ "view"; "a.rp"; "--stats" ]) "--stats rejected on view"
              Expect.isError (parseCli [ "contact-sheet"; "a.rp"; "-o"; "c.png"; "--watch" ]) "--watch rejected on contact-sheet"
              Expect.isError (parseCli [ "snapshot"; "a.rp"; "-o"; "s.png"; "--watch" ]) "--watch rejected on snapshot"
              Expect.isError (parseCli [ "silhouette"; "a.rp"; "-o"; "m.png"; "--watch" ]) "--watch rejected on silhouette"
              Expect.isError (parseCli [ "batch"; "--watch" ]) "--watch rejected on batch"
          }

          test "CLI strict flag scoping for contact-sheet, snapshot, and silhouette" {
              // Required -o / --output and input file
              Expect.isError (parseCli [ "contact-sheet"; "a.rp" ]) "contact-sheet requires -o"
              Expect.isError (parseCli [ "snapshot"; "-o"; "out.png" ]) "snapshot requires input"
              Expect.isError (parseCli [ "silhouette"; "a.rp" ]) "silhouette requires -o"

              // --pretty requires --stats or --stats-out
              Expect.isError (parseCli [ "snapshot"; "a.rp"; "-o"; "out.png"; "--pretty" ]) "--pretty alone rejected"

              Expect.isOk
                  (parseCli [ "snapshot"; "a.rp"; "-o"; "out.png"; "--stats"; "--pretty" ])
                  "--pretty with --stats"

              Expect.isOk
                  (parseCli [ "snapshot"; "a.rp"; "-o"; "out.png"; "--stats-out"; "s.json"; "--pretty" ])
                  "--pretty with --stats-out"

              // --wireframe / --no-grid rejected on silhouette, allowed on contact-sheet & snapshot
              Expect.isError
                  (parseCli [ "silhouette"; "a.rp"; "-o"; "m.png"; "--wireframe" ])
                  "silhouette rejects --wireframe"

              Expect.isError
                  (parseCli [ "silhouette"; "a.rp"; "-o"; "m.png"; "--no-grid" ])
                  "silhouette rejects --no-grid"

              Expect.isOk
                  (parseCli [ "contact-sheet"; "a.rp"; "-o"; "c.png"; "--wireframe"; "--no-grid" ])
                  "contact-sheet accepts --wireframe and --no-grid"

              // --camera rejected on contact-sheet, accepted on snapshot & silhouette
              Expect.isError
                  (parseCli [ "contact-sheet"; "a.rp"; "-o"; "c.png"; "--camera"; "0,0" ])
                  "contact-sheet rejects --camera"

              match Expect.wantOk (parseCli [ "silhouette"; "a.rp"; "-o"; "m.png"; "--camera"; "90,0,12.5"; "--size"; "301" ]) "silhouette ok" with
              | Silhouette s ->
                  Expect.equal s.Size (301, 301) "single int size means NxN"
                  Expect.equal s.Camera (Some { AzimuthDeg = 90.0; ElevationDeg = 0.0; Distance = Fixed 12.5 }) "camera"
              | other -> failwith $"expected Silhouette, got %A{other}"
          }

          test "domain validation for camera, size, contact-sheet size, and load options" {
              // Elevation bounds [-90, 90] and distance > 0
              Expect.isOk (parseCli [ "snapshot"; "a.rp"; "-o"; "o.png"; "--camera"; "-360,-90" ]) "el = -90 ok"
              Expect.isOk (parseCli [ "snapshot"; "a.rp"; "-o"; "o.png"; "--camera"; "720,90,fit" ]) "el = 90, fit ok"
              Expect.isError (parseCli [ "snapshot"; "a.rp"; "-o"; "o.png"; "--camera"; "0,90.1" ]) "el > 90 rejected"
              Expect.isError (parseCli [ "snapshot"; "a.rp"; "-o"; "o.png"; "--camera"; "0,-90.1" ]) "el < -90 rejected"
              Expect.isError (parseCli [ "snapshot"; "a.rp"; "-o"; "o.png"; "--camera"; "0,0,0" ]) "dist = 0 rejected"
              Expect.isError (parseCli [ "snapshot"; "a.rp"; "-o"; "o.png"; "--camera"; "0,0,-5" ]) "dist < 0 rejected"
              Expect.isError (parseCli [ "snapshot"; "a.rp"; "-o"; "o.png"; "--camera"; "NaN,0" ]) "NaN az rejected"
              Expect.isError (parseCli [ "snapshot"; "a.rp"; "-o"; "o.png"; "--camera"; "0,0,Infinity" ]) "Inf dist rejected"

              // Contact-sheet --size must be even in [128, 8192]
              Expect.isOk (parseCli [ "contact-sheet"; "a.rp"; "-o"; "c.png"; "--size"; "128" ]) "128 ok"
              Expect.isOk (parseCli [ "contact-sheet"; "a.rp"; "-o"; "c.png"; "--size"; "8192" ]) "8192 ok"
              Expect.isError (parseCli [ "contact-sheet"; "a.rp"; "-o"; "c.png"; "--size"; "126" ]) "126 too small"
              Expect.isError (parseCli [ "contact-sheet"; "a.rp"; "-o"; "c.png"; "--size"; "129" ]) "129 odd rejected"
              Expect.isError (parseCli [ "contact-sheet"; "a.rp"; "-o"; "c.png"; "--size"; "8194" ]) "8194 too large"
              Expect.isError (parseCli [ "contact-sheet"; "a.rp"; "-o"; "c.png"; "--size"; "512x512" ]) "WxH rejected on contact-sheet"

              // Snapshot --size in [1, 8192]
              Expect.isOk (parseCli [ "snapshot"; "a.rp"; "-o"; "o.png"; "--size"; "1x8192" ]) "1x8192 ok"
              Expect.isError (parseCli [ "snapshot"; "a.rp"; "-o"; "o.png"; "--size"; "0" ]) "0 rejected"
              Expect.isError (parseCli [ "snapshot"; "a.rp"; "-o"; "o.png"; "--size"; "8193" ]) "8193 rejected"
              Expect.isError (parseCli [ "snapshot"; "a.rp"; "-o"; "o.png"; "--size"; "100x0" ]) "h=0 rejected"

              // Load options parity
              Expect.isOk
                  (parseCli
                      [ "snapshot"
                        "a.rp"
                        "-o"
                        "o.png"
                        "-n"
                        "4"
                        "--angle"
                        "22.5"
                        "--step"
                        "1.5"
                        "--width"
                        "0"
                        "--taper"
                        "100"
                        "--tropism"
                        "0,-1,0"
                        "--elasticity"
                        "-0.2"
                        "--seed"
                        "9007199254740991"
                        "--sides"
                        "3"
                        "--surface-div"
                        "5"
                        "--width-scale"
                        "auto"
                        "--max-modules"
                        "unlimited"
                        "--max-primitives"
                        "5000" ])
                  "all valid load options"

              Expect.isError (parseCli [ "snapshot"; "a.rp"; "-o"; "o.png"; "--sides"; "2" ]) "sides < 3"
              Expect.isError (parseCli [ "snapshot"; "a.rp"; "-o"; "o.png"; "--surface-div"; "0" ]) "surface-div < 1"
              Expect.isError (parseCli [ "snapshot"; "a.rp"; "-o"; "o.png"; "--surface-div" ]) "missing value for --surface-div"
              Expect.isError (parseCli [ "snapshot"; "a.rp"; "-o"; "o.png"; "--width"; "-0.1" ]) "width < 0"
              Expect.isError (parseCli [ "snapshot"; "a.rp"; "-o"; "o.png"; "--taper"; "101" ]) "taper > 100"
              Expect.isError (parseCli [ "snapshot"; "a.rp"; "-o"; "o.png"; "--tropism"; "0,0,0" ]) "zero tropism"
              Expect.isError (parseCli [ "snapshot"; "a.rp"; "-o"; "o.png"; "--seed"; "9007199254740993" ]) "seed > 2^53-1"
              Expect.isError (parseCli [ "snapshot"; "a.rp"; "-o"; "o.png"; "--seed"; "9007199254740992" ]) "seed = 2^53"
              Expect.isOk (parseCli [ "snapshot"; "a.rp"; "-o"; "o.png"; "--seed"; "-9007199254740991" ]) "seed = -(2^53-1)"
              Expect.isError (parseCli [ "snapshot"; "a.rp"; "-o"; "o.png"; "--seed"; "-9007199254740992" ]) "seed = -2^53"
              Expect.isError (parseCli [ "snapshot"; "a.rp"; "-o"; "o.png"; "--width-scale"; "0" ]) "width-scale <= 0"
              Expect.isError (parseCli [ "snapshot"; "a.rp"; "-o"; "o.png"; "--max-modules"; "0" ]) "max-modules < 1"
              Expect.isError (parseCli [ "snapshot"; "a.rp"; "-o" ]) "missing value for -o"
              Expect.isError (parseCli [ "snapshot"; "a.rp"; "-o"; "" ]) "empty output path"
              Expect.isError (parseCli [ "view"; "--camera" ]) "missing value for --camera"
              Expect.isError (parseCli [ "snapshot"; "a.rp"; "-o"; "o.png"; "-n" ]) "missing value for -n"
              let errCam = Expect.wantError (parseCli [ "contact-sheet"; "a.rp"; "-o"; "c.png"; "--camera" ]) "contact-sheet --camera"
              Expect.stringContains errCam "contact-sheet" "reports inapplicable option before missing value"
          }

          test "batch command parser enforces strict protocol rules and parses all valid batch commands" {
              Expect.isError (parseBatchLine "") "blank line -> missing command"
              Expect.isError (parseBatchLine "   ") "whitespace line -> missing command"
              Expect.isError (parseBatchLine "batch") "batch rejected inside batch"
              Expect.isError (parseBatchLine "view a.rp") "view rejected inside batch"
              Expect.isError (parseBatchLine "snapshot out.png --wireframe") "--wireframe rejected in batch snapshot"
              Expect.isError (parseBatchLine "snapshot out.png --no-grid") "--no-grid rejected in batch snapshot"
              Expect.isError (parseBatchLine "snapshot out.png --watch") "--watch rejected in batch snapshot"
              Expect.isError (parseBatchLine "load a.rp --watch") "--watch rejected in batch load"
              Expect.isError (parseBatchLine "reload --watch") "--watch rejected in batch reload"
              Expect.isError (parseBatchLine "contact-sheet out.png --no-grid") "--no-grid rejected in batch contact-sheet"
              Expect.isError (parseBatchLine "silhouette out.png --pretty") "--pretty without stats rejected"
              Expect.isError (parseBatchLine "wireframe maybe") "wireframe expects on|off"
              Expect.isError (parseBatchLine "grid 1") "grid expects on|off"
              Expect.isError (parseBatchLine "fit extra") "fit takes no args"
              Expect.isError (parseBatchLine "quit now") "quit takes no args"
              Expect.isError (parseBatchLine "camera 10") "camera needs at least 2 args"
              Expect.isError (parseBatchLine "camera 10 95") "camera el out of range"
              Expect.isError (parseBatchLine "size 0") "size 0 rejected"

              let errReload = Expect.wantError (parseBatchLine "reload --keep-camera extra") "reload extra arg"
              Expect.stringContains errReload "unexpected argument" "reload extra arg reports unexpected argument"
              let errStats = Expect.wantError (parseBatchLine "stats --pretty extra") "stats extra arg"
              Expect.stringContains errStats "unexpected argument" "stats extra arg reports unexpected argument"

              Expect.equal (parseBatchLine "wireframe on") (Ok(SetWireframe true)) "wireframe on"
              Expect.equal (parseBatchLine "grid off") (Ok(SetGrid false)) "grid off"
              Expect.equal (parseBatchLine "size 640 480") (Ok(SetSize(640, 480))) "size w h"
              Expect.equal (parseBatchLine "size 301") (Ok(SetSize(301, 301))) "size N = NxN"
              Expect.equal (parseBatchLine "reload --keep-camera") (Ok(Reload true)) "reload --keep-camera"
              Expect.equal (parseBatchLine "fit") (Ok Fit) "fit"
              Expect.equal (parseBatchLine "stats --pretty") (Ok(BatchStats true)) "stats --pretty"
              Expect.equal (parseBatchLine "quit") (Ok Quit) "quit"

              match Expect.wantOk (parseBatchLine "load \"my tree.rp\" -n 3 --seed 7 --keep-camera") "batch load" with
              | Load(path, opts, keepCam) ->
                  Expect.equal path "my tree.rp" "quoted load path"
                  Expect.equal opts.Derivations (Some 3) "load -n"
                  Expect.equal opts.Seed (Some 7L) "load --seed"
                  Expect.isTrue keepCam "load --keep-camera"
              | other -> failwith $"expected Load, got %A{other}"

              Expect.equal
                  (parseBatchLine "camera -45 -30 10.5")
                  (Ok(SetCamera { AzimuthDeg = -45.0; ElevationDeg = -30.0; Distance = Fixed 10.5 }))
                  "batch camera negative angles + dist"

              Expect.equal
                  (parseBatchLine "camera 0 90 fit")
                  (Ok(SetCamera { AzimuthDeg = 0.0; ElevationDeg = 90.0; Distance = AutoFit }))
                  "batch camera fit"

              match Expect.wantOk (parseBatchLine "snapshot out.png --camera 35,25,fit --size 301x200 --stats --stats-out s.json --pretty") "batch snapshot" with
              | BatchSnapshot s ->
                  Expect.equal s.Output "out.png" "snapshot out"
                  Expect.equal s.Size (Some(301, 200)) "snapshot size"
                  Expect.equal s.Camera (Some { AzimuthDeg = 35.0; ElevationDeg = 25.0; Distance = AutoFit }) "snapshot camera"
                  Expect.equal s.Stats { PrintStdout = true; OutPath = Some "s.json"; Pretty = true } "snapshot stats"
              | other -> failwith $"expected BatchSnapshot, got %A{other}"

              match Expect.wantOk (parseBatchLine "contact-sheet sheet.png --size 512 --stats") "batch contact-sheet" with
              | BatchContactSheet c ->
                  Expect.equal c.Output "sheet.png" "sheet out"
                  Expect.equal c.Size (Some 512) "sheet size"
                  Expect.isTrue c.Stats.PrintStdout "sheet stats"
              | other -> failwith $"expected BatchContactSheet, got %A{other}"

              match Expect.wantOk (parseBatchLine "silhouette mask.png --camera 90,0 --size 256") "batch silhouette" with
              | BatchSilhouette m ->
                  Expect.equal m.Output "mask.png" "mask out"
                  Expect.equal m.Size (Some(256, 256)) "mask size"
                  Expect.equal m.Camera (Some { AzimuthDeg = 90.0; ElevationDeg = 0.0; Distance = KeepOrAutoFit }) "mask camera"
              | other -> failwith $"expected BatchSilhouette, got %A{other}"
          }

          test "LoadOptions conversion helpers and one-shot / render camera resolution" {
              let opts =
                  { emptyLoadOptions with
                      Derivations = Some 5
                      Angle = Some 22.5
                      Step = Some 2.0
                      Width = Some 0.5
                      Taper = Some 15.0
                      Tropism = Some(Vec3.create 1.0 0.0 0.0)
                      Elasticity = Some 0.4
                      Seed = Some 99L
                      Sides = Some 16
                      SurfaceDiv = Some 8
                      WidthScale = Some(Some 0.05)
                      MaxModules = Some None
                      MaxPrimitives = Some(Some 1000) }

              let s = applySettingsOverrides opts Model.defaultSettings
              Expect.equal s.Derivations 5 "derivations"
              Expect.equal s.Angle 22.5 "angle"
              Expect.equal s.Step 2.0 "step"
              Expect.equal s.Width 0.5 "width"
              Expect.equal s.Taper 15.0 "taper"
              Expect.equal s.Tropism (Vec3.create 1.0 0.0 0.0) "tropism"
              Expect.equal s.Elasticity 0.4 "elasticity"
              Expect.equal s.Seed 99L "seed"

              let mo = toMeshOptions opts
              Expect.equal mo.Sides 16 "sides"
              Expect.equal mo.SurfaceDiv (Some 8) "surfaceDiv"
              Expect.equal mo.WidthScale (Some 0.05) "widthScale"

              let settingsWithScale = { Model.defaultSettings with WidthScale = Some 1.0 }
              let moFallback = toMeshOptionsWithSettings settingsWithScale emptyLoadOptions
              Expect.equal moFallback.WidthScale (Some 1.0) "falls back to model.Settings.WidthScale"
              let moAuto = toMeshOptionsWithSettings settingsWithScale { emptyLoadOptions with WidthScale = Some None }
              Expect.equal moAuto.WidthScale None "--width-scale auto overrides settings.WidthScale"
              let moExplicit = toMeshOptionsWithSettings settingsWithScale { emptyLoadOptions with WidthScale = Some(Some 0.25) }
              Expect.equal moExplicit.WidthScale (Some 0.25) "--width-scale <k> overrides settings.WidthScale"

              let lim = toLimits opts
              Expect.equal lim.MaxModules None "maxModules unlimited"
              Expect.equal lim.MaxPrimitives (Some 1000) "maxPrimitives"

              let b = Camera.boundsOfHalfExtents (Vec3.create 1.0 2.0 1.0)
              let cDefault = resolveOneShotCamera b None 1.0
              Expect.equal cDefault.AzimuthDeg Camera.defaultAzimuthDeg "one-shot default az"
              let cKeep = resolveOneShotCamera b (Some { AzimuthDeg = 90.0; ElevationDeg = 0.0; Distance = KeepOrAutoFit }) 1.0
              let cFit = resolveOneShotCamera b (Some { AzimuthDeg = 90.0; ElevationDeg = 0.0; Distance = AutoFit }) 1.0
              Expect.equal cKeep cFit "one-shot omitted dist auto-fits"
              let cFixed = resolveOneShotCamera b (Some { AzimuthDeg = 90.0; ElevationDeg = 0.0; Distance = Fixed 15.0 }) 1.0
              Expect.equal cFixed.Distance 15.0 "one-shot fixed dist"
          }

          test "SessionState enforces pre-load errors, first-load camera inheritance, --keep-camera, and non-mutating renders" {
              let statsA = sampleStats "axiom: F;\n"
              let statsB = sampleStats "axiom: F F;\n"
              let modelA = makeLoaded "a.rp" emptyLoadOptions (Vec3.create 1.0 2.0 1.0) statsA
              let modelB = makeLoaded "b.rp" { emptyLoadOptions with Derivations = Some 2 } (Vec3.create 5.0 5.0 5.0) statsB

              // Fresh session: pre-load errors on commands requiring a model.
              let s0 = initialSession<string>
              Expect.isError (requireLoaded s0) "requireLoaded fails before load"
              Expect.isError (applyFit 1.0 s0) "fit fails before load"

              match tryReload (fun _ _ -> Ok modelA) 1.0 false s0 with
              | Error(Choice1Of2 msg) -> Expect.stringContains msg "no model loaded" "reload before load"
              | other -> failwith $"expected pre-load error, got %A{other}"

              // Pre-load settings (camera, size, wireframe, grid) are allowed; chaining preserves fixed dist unless overridden.
              let s1 =
                  s0
                  |> applySetSize 800 400
                  |> applySetWireframe true
                  |> applySetGrid false
                  |> applySetCamera 2.0 { AzimuthDeg = 45.0; ElevationDeg = 5.0; Distance = Fixed 12.0 }
                  |> applySetCamera 2.0 { AzimuthDeg = 90.0; ElevationDeg = 10.0; Distance = KeepOrAutoFit }

              let s2Fixed = applyLoad (sessionAspect s1) false modelA s1
              Expect.equal (Expect.wantSome s2Fixed.Camera "cam").Distance 12.0 "chained pre-load camera keeps fixed dist"

              let s1Fit = applySetCamera 2.0 { AzimuthDeg = 90.0; ElevationDeg = 10.0; Distance = AutoFit } s1
              let s2 = applyLoad (sessionAspect s1Fit) false modelA s1Fit
              let cam2 = Expect.wantSome s2.Camera "camera set after first load"
              Expect.equal cam2.AzimuthDeg 90.0 "first load inherits pre-load azimuth"
              Expect.equal cam2.ElevationDeg 10.0 "first load inherits pre-load elevation"
              Expect.floatClose Accuracy.high cam2.Distance (Camera.fitDistance modelA.Bounds 90.0 10.0 2.0) "auto-fitted at aspect 2.0"

              // Non-mutating render camera override does not modify session camera; AutoFit resets target, Fixed preserves it.
              let overrideCam =
                  resolveRenderCamera
                      s2
                      modelA.Bounds
                      (Some { AzimuthDeg = 0.0; ElevationDeg = 0.0; Distance = KeepOrAutoFit })
                      1.0

              Expect.equal overrideCam.AzimuthDeg 0.0 "override az"
              Expect.equal overrideCam.Distance cam2.Distance "omitted dist keeps session distance"
              Expect.equal s2.Camera (Some cam2) "session state immutable"

              let panned = { s2 with Camera = Some { cam2 with Target = Vec3.create 1.0 2.0 3.0 } }
              let fitOverride =
                  resolveRenderCamera panned modelA.Bounds (Some { AzimuthDeg = 0.0; ElevationDeg = 0.0; Distance = AutoFit }) 1.0
              Expect.equal fitOverride.Target Vec3.zero "AutoFit override resets target"

              // Explicit `camera <az> <el> <dist>` preserves panned target; `fit` resets target to 0.
              let s3 =
                  applySetCamera 2.0 { AzimuthDeg = 180.0; ElevationDeg = 0.0; Distance = Fixed 42.0 } panned

              let cam3 = Expect.wantSome s3.Camera "cam3"
              Expect.equal cam3.Target (Vec3.create 1.0 2.0 3.0) "fixed dist preserves pan target"
              Expect.equal cam3.Distance 42.0 "fixed dist applied"

              // Second load WITH --keep-camera preserves (az, el, dist, target).
              let s4 = applyLoad 2.0 true modelB s3
              Expect.equal s4.Camera (Some cam3) "--keep-camera preserves full camera state"
              Expect.equal (s4.Loaded |> Option.map _.Input) (Some "b.rp") "model updated"

              // Load WITHOUT --keep-camera resets to 3/4 ISO auto-fitted (PendingCamera was consumed on first load).
              let s5 = applyLoad 2.0 false modelB s4
              let cam5 = Expect.wantSome s5.Camera "cam5"
              Expect.equal cam5.Target Vec3.zero "target reset"
              Expect.equal cam5.AzimuthDeg Camera.defaultAzimuthDeg "default 3/4 ISO az"
              Expect.equal cam5.ElevationDeg Camera.defaultElevationDeg "default 3/4 ISO el"

              // `fit` resets pan target and re-fits at current angles.
              let s5Panned = { s5 with Camera = Some { cam5 with Target = Vec3.create 3.0 0.0 0.0; Distance = 99.0 } }
              let s6 = Expect.wantOk (applyFit 2.0 s5Panned) "fit succeeds"
              let cam6 = Expect.wantSome s6.Camera "cam6"
              Expect.equal cam6.Target Vec3.zero "fit resets target"
              Expect.floatClose Accuracy.high cam6.Distance (Camera.fitDistance modelB.Bounds cam5.AzimuthDeg cam5.ElevationDeg 2.0) "fit recomputes dist"
          }

          test "transactional load and reload preserve previous scene, stats, load spec, and camera on failure" {
              let goodSource = "settings { derivations = 1; }\naxiom: F;\nF -> F[+F]F;\n"
              let explodeSource = "settings { derivations = 30; }\naxiom: F;\nF -> F F;\n"

              let realLoader (src: string) (opts: LoadOptions) : Result<LoadedModel<string>, string> =
                  match Pipeline.compile src with
                  | Error diags -> Error(Diagnostic.format "mem.rp" diags.Head)
                  | Ok(model, _) ->
                      let model = { model with Settings = applySettingsOverrides opts model.Settings }
                      let limits = toLimits opts

                      match Pipeline.runWith limits model with
                      | Error diags -> Error(Diagnostic.format "mem.rp" diags.Head)
                      | Ok output ->
                          match
                              Stats.analyze
                                  output.Derived
                                  output.Drawing
                                  output.DerivationTrace
                                  output.Geometry
                                  output.TurtleTrace
                          with
                          | Error diags -> Error(Diagnostic.format "mem.rp" diags.Head)
                          | Ok stats ->
                              match Mesh.build (toMeshOptions opts) output.Geometry |> Result.bind Scene.fromMesh with
                              | Error msg -> Error msg
                              | Ok sc ->
                                  Ok
                                      { Input = src
                                        Load = opts
                                        Stage = DiscreteStage(0, 0)
                                        Bounds = sc.Bounds
                                        Stats = stats
                                        Scene = "loaded" }

              let s0 = initialSession<string>
              let optsGood = { emptyLoadOptions with Derivations = Some 2 }
              let s1 = Expect.wantOk (tryLoad realLoader 1.0 goodSource optsGood false s0) "initial load succeeds"

              // Exploding load hits RP0206 budget failure: Error returned, previous s1 untouched.
              let optsTight = { emptyLoadOptions with MaxModules = Some(Some 100) }
              let err = Expect.wantError (tryLoad realLoader 1.0 explodeSource optsTight false s1) "exploding load fails"
              Expect.stringContains err "RP0206" "module budget diagnostic"

              // Reload with a failing loader also leaves s1 completely intact.
              match tryReload (fun _ _ -> Error "disk disappeared") 1.0 true s1 with
              | Error(Choice2Of2 msg) -> Expect.equal msg "disk disappeared" "reload error propagated"
              | other -> failwith $"expected reload error, got %A{other}"

              // Original state s1 still has cached load spec, stats, and camera intact for subsequent reload.
              let reloaded = Expect.wantOk (tryReload realLoader 1.0 true s1) "subsequent reload recovers"
              Expect.equal reloaded.Loaded.Value.Load optsGood "cached load options preserved"
              Expect.equal reloaded.Loaded.Value.Stats s1.Loaded.Value.Stats "cached stats identical"
              Expect.equal reloaded.Camera s1.Camera "camera preserved"

              // Reload WITHOUT --keep-camera resets a customized camera to 3/4 ISO auto-fit.
              let customCamState =
                  applySetCamera 1.0 { AzimuthDeg = 180.0; ElevationDeg = -20.0; Distance = Fixed 77.0 } s1
              let reloadedReset = Expect.wantOk (tryReload realLoader 1.0 false customCamState) "reload without --keep-camera"
              let resetCam = Expect.wantSome reloadedReset.Camera "resetCam"
              Expect.equal resetCam.AzimuthDeg Camera.defaultAzimuthDeg "reload resets az to 3/4 ISO"
              Expect.equal resetCam.ElevationDeg Camera.defaultElevationDeg "reload resets el to 3/4 ISO"
              Expect.equal resetCam.Target Vec3.zero "reload resets target"
          }

          test "--time option parsing, overrides, and mutual exclusion with -n/--derivations" {
              match Expect.wantOk (parseCli [ "snapshot"; "a.rp"; "-o"; "o.png"; "--time"; "1.25" ]) "snapshot --time" with
              | Snapshot s ->
                  Expect.equal s.Base.Load.Time (Some 1.25) "time parsed"
                  Expect.isTrue (hasAnyLoadOption s.Base.Load) "hasAnyLoadOption includes Time"
                  let settings = applySettingsOverrides s.Base.Load Model.defaultSettings
                  Expect.equal settings.Time (Some 1.25) "applySettingsOverrides sets Time"
              | other -> failwith $"expected Snapshot, got %A{other}"

              Expect.isOk (parseCli [ "view"; "a.rp"; "--time"; "0" ]) "--time 0 ok"
              Expect.isError (parseCli [ "snapshot"; "a.rp"; "-o"; "o.png"; "--time"; "-0.1" ]) "negative --time rejected"
              Expect.isError (parseCli [ "snapshot"; "a.rp"; "-o"; "o.png"; "--time"; "NaN" ]) "NaN --time rejected"
              Expect.isError (parseCli [ "snapshot"; "a.rp"; "-o"; "o.png"; "--time" ]) "missing --time value rejected"
              Expect.isError (parseCli [ "snapshot"; "a.rp"; "-o"; "o.png"; "-n"; "2"; "--time"; "1" ]) "-n and --time mutually exclusive"
              Expect.isError (parseCli [ "view"; "a.rp"; "--derivations"; "2"; "--time"; "1" ]) "--derivations and --time mutually exclusive on view"
              Expect.isError (parseBatchLine "load a.rp -n 2 --time 1") "-n and --time mutually exclusive in batch load"
          }

          test "sequence CLI parsing, batch step/time/sequence parsing, and discrete/timed schedule resolution" {
              // CLI sequence valid forms
              match Expect.wantOk (parseCli [ "sequence"; "a.rp"; "-o"; "frames/f_"; "--from"; "0"; "--to"; "10"; "--step"; "2"; "--size"; "320x240"; "--camera"; "35,25"; "--wireframe"; "--no-grid" ]) "sequence snapshot" with
              | Sequence s ->
                  Expect.equal s.Input "a.rp" "input"
                  Expect.equal s.OutputPrefix "frames/f_" "prefix"
                  Expect.equal s.From 0.0 "from"
                  Expect.equal s.To 10.0 "to"
                  Expect.equal s.Step (Some 2.0) "sequence --step parsed, not LoadOptions.Step"
                  Expect.equal s.Load.Step None "LoadOptions.Step remains None"
                  Expect.equal s.Mode SeqSnapshot "default mode snapshot"
                  Expect.equal s.SnapshotSize (320, 240) "snapshot size"
                  Expect.isTrue s.Wireframe "wireframe"
                  Expect.isFalse s.Grid "no-grid"
              | other -> failwith $"expected Sequence, got %A{other}"

              match Expect.wantOk (parseCli [ "sequence"; "a.rp"; "-o"; "s_"; "--from"; "0"; "--to"; "2.5"; "--frames"; "6"; "--size"; "512"; "--mode"; "contact-sheet" ]) "sequence contact-sheet" with
              | Sequence s ->
                  Expect.equal s.Mode SeqContactSheet "mode contact-sheet"
                  Expect.equal s.Frames (Some 6) "frames"
                  Expect.equal s.ContactSheetSize 512 "contact-sheet size"
              | other -> failwith $"expected Sequence, got %A{other}"

              // CLI sequence errors
              Expect.isError (parseCli [ "sequence"; "a.rp"; "-o"; "f_"; "--to"; "5" ]) "missing --from"
              Expect.isError (parseCli [ "sequence"; "a.rp"; "-o"; "f_"; "--from"; "0" ]) "missing --to"
              Expect.isError (parseCli [ "sequence"; "a.rp"; "-o"; "f_"; "--from"; "5"; "--to"; "2" ]) "from > to"
              Expect.isError (parseCli [ "sequence"; "a.rp"; "-o"; "f_"; "--from"; "-1"; "--to"; "2" ]) "negative from"
              Expect.isError (parseCli [ "sequence"; "a.rp"; "-o"; "f_"; "--from"; "0"; "--to"; "2"; "--step"; "0" ]) "step <= 0"
              Expect.isError (parseCli [ "sequence"; "a.rp"; "-o"; "f_"; "--from"; "0"; "--to"; "2"; "--frames"; "1" ]) "frames < 2"
              Expect.isError (parseCli [ "sequence"; "a.rp"; "-o"; "f_"; "--from"; "0"; "--to"; "2"; "--step"; "1"; "--frames"; "3" ]) "both step and frames"
              Expect.isError (parseCli [ "sequence"; "a.rp"; "-o"; "f_"; "--from"; "0"; "--to"; "2"; "--stats" ]) "--stats rejected"
              Expect.isError (parseCli [ "sequence"; "a.rp"; "-o"; "f_"; "--from"; "0"; "--to"; "2"; "--stats-out"; "s.json" ]) "--stats-out rejected"
              Expect.isError (parseCli [ "sequence"; "a.rp"; "-o"; "f_"; "--from"; "0"; "--to"; "2"; "--pretty" ]) "--pretty rejected"
              Expect.isError (parseCli [ "sequence"; "a.rp"; "-o"; "f_"; "--from"; "0"; "--to"; "2"; "--watch" ]) "--watch rejected"
              Expect.isError (parseCli [ "sequence"; "a.rp"; "-o"; "f_"; "--from"; "0"; "--to"; "2"; "-n"; "3" ]) "-n rejected"
              Expect.isError (parseCli [ "sequence"; "a.rp"; "-o"; "f_"; "--from"; "0"; "--to"; "2"; "--time"; "1" ]) "--time rejected"
              Expect.isError (parseCli [ "sequence"; "a.rp"; "-o"; "f_"; "--from"; "0"; "--to"; "2"; "--mode"; "contact-sheet"; "--camera"; "0,0" ]) "contact-sheet rejects --camera"
              Expect.isError (parseCli [ "sequence"; "a.rp"; "-o"; "f_"; "--from"; "0"; "--to"; "2"; "--mode"; "contact-sheet"; "--size"; "300x200" ]) "contact-sheet rejects WxH size"
              Expect.isError (parseCli [ "sequence"; "a.rp"; "-o"; "f_"; "--from"; "0"; "--to"; "2"; "--mode"; "silhouette"; "--wireframe" ]) "silhouette rejects --wireframe"
              Expect.isError (parseCli [ "sequence"; "a.rp"; "-o"; "f_"; "--from"; "0"; "--to"; "2"; "--mode"; "silhouette"; "--no-grid" ]) "silhouette rejects --no-grid"
              Expect.isError (parseCli [ "sequence"; "a.rp"; "-o"; "f_"; "--from"; "0"; "--to"; "2"; "--mode"; "bogus" ]) "unknown --mode"

              // batch step / time / sequence
              Expect.equal (parseBatchLine "step 0") (Ok(Step 0)) "step 0"
              Expect.equal (parseBatchLine "step 12") (Ok(Step 12)) "step 12"
              Expect.isError (parseBatchLine "step -1") "negative step"
              Expect.isError (parseBatchLine "step 1.5") "fractional step"
              Expect.isError (parseBatchLine "step") "missing step arg"
              Expect.isError (parseBatchLine "step 1 2") "extra step arg"

              Expect.equal (parseBatchLine "time 0") (Ok(Time 0.0)) "time 0"
              Expect.equal (parseBatchLine "time 1.25") (Ok(Time 1.25)) "time 1.25"
              Expect.isError (parseBatchLine "time -0.1") "negative time"
              Expect.isError (parseBatchLine "time NaN") "NaN time"
              Expect.isError (parseBatchLine "time") "missing time arg"

              match Expect.wantOk (parseBatchLine "sequence frames/f_ --from 0 --to 4 --step 2 --mode silhouette --size 128") "batch sequence" with
              | BatchSequence b ->
                  Expect.equal b.OutputPrefix "frames/f_" "prefix"
                  Expect.equal b.Mode SeqSilhouette "mode"
                  Expect.equal b.SnapshotSize (Some(128, 128)) "size"
              | other -> failwith $"expected BatchSequence, got %A{other}"

              Expect.isError (parseBatchLine "sequence f_ --from 0 --to 2 --wireframe") "batch sequence rejects --wireframe"
              Expect.isError (parseBatchLine "sequence f_ --from 0 --to 2 --no-grid") "batch sequence rejects --no-grid"
              Expect.isError (parseBatchLine "sequence f_ --from 0 --to 2 --stats") "batch sequence rejects --stats"

              // resolveSequenceSchedule discrete vs timed (O(1) descriptors + maxSequenceFrames cap)
              let discDefault = Expect.wantOk (resolveSequenceSchedule false 0.0 4.0 None None) "discrete default step 1"
              Expect.equal discDefault (DiscreteRange(0, 1, 5)) "discrete descriptor"
              Expect.equal (sequenceFrameStages discDefault) [ FrameDiscrete 0; FrameDiscrete 1; FrameDiscrete 2; FrameDiscrete 3; FrameDiscrete 4 ] "discrete step 1 stages"

              let discStep2 = Expect.wantOk (resolveSequenceSchedule false 1.0 5.0 (Some 2.0) None) "discrete step 2"
              Expect.equal (sequenceFrameStages discStep2) [ FrameDiscrete 1; FrameDiscrete 3; FrameDiscrete 5 ] "discrete step 2 stages"

              let discSingle = Expect.wantOk (resolveSequenceSchedule false 3.0 3.0 None None) "discrete from == to"
              Expect.equal (sequenceFrameStages discSingle) [ FrameDiscrete 3 ] "discrete single stage"

              Expect.isError (resolveSequenceSchedule false 0.0 4.0 None (Some 3)) "discrete rejects --frames"
              Expect.isError (resolveSequenceSchedule false 0.0 4.0 (Some 0.5) None) "discrete rejects fractional --step"
              Expect.isError (resolveSequenceSchedule false 0.5 4.0 None None) "discrete rejects fractional --from"
              Expect.isError (resolveSequenceSchedule false 0.0 4.5 None None) "discrete rejects fractional --to"
              Expect.isError (resolveSequenceSchedule false 0.0 2147483647.0 None None) "discrete rejects huge range without OOM"
              Expect.isError (resolveSequenceSchedule false 0.0 (float maxSequenceFrames) None None) "discrete rejects maxSequenceFrames + 1"
              Expect.isOk (resolveSequenceSchedule false 0.0 (float (maxSequenceFrames - 1)) None None) "discrete allows exact maxSequenceFrames"

              Expect.isError (resolveSequenceSchedule true 0.0 1.0 None None) "timed requires --step or --frames"

              let timedStep = Expect.wantOk (resolveSequenceSchedule true 0.0 0.3 (Some 0.1) None) "timed step 0.1"
              Expect.equal (sequenceFrameStages timedStep) [ FrameTimed 0.0; FrameTimed 0.1; FrameTimed 0.2; FrameTimed 0.3 ] "timed step 0.1 hits endpoint cleanly"

              let timedSingle = Expect.wantOk (resolveSequenceSchedule true 1.5 1.5 (Some 0.25) None) "timed from == to"
              Expect.equal (sequenceFrameStages timedSingle) [ FrameTimed 1.5 ] "timed single stage"

              let timedFrames = Expect.wantOk (resolveSequenceSchedule true 1.0 3.0 None (Some 5)) "timed frames 5"
              Expect.equal (sequenceFrameStages timedFrames) [ FrameTimed 1.0; FrameTimed 1.5; FrameTimed 2.0; FrameTimed 2.5; FrameTimed 3.0 ] "timed frames 5 stages"

              Expect.isError (resolveSequenceSchedule true 0.0 1.0 None (Some(maxSequenceFrames + 1))) "timed rejects --frames > maxSequenceFrames"
              Expect.isError (resolveSequenceSchedule true 0.0 1e6 (Some 0.5) None) "timed rejects --step exceeding maxSequenceFrames"
          } ]
