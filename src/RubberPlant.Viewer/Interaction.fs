/// Pure interactive window state machine: orbit/pan/zoom, shortcuts, HiDPI aspect,
/// and window titles (docs/viewer.md §6.4). Graphics-API-free (§2.4).
module RubberPlant.Viewer.Interaction

open System
open System.IO
open RubberPlant

type DragButton =
    | Left
    | Right
    | Middle

type ShortcutKey =
    | ViewFront
    | ViewSide
    | ViewIso
    | ViewTop
    | FitCamera
    | ToggleWireframe
    | ToggleGrid
    | ReloadModel
    | CloseWindow

type WindowEvent =
    | MouseButton of button: DragButton * pressed: bool * shift: bool * x: float * y: float
    | CursorMove of x: float * y: float * winWidth: int * winHeight: int
    | Scroll of deltaY: float
    | KeyPress of ShortcutKey
    | FramebufferResized of fbWidth: int * fbHeight: int
    | WindowRefresh

type InteractiveEffect =
    | NoEffect
    | Redraw
    | Reload
    | Close

type DragMode =
    | Orbit
    | Pan

type DragState =
    { LeftHeld: bool
      RightHeld: bool
      MiddleHeld: bool
      LeftShiftOnPress: bool
      LastCursor: (float * float) option }

type InteractiveState =
    { Drag: DragState
      LastValidAspect: float
      PreviewCamera: Camera.Camera }

let orbitDegPerPixel = 0.35

let emptyDragState =
    { LeftHeld = false
      RightHeld = false
      MiddleHeld = false
      LeftShiftOnPress = false
      LastCursor = None }

let activeDragMode (drag: DragState) : DragMode option =
    if drag.RightHeld || drag.MiddleHeld || (drag.LeftHeld && drag.LeftShiftOnPress) then
        Some Pan
    elif drag.LeftHeld then
        Some Orbit
    else
        None

let windowTitle (loaded: Protocol.LoadedModel<'scene> option) : string =
    match loaded with
    | None -> "RubberPlant — (no model)"
    | Some m ->
        let name = Path.GetFileName m.Input
        let display = if String.IsNullOrEmpty name then m.Input else name
        $"RubberPlant — %s{display}"

let zoomBounds (bounds: Camera.Bounds) : struct (float * float) =
    let r =
        if bounds.IsEmpty || bounds.Radius = 0.0 then
            1.0
        else
            bounds.Radius

    let minDist = max 1e-9 (r * 1e-4)
    let maxDist = min 1e15 (r * 1e4)
    struct (minDist, maxDist)

let createInitialState (aspect: float) (initialCameraSpec: Protocol.CameraSpec option) : InteractiveState =
    let safeAspect =
        if Double.IsFinite aspect && aspect > 0.0 then aspect else 1.0

    { Drag = emptyDragState
      LastValidAspect = safeAspect
      PreviewCamera = Protocol.resolveOneShotCamera Camera.emptyBounds initialCameraSpec safeAspect }

let effectiveBounds (session: Protocol.SessionState<'scene>) : Camera.Bounds =
    match session.Loaded with
    | Some loaded -> loaded.Bounds
    | None -> Camera.emptyBounds

let effectiveCamera (session: Protocol.SessionState<'scene>) (interactive: InteractiveState) : Camera.Camera =
    match session.Camera with
    | Some c -> c
    | None -> interactive.PreviewCamera

let syncPreviewCamera (session: Protocol.SessionState<'scene>) (interactive: InteractiveState) : InteractiveState =
    if session.Loaded.IsNone && session.PendingCamera.IsSome then
        { interactive with
            PreviewCamera =
                Protocol.resolveOneShotCamera Camera.emptyBounds session.PendingCamera interactive.LastValidAspect }
    else
        interactive

let private updateActiveCamera
    (f: Camera.Camera -> Camera.Camera)
    (session: Protocol.SessionState<'scene>)
    (interactive: InteractiveState)
    : Protocol.SessionState<'scene> * InteractiveState =
    match session.Loaded with
    | Some _ ->
        let cam' = f (effectiveCamera session interactive)
        { session with Camera = Some cam' }, interactive
    | None ->
        let cam' = f interactive.PreviewCamera
        session, { interactive with PreviewCamera = cam' }

let handleEvent
    (ev: WindowEvent)
    (session: Protocol.SessionState<'scene>)
    (interactive: InteractiveState)
    : Protocol.SessionState<'scene> * InteractiveState * InteractiveEffect =
    match ev with
    | MouseButton(btn, pressed, shift, x, y) ->
        let d = interactive.Drag

        let drag' =
            match btn with
            | Left ->
                { d with
                    LeftHeld = pressed
                    LeftShiftOnPress = if pressed then shift else false
                    LastCursor = Some(x, y) }
            | Right ->
                { d with
                    RightHeld = pressed
                    LastCursor = Some(x, y) }
            | Middle ->
                { d with
                    MiddleHeld = pressed
                    LastCursor = Some(x, y) }

        session, { interactive with Drag = drag' }, NoEffect

    | CursorMove(x, y, _winWidth, winHeight) ->
        let dx, dy =
            match interactive.Drag.LastCursor with
            | Some(px, py) -> x - px, y - py
            | None -> 0.0, 0.0

        let drag' =
            { interactive.Drag with
                LastCursor = Some(x, y) }

        let interactive' = { interactive with Drag = drag' }

        if dx = 0.0 && dy = 0.0 then
            session, interactive', NoEffect
        else
            match activeDragMode drag' with
            | None -> session, interactive', NoEffect
            | Some Orbit ->
                let session', interactive'' =
                    updateActiveCamera
                        (Camera.orbit (-dx * orbitDegPerPixel) (dy * orbitDegPerPixel))
                        session
                        interactive'

                session', interactive'', Redraw
            | Some Pan ->
                let scale = 2.0 * Builtins.tanDeg (Camera.fovYDeg * 0.5) / float (max 1 winHeight)

                let session', interactive'' =
                    updateActiveCamera (Camera.pan (-dx * scale) (dy * scale)) session interactive'

                session', interactive'', Redraw

    | Scroll deltaY ->
        if not (Double.IsFinite deltaY) || deltaY = 0.0 then
            session, interactive, NoEffect
        else
            let struct (minD, maxD) = zoomBounds (effectiveBounds session)
            let session', interactive' = updateActiveCamera (Camera.zoom deltaY minD maxD) session interactive
            session', interactive', Redraw

    | KeyPress k ->
        match k with
        | ViewFront
        | ViewSide
        | ViewIso
        | ViewTop ->
            let view =
                match k with
                | ViewFront -> Camera.frontView
                | ViewSide -> Camera.sideView
                | ViewIso -> Camera.isoView
                | ViewTop -> Camera.topView
                | _ -> Camera.isoView

            let fitted =
                Camera.fit (effectiveBounds session) view.AzimuthDeg view.ElevationDeg interactive.LastValidAspect

            let session', interactive' = updateActiveCamera (fun _ -> fitted) session interactive
            session', interactive', Redraw

        | FitCamera ->
            let cur = effectiveCamera session interactive

            let fitted =
                Camera.fit (effectiveBounds session) cur.AzimuthDeg cur.ElevationDeg interactive.LastValidAspect

            let session', interactive' = updateActiveCamera (fun _ -> fitted) session interactive
            session', interactive', Redraw

        | ToggleWireframe -> Protocol.applySetWireframe (not session.Wireframe) session, interactive, Redraw
        | ToggleGrid -> Protocol.applySetGrid (not session.Grid) session, interactive, Redraw
        | ReloadModel ->
            if session.Loaded.IsSome then
                session, interactive, Reload
            else
                session, interactive, NoEffect
        | CloseWindow -> session, interactive, Close

    | FramebufferResized(fbWidth, fbHeight) ->
        if fbWidth > 0 && fbHeight > 0 then
            session, { interactive with LastValidAspect = float fbWidth / float fbHeight }, Redraw
        else
            session, interactive, NoEffect

    | WindowRefresh -> session, interactive, Redraw
