module RubberPlant.Viewer.Tests.InteractionTests

open System
open Expecto
open RubberPlant
open RubberPlant.Geometry
open RubberPlant.Backends
open RubberPlant.Viewer
open RubberPlant.Viewer.Interaction

let private dummyStats () =
    let model, _ = Expect.wantOk (Pipeline.compile "axiom: F;\n") "compile"
    let output = Expect.wantOk (Pipeline.run model) "run"

    Expect.wantOk
        (Stats.analyze output.Derived output.Drawing output.DerivationTrace output.Geometry output.TurtleTrace)
        "stats"

let private makeLoadedSessionWithStage (halfExtents: Vec3) (stage: Protocol.StageInfo) : Protocol.SessionState<string> =
    let bounds = Camera.boundsOfHalfExtents halfExtents

    let loaded: Protocol.LoadedModel<string> =
        { Input = "dir/sub/my-tree.rp"
          Load = Protocol.emptyLoadOptions
          Stage = stage
          Bounds = bounds
          Stats = dummyStats ()
          Scene = "scene" }

    Protocol.applyLoad 1.5 false loaded Protocol.initialSession<string>

let private makeLoadedSession (halfExtents: Vec3) : Protocol.SessionState<string> =
    makeLoadedSessionWithStage halfExtents (Protocol.DiscreteStage(0, 0))

[<Tests>]
let tests =
    testList
        "Interaction"
        [ test "windowTitle formats loaded filename with em-dash and stage, or (no model)" {
              Expect.equal (windowTitle None) "RubberPlant — (no model)" "unloaded title"

              let sDiscrete = makeLoadedSessionWithStage (Vec3.create 1.0 2.0 1.0) (Protocol.DiscreteStage(3, 10))
              Expect.equal (windowTitle sDiscrete.Loaded) "RubberPlant — my-tree.rp [step 3/10]" "discrete stage title"

              let sTimed = makeLoadedSessionWithStage (Vec3.create 1.0 2.0 1.0) (Protocol.TimedStage(1.25, 2.0))
              Expect.equal (windowTitle sTimed.Loaded) "RubberPlant — my-tree.rp [t = 1.25]" "timed stage title"
          }

          test "left-drag orbits azimuth/elevation and clamps elevation to [-90, 90]" {
              let s0 = makeLoadedSession (Vec3.create 1.0 2.0 1.0)
              let i0 = createInitialState 1.5 None
              let cam0 = effectiveCamera s0 i0

              let s1, i1, e1 = handleEvent (MouseButton(Left, true, false, 100.0, 200.0)) s0 i0
              Expect.equal e1 NoEffect "mouse press emits NoEffect"

              let s2, i2, e2 = handleEvent (CursorMove(120.0, 180.0, 800, 600)) s1 i1
              Expect.equal e2 Redraw "left-drag cursor move emits Redraw"
              let cam2 = effectiveCamera s2 i2
              Expect.floatClose Accuracy.veryHigh cam2.AzimuthDeg (cam0.AzimuthDeg - 20.0 * orbitDegPerPixel) "orbit az"
              Expect.floatClose Accuracy.veryHigh cam2.ElevationDeg (cam0.ElevationDeg - 20.0 * orbitDegPerPixel) "orbit el"

              // Dragging far down/up clamps elevation cleanly to +/- 90 degrees.
              let s3, i3, _ = handleEvent (CursorMove(120.0, 2000.0, 800, 600)) s2 i2
              Expect.equal (effectiveCamera s3 i3).ElevationDeg 90.0 "clamped +90"

              let s4, i4, _ = handleEvent (CursorMove(120.0, -2000.0, 800, 600)) s3 i3
              Expect.equal (effectiveCamera s4 i4).ElevationDeg -90.0 "clamped -90"

              let _, _, eRel = handleEvent (MouseButton(Left, false, false, 120.0, -2000.0)) s4 i4
              Expect.equal eRel NoEffect "mouse release emits NoEffect"
          }

          test "right-drag, middle-drag, and Shift+left-drag pan 1:1 on target plane" {
              let s0 = makeLoadedSession (Vec3.create 1.0 2.0 1.0)
              let i0 = createInitialState 1.5 None
              let cam0 = effectiveCamera s0 i0
              let winH = 600
              let expectedScale = 2.0 * Builtins.tanDeg (Camera.fovYDeg * 0.5) / float winH

              for btn, shift in [ Right, false; Middle, false; Left, true ] do
                  let s1, i1, _ = handleEvent (MouseButton(btn, true, shift, 10.0, 20.0)) s0 i0
                  let s2, i2, eff = handleEvent (CursorMove(30.0, 50.0, 800, winH)) s1 i1
                  Expect.equal eff Redraw "pan triggers Redraw"
                  let expected = Camera.pan (-20.0 * expectedScale) (30.0 * expectedScale) cam0
                  let actual = effectiveCamera s2 i2
                  Expect.floatClose Accuracy.veryHigh actual.Target.X expected.Target.X "pan Target.X"
                  Expect.floatClose Accuracy.veryHigh actual.Target.Y expected.Target.Y "pan Target.Y"
                  Expect.floatClose Accuracy.veryHigh actual.Target.Z expected.Target.Z "pan Target.Z"
                  Expect.equal actual.AzimuthDeg cam0.AzimuthDeg "pan preserves az"
                  Expect.equal actual.ElevationDeg cam0.ElevationDeg "pan preserves el"
          }

          test "hovering without a held button or zero delta produces NoEffect" {
              let s0 = makeLoadedSession (Vec3.create 1.0 2.0 1.0)
              let i0 = createInitialState 1.5 None

              let s1, i1, e1 = handleEvent (CursorMove(50.0, 60.0, 800, 600)) s0 i0
              Expect.equal e1 NoEffect "initial cursor move without drag is NoEffect"
              Expect.equal s1 s0 "session unchanged"

              let s2, i2, e2 = handleEvent (CursorMove(90.0, 120.0, 800, 600)) s1 i1
              Expect.equal e2 NoEffect "subsequent hover without drag is NoEffect"
              Expect.equal i2.Drag.LastCursor (Some(90.0, 120.0)) "cursor tracked"

              let s3, i3, _ = handleEvent (MouseButton(Left, true, false, 90.0, 120.0)) s2 i2
              let _, _, eZero = handleEvent (CursorMove(90.0, 120.0, 800, 600)) s3 i3
              Expect.equal eZero NoEffect "zero delta move while pressed is NoEffect"
          }

          test "scroll zoom applies 0.9^delta and scale-invariant min/max distance bounds" {
              let s0 = makeLoadedSession (Vec3.create 100.0 200.0 100.0)
              let i0 = createInitialState 1.0 None
              let cam0 = effectiveCamera s0 i0
              let struct (minD, maxD) = zoomBounds (effectiveBounds s0)

              let s1, i1, e1 = handleEvent (Scroll 2.0) s0 i0
              Expect.equal e1 Redraw "scroll triggers Redraw"
              Expect.floatClose Accuracy.high (effectiveCamera s1 i1).Distance (cam0.Distance * 0.81) "scroll +2 scales by 0.81"

              let sMin, iMin, _ = handleEvent (Scroll 10000.0) s1 i1
              Expect.equal (effectiveCamera sMin iMin).Distance minD "clamped at scale-invariant minDist"

              let sMax, iMax, _ = handleEvent (Scroll -10000.0) s1 i1
              Expect.equal (effectiveCamera sMax iMax).Distance maxD "clamped at scale-invariant maxDist"

              let _, _, eZero = handleEvent (Scroll 0.0) s0 i0
              Expect.equal eZero NoEffect "scroll 0 is NoEffect"
              let _, _, eNan = handleEvent (Scroll nan) s0 i0
              Expect.equal eNan NoEffect "scroll NaN is NoEffect"
          }

          test "shortcuts 1-4, F, W, G, R, Esc/Q update camera, toggles, and effects" {
              let s0 = makeLoadedSession (Vec3.create 2.0 5.0 3.0)
              let i0 = createInitialState 1.6 None

              // Pan & orbit away from default first
              let sOrb =
                  { s0 with
                      Camera =
                          Some
                              { Target = Vec3.create 3.0 -2.0 1.0
                                AzimuthDeg = 123.0
                                ElevationDeg = -40.0
                                Distance = 50.0 } }

              for key, expectedView in
                  [ ViewFront, Camera.frontView
                    ViewSide, Camera.sideView
                    ViewIso, Camera.isoView
                    ViewTop, Camera.topView ] do
                  let sk, _, eff = handleEvent (KeyPress key) sOrb i0
                  Expect.equal eff Redraw "view shortcut triggers Redraw"
                  let cam = Option.get sk.Camera
                  Expect.equal cam.Target Vec3.zero "view shortcut resets target"
                  Expect.equal cam.AzimuthDeg expectedView.AzimuthDeg "view shortcut az"
                  Expect.equal cam.ElevationDeg expectedView.ElevationDeg "view shortcut el"
                  Expect.floatClose Accuracy.high cam.Distance (Camera.fitDistance (effectiveBounds sOrb) expectedView.AzimuthDeg expectedView.ElevationDeg 1.6) "view shortcut fit distance"

              let sf, _, ef = handleEvent (KeyPress FitCamera) sOrb i0
              Expect.equal ef Redraw "F triggers Redraw"
              let camF = Option.get sf.Camera
              Expect.equal camF.Target Vec3.zero "F resets target"
              Expect.equal camF.AzimuthDeg 123.0 "F keeps az"
              Expect.equal camF.ElevationDeg -40.0 "F keeps el"

              let sw, _, ew = handleEvent (KeyPress ToggleWireframe) s0 i0
              Expect.equal ew Redraw "W triggers Redraw"
              Expect.isTrue sw.Wireframe "wireframe toggled on"

              let sg, _, eg = handleEvent (KeyPress ToggleGrid) sw i0
              Expect.equal eg Redraw "G triggers Redraw"
              Expect.isFalse sg.Grid "grid toggled off"

              let _, _, er = handleEvent (KeyPress ReloadModel) s0 i0
              Expect.equal er Reload "R on loaded model returns Reload"

              let unloaded = Protocol.initialSession<string>
              let _, _, erUnloaded = handleEvent (KeyPress ReloadModel) unloaded i0
              Expect.equal erUnloaded NoEffect "R on unloaded session returns NoEffect"

              let _, _, ec = handleEvent (KeyPress CloseWindow) s0 i0
              Expect.equal ec Close "Esc/Q returns Close"
          }

          test "minimized (0, 0) resize preserves LastValidAspect and emits NoEffect" {
              let s0 = makeLoadedSession (Vec3.create 1.0 2.0 1.0)
              let i0 = createInitialState (4.0 / 3.0) None

              let _, iMin, eMin = handleEvent (FramebufferResized(0, 0)) s0 i0
              Expect.equal eMin NoEffect "minimized resize is NoEffect"
              Expect.floatClose Accuracy.veryHigh iMin.LastValidAspect (4.0 / 3.0) "minimized preserves aspect"

              let _, iWide, eWide = handleEvent (FramebufferResized(1920, 1080)) s0 iMin
              Expect.equal eWide Redraw "non-zero resize triggers Redraw"
              Expect.floatClose Accuracy.veryHigh iWide.LastValidAspect (1920.0 / 1080.0) "aspect updated"

              let _, _, eRef = handleEvent WindowRefresh s0 iWide
              Expect.equal eRef Redraw "WindowRefresh triggers Redraw"
          }

          test "unloaded preview camera preserves PendingCamera for first load and syncPreviewCamera updates it" {
              let camSpec: Protocol.CameraSpec =
                  { AzimuthDeg = 90.0
                    ElevationDeg = 0.0
                    Distance = Protocol.KeepOrAutoFit }

              let s0 =
                  { Protocol.initialSession<string> with
                      PendingCamera = Some camSpec }

              let i0 = createInitialState 1.5 (Some camSpec)
              let s1, i1, _ = handleEvent (Scroll 2.0) s0 i0
              Expect.equal s1.PendingCamera (Some camSpec) "scroll on preview does not corrupt PendingCamera"
              Expect.notEqual i1.PreviewCamera.Distance i0.PreviewCamera.Distance "preview camera zoomed"

              let s2 =
                  Protocol.applySetCamera
                      1.5
                      { AzimuthDeg = 180.0
                        ElevationDeg = 15.0
                        Distance = Protocol.Fixed 12.0 }
                      s1

              let i2 = syncPreviewCamera s2 i1
              Expect.equal i2.PreviewCamera.AzimuthDeg 180.0 "syncPreviewCamera updates azimuth"
              Expect.equal i2.PreviewCamera.ElevationDeg 15.0 "syncPreviewCamera updates elevation"
              Expect.equal i2.PreviewCamera.Distance 12.0 "syncPreviewCamera updates distance"
          }

          test "Shift+Left pan keeps LeftShiftOnPress until mouse button release" {
              let s0 = makeLoadedSession (Vec3.create 1.0 2.0 1.0)
              let i0 = createInitialState 1.5 None
              let s1, i1, _ = handleEvent (MouseButton(Left, true, true, 10.0, 20.0)) s0 i0
              Expect.equal (activeDragMode i1.Drag) (Some Pan) "Shift+Left starts Pan"

              // Moving with Shift held stays Pan; releasing Left clears LeftShiftOnPress.
              let s2, i2, eff = handleEvent (CursorMove(25.0, 35.0, 800, 600)) s1 i1
              Expect.equal eff Redraw "pan redraws"
              Expect.equal (activeDragMode i2.Drag) (Some Pan) "still Pan while held"

              let _, i3, _ = handleEvent (MouseButton(Left, false, false, 25.0, 35.0)) s2 i2
              Expect.equal (activeDragMode i3.Drag) None "released clears drag mode"
              Expect.isFalse i3.Drag.LeftShiftOnPress "LeftShiftOnPress cleared on release"
          }

          test "discrete stepping shortcuts (Left/Right/Shift/Home/0/End/Space) and playback advance cleanly" {
              let sTarget = makeLoadedSessionWithStage (Vec3.create 1.0 2.0 1.0) (Protocol.DiscreteStage(10, 10))
              let i0 = createInitialState 1.5 None

              // StepBackward by 1 and Shift+StepBackward by 5
              let _, i1, eBack1 = handleEvent (KeyPress(StepBackward false)) sTarget i0
              Expect.equal eBack1 (StepDiscrete 9) "Left steps -1"
              Expect.isFalse i1.Playing "paused"

              let _, _, eBack5 = handleEvent (KeyPress(StepBackward true)) sTarget i1
              Expect.equal eBack5 (StepDiscrete 5) "Shift+Left steps -5"

              // Clamps at 0
              let sAt2 = makeLoadedSessionWithStage (Vec3.create 1.0 2.0 1.0) (Protocol.DiscreteStage(2, 10))
              let _, _, eClamp = handleEvent (KeyPress(StepBackward true)) sAt2 i0
              Expect.equal eClamp (StepDiscrete 0) "Shift+Left clamps >= 0"

              let sAt0 = makeLoadedSessionWithStage (Vec3.create 1.0 2.0 1.0) (Protocol.DiscreteStage(0, 10))
              let _, _, eAt0 = handleEvent (KeyPress(StepBackward false)) sAt0 i0
              Expect.equal eAt0 NoEffect "Left at 0 is NoEffect"

              // StepForward by 1 and Shift+StepForward by 5 (allows stepping past target)
              let _, _, eFwd1 = handleEvent (KeyPress(StepForward false)) sTarget i0
              Expect.equal eFwd1 (StepDiscrete 11) "Right steps +1 past target"
              let _, _, eFwd5 = handleEvent (KeyPress(StepForward true)) sTarget i0
              Expect.equal eFwd5 (StepDiscrete 15) "Shift+Right steps +5"

              // Home/0 and End
              let _, _, eHome = handleEvent (KeyPress JumpStart) sAt2 i0
              Expect.equal eHome (StepDiscrete 0) "Home jumps to 0"
              let _, _, eEnd = handleEvent (KeyPress JumpEnd) sAt2 i0
              Expect.equal eEnd (StepDiscrete 10) "End jumps to target"

              // Space at target restarts from 0 and begins Playing; Space again pauses; advancePlayback stops at target.
              let _, iPlayStart, ePlayStart = handleEvent (KeyPress TogglePlay) sTarget i0
              Expect.isTrue iPlayStart.Playing "Space at target starts Playing"
              Expect.equal ePlayStart (StepDiscrete 0) "Space at target jumps to 0"

              let _, iPaused, ePause = handleEvent (KeyPress TogglePlay) sAt2 iPlayStart
              Expect.isFalse iPaused.Playing "Space pauses playback"
              Expect.equal ePause NoEffect "pause emits NoEffect"

              let _, iPlayMid, ePlayMid = handleEvent (KeyPress TogglePlay) sAt2 iPaused
              Expect.isTrue iPlayMid.Playing "Space below target starts Playing"
              Expect.equal ePlayMid NoEffect "starts from current step"

              let sNearEnd = makeLoadedSessionWithStage (Vec3.create 1.0 2.0 1.0) (Protocol.DiscreteStage(9, 10))
              let iAfterTick, eTick = advancePlayback sNearEnd iPlayMid
              Expect.equal eTick (StepDiscrete 10) "advancePlayback reaches target"
              Expect.isFalse iAfterTick.Playing "advancePlayback pauses cleanly at target"

              // Manual step pauses Playing
              let _, iManualStop, _ = handleEvent (KeyPress(StepBackward false)) sAt2 iPlayMid
              Expect.isFalse iManualStop.Playing "manual step stops playback"
          }

          test "timed stepping shortcuts and playback use max(0.05, target/60) and 0.2x Shift precision" {
              let target = 6.0
              let expectedDt = max 0.05 (target / 60.0) // 0.1
              let sMid = makeLoadedSessionWithStage (Vec3.create 1.0 2.0 1.0) (Protocol.TimedStage(3.0, target))
              let i0 = createInitialState 1.5 None

              let _, _, eBack = handleEvent (KeyPress(StepBackward false)) sMid i0
              match eBack with
              | StepTimed t -> Expect.floatClose Accuracy.veryHigh t (3.0 - expectedDt) "timed backward"
              | other -> failwith $"expected StepTimed, got %A{other}"

              let _, _, eShiftFwd = handleEvent (KeyPress(StepForward true)) sMid i0
              match eShiftFwd with
              | StepTimed t -> Expect.floatClose Accuracy.veryHigh t (3.0 + expectedDt * 0.2) "timed Shift+Right uses 0.2x step"
              | other -> failwith $"expected StepTimed, got %A{other}"

              let _, _, eHome = handleEvent (KeyPress JumpStart) sMid i0
              Expect.equal eHome (StepTimed 0.0) "timed Home jumps to 0.0"

              let _, _, eEnd = handleEvent (KeyPress JumpEnd) sMid i0
              Expect.equal eEnd (StepTimed target) "timed End jumps to target"

              let sAlmost = makeLoadedSessionWithStage (Vec3.create 1.0 2.0 1.0) (Protocol.TimedStage(5.95, target))
              let iPlay = { i0 with Playing = true }
              let iDone, eDone = advancePlayback sAlmost iPlay
              Expect.equal eDone (StepTimed target) "timed playback clamps final tick at target"
              Expect.isFalse iDone.Playing "timed playback pauses at target"
          } ]
