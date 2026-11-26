/// OpenGL 3.3 Core offscreen and windowed renderer behind an API-agnostic boundary
/// (docs/viewer.md §2.4, §5, §6.4). Only `GlContext.fs`, `Shaders.fs`, and this module
/// touch `Silk.NET.*`.
module RubberPlant.Viewer.Renderer

#nowarn "9"

open System
open System.Collections.Concurrent
open Microsoft.FSharp.NativeInterop
open Silk.NET.GLFW
open Silk.NET.OpenGL
open RubberPlant.Backends
open RubberPlant.Viewer.GlContext
open RubberPlant.Viewer.Shaders

/// Narrow graphics-API-agnostic offscreen renderer boundary (§2.4).
type IOffscreenRenderer =
    inherit IDisposable
    abstract UploadScene: Scene.MeshScene -> Scene.GridGeometry option -> unit

    abstract RenderShaded:
        width: int -> height: int -> camera: Camera.Camera -> showGrid: bool -> wireframe: bool -> Result<byte[], string>

    abstract RenderSilhouette: width: int -> height: int -> camera: Camera.Camera -> Result<byte[], string>

/// Window creation parameters for interactive mode (§6.2, §6.4).
type WindowOptions =
    { Width: int
      Height: int
      Title: string
      Visible: bool }

/// Narrow graphics-API-agnostic interactive window renderer boundary (§2.4, §6.4).
type IInteractiveRenderer =
    inherit IOffscreenRenderer
    abstract ShouldClose: bool
    abstract RequestClose: unit -> unit
    abstract SetTitle: string -> unit
    abstract FramebufferSize: unit -> struct (int * int)
    abstract WaitEvents: unit -> unit
    abstract WaitEventsTimeout: seconds: float -> unit
    abstract PostWakeup: unit -> unit
    abstract DrainEvents: unit -> Interaction.WindowEvent list
    abstract RenderToWindow: camera: Camera.Camera -> showGrid: bool -> wireframe: bool -> unit

type private ShadedTarget =
    { Width: int
      Height: int
      Samples: int
      MsaaFbo: uint32
      MsaaColorRbo: uint32
      MsaaDepthRbo: uint32
      ResolveFbo: uint32
      ResolveColorRbo: uint32
      SingleDepthRbo: uint32 }

type private SilhouetteTarget =
    { Width: int
      Height: int
      Fbo: uint32
      ColorRbo: uint32
      DepthRbo: uint32 }

let private deleteShadedTarget (gl: GL) (t: ShadedTarget) =
    if t.MsaaFbo <> 0u then gl.DeleteFramebuffer(t.MsaaFbo)
    if t.MsaaColorRbo <> 0u then gl.DeleteRenderbuffer(t.MsaaColorRbo)
    if t.MsaaDepthRbo <> 0u then gl.DeleteRenderbuffer(t.MsaaDepthRbo)
    if t.ResolveFbo <> 0u then gl.DeleteFramebuffer(t.ResolveFbo)
    if t.ResolveColorRbo <> 0u then gl.DeleteRenderbuffer(t.ResolveColorRbo)
    if t.SingleDepthRbo <> 0u then gl.DeleteRenderbuffer(t.SingleDepthRbo)

let private deleteSilhouetteTarget (gl: GL) (t: SilhouetteTarget) =
    if t.Fbo <> 0u then gl.DeleteFramebuffer(t.Fbo)
    if t.ColorRbo <> 0u then gl.DeleteRenderbuffer(t.ColorRbo)
    if t.DepthRbo <> 0u then gl.DeleteRenderbuffer(t.DepthRbo)

let private tryCreateShadedTargetWithFormat
    (gl: GL)
    (w: int)
    (h: int)
    (requestedSamples: int)
    (colorFormat: InternalFormat)
    : Result<ShadedTarget, string> =
    let samples = max 1 requestedSamples

    if samples >= 2 then
        let msaaFbo = gl.GenFramebuffer()
        let msaaColor = gl.GenRenderbuffer()
        let msaaDepth = gl.GenRenderbuffer()
        let resolveFbo = gl.GenFramebuffer()
        let resolveColor = gl.GenRenderbuffer()

        let created =
            { Width = w
              Height = h
              Samples = samples
              MsaaFbo = msaaFbo
              MsaaColorRbo = msaaColor
              MsaaDepthRbo = msaaDepth
              ResolveFbo = resolveFbo
              ResolveColorRbo = resolveColor
              SingleDepthRbo = 0u }

        gl.BindFramebuffer(FramebufferTarget.Framebuffer, msaaFbo)
        gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, msaaColor)
        gl.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, uint32 samples, colorFormat, uint32 w, uint32 h)
        gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, msaaColor)

        gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, msaaDepth)
        gl.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, uint32 samples, InternalFormat.DepthComponent24, uint32 w, uint32 h)
        gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, msaaDepth)

        let msaaStatus = gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer)

        gl.BindFramebuffer(FramebufferTarget.Framebuffer, resolveFbo)
        gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, resolveColor)
        gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, colorFormat, uint32 w, uint32 h)
        gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, resolveColor)

        let resolveStatus = gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer)
        gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0u)
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0u)

        if msaaStatus = GLEnum.FramebufferComplete && resolveStatus = GLEnum.FramebufferComplete then
            Ok created
        else
            deleteShadedTarget gl created
            Error $"MSAA shaded FBO incomplete (msaa=%s{string (int msaaStatus)}, resolve=%s{string (int resolveStatus)})"
    else
        let fbo = gl.GenFramebuffer()
        let colorRbo = gl.GenRenderbuffer()
        let depthRbo = gl.GenRenderbuffer()

        let created =
            { Width = w
              Height = h
              Samples = 1
              MsaaFbo = 0u
              MsaaColorRbo = 0u
              MsaaDepthRbo = 0u
              ResolveFbo = fbo
              ResolveColorRbo = colorRbo
              SingleDepthRbo = depthRbo }

        gl.BindFramebuffer(FramebufferTarget.Framebuffer, fbo)
        gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, colorRbo)
        gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, colorFormat, uint32 w, uint32 h)
        gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, colorRbo)

        gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, depthRbo)
        gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.DepthComponent24, uint32 w, uint32 h)
        gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, depthRbo)

        let status = gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer)
        gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0u)
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0u)

        if status = GLEnum.FramebufferComplete then
            Ok created
        else
            deleteShadedTarget gl created
            Error $"shaded FBO incomplete (status=%s{string (int status)})"

let private createShadedTarget (gl: GL) (w: int) (h: int) (maxSamples: int) : Result<ShadedTarget, string> =
    let samples = min 4 (max 1 maxSamples)

    match tryCreateShadedTargetWithFormat gl w h samples InternalFormat.Rgb8 with
    | Ok t -> Ok t
    | Error _ ->
        match tryCreateShadedTargetWithFormat gl w h samples InternalFormat.Rgba8 with
        | Ok t -> Ok t
        | Error _ when samples >= 2 ->
            // Fall back to 1x non-MSAA if driver rejects multisample FBOs.
            match tryCreateShadedTargetWithFormat gl w h 1 InternalFormat.Rgb8 with
            | Ok t -> Ok t
            | Error _ -> tryCreateShadedTargetWithFormat gl w h 1 InternalFormat.Rgba8
        | Error err -> Error err

let private tryCreateSilhouetteTargetWithFormat
    (gl: GL)
    (w: int)
    (h: int)
    (colorFormat: InternalFormat)
    : Result<SilhouetteTarget, string> =
    let fbo = gl.GenFramebuffer()
    let colorRbo = gl.GenRenderbuffer()
    let depthRbo = gl.GenRenderbuffer()

    let created =
        { Width = w
          Height = h
          Fbo = fbo
          ColorRbo = colorRbo
          DepthRbo = depthRbo }

    gl.BindFramebuffer(FramebufferTarget.Framebuffer, fbo)
    gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, colorRbo)
    gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, colorFormat, uint32 w, uint32 h)
    gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, colorRbo)

    gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, depthRbo)
    gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.DepthComponent24, uint32 w, uint32 h)
    gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, depthRbo)

    let status = gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer)
    gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0u)
    gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0u)

    if status = GLEnum.FramebufferComplete then
        Ok created
    else
        deleteSilhouetteTarget gl created
        Error $"silhouette FBO incomplete (status=%s{string (int status)})"

let private createSilhouetteTarget (gl: GL) (w: int) (h: int) : Result<SilhouetteTarget, string> =
    match tryCreateSilhouetteTargetWithFormat gl w h InternalFormat.R8 with
    | Ok t -> Ok t
    | Error _ ->
        match tryCreateSilhouetteTargetWithFormat gl w h InternalFormat.Rgb8 with
        | Ok t -> Ok t
        | Error _ -> tryCreateSilhouetteTargetWithFormat gl w h InternalFormat.Rgba8

let private setMat4 (gl: GL) (loc: int) (m: float32[]) =
    use p = fixed m
    gl.UniformMatrix4(loc, 1u, false, p)

let private indexByteOffsetPtr (firstIndex: int) : voidptr =
    NativePtr.toVoidPtr (NativePtr.ofNativeInt<byte> (nativeint (int64 firstIndex * 4L)))

type private GlRendererCore(ctx: GlContext, shaders: ShaderSet, isInteractive: bool) =
    let gl = ctx.Gl

    let mutable meshVao = 0u
    let mutable meshVbo = 0u
    let mutable meshEbo = 0u
    let mutable ranges: Scene.DrawRange list = []
    let mutable totalIndices = 0
    let mutable bounds = Camera.emptyBounds

    let mutable gridVao = 0u
    let mutable gridVbo = 0u
    let mutable gridVertexCount = 0
    let mutable gridExtent: float option = None

    let mutable shadedTarget: ShadedTarget option = None
    let mutable silhouetteTarget: SilhouetteTarget option = None
    let mutable disposed = false

    let deleteMeshBuffers () =
        if meshVao <> 0u then
            gl.DeleteVertexArray(meshVao)
            meshVao <- 0u

        if meshVbo <> 0u then
            gl.DeleteBuffer(meshVbo)
            meshVbo <- 0u

        if meshEbo <> 0u then
            gl.DeleteBuffer(meshEbo)
            meshEbo <- 0u

        ranges <- []
        totalIndices <- 0
        bounds <- Camera.emptyBounds

    let deleteGridBuffers () =
        if gridVao <> 0u then
            gl.DeleteVertexArray(gridVao)
            gridVao <- 0u

        if gridVbo <> 0u then
            gl.DeleteBuffer(gridVbo)
            gridVbo <- 0u

        gridVertexCount <- 0
        gridExtent <- None

    let validateSize (w: int) (h: int) (bytesPerPixel: int) =
        if w < 1 || h < 1 then
            Error $"render dimensions must be positive, got %s{string w}x%s{string h}"
        elif w > ctx.MaxRenderbufferSize || h > ctx.MaxRenderbufferSize then
            Error
                $"requested render size %s{string w}x%s{string h} exceeds OpenGL GL_MAX_RENDERBUFFER_SIZE (%s{string ctx.MaxRenderbufferSize})"
        elif int64 w * int64 h * int64 bytesPerPixel > int64 Array.MaxLength then
            Error $"requested render size %s{string w}x%s{string h} exceeds maximum pixel buffer size"
        else
            Ok()

    let ensureShadedTarget (w: int) (h: int) : Result<ShadedTarget, string> =
        match shadedTarget with
        | Some t when t.Width = w && t.Height = h -> Ok t
        | oldOpt ->
            oldOpt |> Option.iter (deleteShadedTarget gl)
            shadedTarget <- None

            match createShadedTarget gl w h ctx.MaxSamples with
            | Ok t ->
                shadedTarget <- Some t
                Ok t
            | Error e -> Error e

    let ensureSilhouetteTarget (w: int) (h: int) : Result<SilhouetteTarget, string> =
        match silhouetteTarget with
        | Some t when t.Width = w && t.Height = h -> Ok t
        | oldOpt ->
            oldOpt |> Option.iter (deleteSilhouetteTarget gl)
            silhouetteTarget <- None

            match createSilhouetteTarget gl w h with
            | Ok t ->
                silhouetteTarget <- Some t
                Ok t
            | Error e -> Error e

    let drawShadedPass (w: int) (h: int) (camera: Camera.Camera) (showGrid: bool) (wireframe: bool) =
        gl.Enable(EnableCap.DepthTest)
        gl.Disable(EnableCap.CullFace)

        // Background #f2f2f0 (§5.2).
        gl.ClearColor(242.0f / 255.0f, 242.0f / 255.0f, 240.0f / 255.0f, 1.0f)
        gl.Clear(ClearBufferMask.ColorBufferBit ||| ClearBufferMask.DepthBufferBit)

        let effectiveGridExtent = if showGrid then gridExtent else None
        let aspect = float w / float h
        let vp = Camera.viewProjectionMatrix bounds camera aspect effectiveGridExtent

        if showGrid && gridVertexCount > 0 && gridVao <> 0u then
            gl.UseProgram(shaders.Line.Program)
            setMat4 gl shaders.Line.ViewProjLoc vp
            gl.BindVertexArray(gridVao)
            gl.DrawArrays(PrimitiveType.Lines, 0, uint32 gridVertexCount)

        if totalIndices > 0 && not ranges.IsEmpty && meshVao <> 0u then
            if wireframe then
                gl.Enable(EnableCap.PolygonOffsetFill)
                gl.PolygonOffset(1.0f, 1.0f)

            let basis = Camera.basis camera
            gl.UseProgram(shaders.Lit.Program)
            setMat4 gl shaders.Lit.ViewProjLoc vp

            gl.Uniform3(
                shaders.Lit.EyePosLoc,
                float32 basis.Pos.X,
                float32 basis.Pos.Y,
                float32 basis.Pos.Z
            )

            gl.BindVertexArray(meshVao)

            for r in ranges do
                let lr, lg, lb = Palette.linear[Palette.entry r.Color]
                gl.Uniform3(shaders.Lit.AlbedoLoc, float32 lr, float32 lg, float32 lb)

                gl.DrawElements(
                    PrimitiveType.Triangles,
                    uint32 r.IndexCount,
                    DrawElementsType.UnsignedInt,
                    indexByteOffsetPtr r.FirstIndex
                )

            if wireframe then
                gl.Disable(EnableCap.PolygonOffsetFill)
                gl.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Line)
                gl.UseProgram(shaders.Flat.Program)
                setMat4 gl shaders.Flat.ViewProjLoc vp
                gl.Uniform3(shaders.Flat.ColorLoc, 48.0f / 255.0f, 48.0f / 255.0f, 48.0f / 255.0f)

                gl.DrawElements(
                    PrimitiveType.Triangles,
                    uint32 totalIndices,
                    DrawElementsType.UnsignedInt,
                    indexByteOffsetPtr 0
                )

                gl.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill)

    let restoreDefaultFramebuffer () =
        gl.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill)
        gl.Disable(EnableCap.PolygonOffsetFill)
        gl.BindVertexArray(0u)
        gl.UseProgram(0u)
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0u)

        if isInteractive then
            let struct (fbW, fbH) = ctx.GetFramebufferSize()

            if fbW > 0 && fbH > 0 then
                gl.Viewport(0, 0, uint32 fbW, uint32 fbH)

    member _.UploadScene (scene: Scene.MeshScene) (grid: Scene.GridGeometry option) =
        if not disposed then
            deleteMeshBuffers ()
            deleteGridBuffers ()

            bounds <- scene.Bounds

            if not scene.IsEmpty && scene.Vertices.Length > 0 && scene.Indices.Length > 0 then
                let vao = gl.GenVertexArray()
                let vbo = gl.GenBuffer()
                let ebo = gl.GenBuffer()
                meshVao <- vao
                meshVbo <- vbo
                meshEbo <- ebo
                ranges <- scene.Ranges
                totalIndices <- scene.Indices.Length

                gl.BindVertexArray(vao)

                gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo)
                use vPtr = fixed scene.Vertices

                gl.BufferData(
                    BufferTargetARB.ArrayBuffer,
                    unativeint (int64 scene.Vertices.Length * 4L),
                    NativePtr.toVoidPtr vPtr,
                    BufferUsageARB.StaticDraw
                )

                gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, ebo)
                use iPtr = fixed scene.Indices

                gl.BufferData(
                    BufferTargetARB.ElementArrayBuffer,
                    unativeint (int64 scene.Indices.Length * 4L),
                    NativePtr.toVoidPtr iPtr,
                    BufferUsageARB.StaticDraw
                )

                gl.EnableVertexAttribArray(0u)
                gl.VertexAttribPointer(0u, 3, VertexAttribPointerType.Float, false, 24u, 0n)
                gl.EnableVertexAttribArray(1u)
                gl.VertexAttribPointer(1u, 3, VertexAttribPointerType.Float, false, 24u, 12n)

                gl.BindVertexArray(0u)
                gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0u)

            match grid with
            | Some g when g.VertexCount > 0 && g.Vertices.Length > 0 ->
                let vao = gl.GenVertexArray()
                let vbo = gl.GenBuffer()
                gridVao <- vao
                gridVbo <- vbo
                gridVertexCount <- g.VertexCount
                gridExtent <- Some g.Extent

                gl.BindVertexArray(vao)
                gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo)
                use gPtr = fixed g.Vertices

                gl.BufferData(
                    BufferTargetARB.ArrayBuffer,
                    unativeint (int64 g.Vertices.Length * 4L),
                    NativePtr.toVoidPtr gPtr,
                    BufferUsageARB.StaticDraw
                )

                gl.EnableVertexAttribArray(0u)
                gl.VertexAttribPointer(0u, 3, VertexAttribPointerType.Float, false, 24u, 0n)
                gl.EnableVertexAttribArray(1u)
                gl.VertexAttribPointer(1u, 3, VertexAttribPointerType.Float, false, 24u, 12n)

                gl.BindVertexArray(0u)
                gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0u)
            | _ -> ()

    member _.RenderShaded (w: int) (h: int) (camera: Camera.Camera) (showGrid: bool) (wireframe: bool) =
        if disposed then
            Error "renderer has been disposed"
        else
            match validateSize w h 3 with
            | Error e -> Error e
            | Ok() ->
                match ensureShadedTarget w h with
                | Error e -> Error e
                | Ok target ->
                    try
                        let drawFbo =
                            if target.Samples >= 2 then target.MsaaFbo else target.ResolveFbo

                        gl.BindFramebuffer(FramebufferTarget.Framebuffer, drawFbo)
                        gl.Viewport(0, 0, uint32 w, uint32 h)

                        if target.Samples >= 2 then
                            gl.Enable(EnableCap.Multisample)
                        else
                            gl.Disable(EnableCap.Multisample)

                        drawShadedPass w h camera showGrid wireframe

                        if target.Samples >= 2 then
                            gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, target.MsaaFbo)
                            gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, target.ResolveFbo)

                            gl.BlitFramebuffer(
                                0,
                                0,
                                w,
                                h,
                                0,
                                0,
                                w,
                                h,
                                ClearBufferMask.ColorBufferBit,
                                BlitFramebufferFilter.Nearest
                            )

                            gl.BindFramebuffer(FramebufferTarget.Framebuffer, target.ResolveFbo)

                        let pixels = Array.zeroCreate<byte> (w * h * 3)
                        gl.PixelStore(PixelStoreParameter.PackAlignment, 1)
                        gl.ReadBuffer(ReadBufferMode.ColorAttachment0)
                        use pPtr = fixed pixels

                        gl.ReadPixels(
                            0,
                            0,
                            uint32 w,
                            uint32 h,
                            PixelFormat.Rgb,
                            PixelType.UnsignedByte,
                            NativePtr.toVoidPtr pPtr
                        )

                        Ok pixels
                    finally
                        restoreDefaultFramebuffer ()

    member _.RenderSilhouette (w: int) (h: int) (camera: Camera.Camera) =
        if disposed then
            Error "renderer has been disposed"
        else
            match validateSize w h 1 with
            | Error e -> Error e
            | Ok() ->
                match ensureSilhouetteTarget w h with
                | Error e -> Error e
                | Ok target ->
                    try
                        gl.BindFramebuffer(FramebufferTarget.Framebuffer, target.Fbo)
                        gl.Viewport(0, 0, uint32 w, uint32 h)
                        gl.Disable(EnableCap.Multisample)
                        gl.Enable(EnableCap.DepthTest)
                        gl.Disable(EnableCap.CullFace)

                        gl.ClearColor(0.0f, 0.0f, 0.0f, 1.0f)
                        gl.Clear(ClearBufferMask.ColorBufferBit ||| ClearBufferMask.DepthBufferBit)

                        if totalIndices > 0 && meshVao <> 0u then
                            let aspect = float w / float h
                            let vp = Camera.viewProjectionMatrix bounds camera aspect None
                            gl.UseProgram(shaders.Flat.Program)
                            setMat4 gl shaders.Flat.ViewProjLoc vp
                            gl.Uniform3(shaders.Flat.ColorLoc, 1.0f, 1.0f, 1.0f)
                            gl.BindVertexArray(meshVao)

                            gl.DrawElements(
                                PrimitiveType.Triangles,
                                uint32 totalIndices,
                                DrawElementsType.UnsignedInt,
                                indexByteOffsetPtr 0
                            )

                        let pixels = Array.zeroCreate<byte> (w * h)
                        gl.PixelStore(PixelStoreParameter.PackAlignment, 1)
                        gl.ReadBuffer(ReadBufferMode.ColorAttachment0)
                        use pPtr = fixed pixels

                        gl.ReadPixels(
                            0,
                            0,
                            uint32 w,
                            uint32 h,
                            PixelFormat.Red,
                            PixelType.UnsignedByte,
                            NativePtr.toVoidPtr pPtr
                        )

                        Ok pixels
                    finally
                        restoreDefaultFramebuffer ()

    member _.RenderToWindow (camera: Camera.Camera) (showGrid: bool) (wireframe: bool) =
        if not disposed then
            let struct (fbW, fbH) = ctx.GetFramebufferSize()

            if fbW > 0 && fbH > 0 then
                try
                    gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0u)
                    gl.Viewport(0, 0, uint32 fbW, uint32 fbH)

                    if ctx.WindowSamples >= 2 then
                        gl.Enable(EnableCap.Multisample)
                    else
                        gl.Disable(EnableCap.Multisample)

                    drawShadedPass fbW fbH camera showGrid wireframe
                finally
                    gl.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill)
                    gl.Disable(EnableCap.PolygonOffsetFill)
                    gl.BindVertexArray(0u)
                    gl.UseProgram(0u)

                ctx.SwapBuffers()

    member _.Dispose() =
        if not disposed then
            disposed <- true

            try
                deleteMeshBuffers ()
                deleteGridBuffers ()
                shadedTarget |> Option.iter (deleteShadedTarget gl)
                shadedTarget <- None
                silhouetteTarget |> Option.iter (deleteSilhouetteTarget gl)
                silhouetteTarget <- None
                (shaders :> IDisposable).Dispose()
            finally
                (ctx :> IDisposable).Dispose()

    interface IOffscreenRenderer with
        member this.UploadScene scene grid = this.UploadScene scene grid
        member this.RenderShaded w h cam showGrid wireframe = this.RenderShaded w h cam showGrid wireframe
        member this.RenderSilhouette w h cam = this.RenderSilhouette w h cam
        member this.Dispose() = this.Dispose()

let private mapShortcut (key: Keys) (shift: bool) : Interaction.ShortcutKey option =
    match key with
    | Keys.Number1
    | Keys.Keypad1 -> Some Interaction.ViewFront
    | Keys.Number2
    | Keys.Keypad2 -> Some Interaction.ViewSide
    | Keys.Number3
    | Keys.Keypad3 -> Some Interaction.ViewIso
    | Keys.Number4
    | Keys.Keypad4 -> Some Interaction.ViewTop
    | Keys.F -> Some Interaction.FitCamera
    | Keys.W -> Some Interaction.ToggleWireframe
    | Keys.G -> Some Interaction.ToggleGrid
    | Keys.R -> Some Interaction.ReloadModel
    | Keys.Escape
    | Keys.Q -> Some Interaction.CloseWindow
    | Keys.Left
    | Keys.LeftBracket -> Some(Interaction.StepBackward shift)
    | Keys.Right
    | Keys.RightBracket -> Some(Interaction.StepForward shift)
    | Keys.Home
    | Keys.Number0
    | Keys.Keypad0 -> Some Interaction.JumpStart
    | Keys.End -> Some Interaction.JumpEnd
    | Keys.Space -> Some Interaction.TogglePlay
    | _ -> None

type private GlInteractiveRenderer(ctx: GlContext, shaders: ShaderSet) =
    let core = new GlRendererCore(ctx, shaders, true)
    let glfw = ctx.GlfwApi
    let window = ctx.WindowHandle
    let events = ConcurrentQueue<Interaction.WindowEvent>()
    let mutable disposed = false

    // Root every callback delegate in instance fields so GC/Native AOT never collects them
    // while GLFW holds the native function pointers.
    let mouseButtonCb =
        GlfwCallbacks.MouseButtonCallback(fun win btn action mods ->
            let buttonOpt =
                match btn with
                | MouseButton.Left -> Some Interaction.Left
                | MouseButton.Right -> Some Interaction.Right
                | MouseButton.Middle -> Some Interaction.Middle
                | _ -> None

            match buttonOpt with
            | Some mapped when action = InputAction.Press || action = InputAction.Release ->
                let pressed = action = InputAction.Press
                let shift = (mods &&& KeyModifiers.Shift) <> enum 0
                let mutable cx = 0.0
                let mutable cy = 0.0
                glfw.GetCursorPos(win, &cx, &cy)
                events.Enqueue(Interaction.MouseButton(mapped, pressed, shift, cx, cy))
            | _ -> ())

    let cursorPosCb =
        GlfwCallbacks.CursorPosCallback(fun win x y ->
            let mutable winW = 0
            let mutable winH = 0
            glfw.GetWindowSize(win, &winW, &winH)
            events.Enqueue(Interaction.CursorMove(x, y, winW, winH)))

    let scrollCb =
        GlfwCallbacks.ScrollCallback(fun _ _offsetX offsetY -> events.Enqueue(Interaction.Scroll offsetY))

    let keyCb =
        GlfwCallbacks.KeyCallback(fun _ key _ action mods ->
            let unsupportedMods =
                mods &&& (KeyModifiers.Control ||| KeyModifiers.Alt ||| KeyModifiers.Super)

            if action = InputAction.Press && unsupportedMods = enum 0 then
                let shift = (mods &&& KeyModifiers.Shift) <> enum 0

                match mapShortcut key shift with
                | Some k -> events.Enqueue(Interaction.KeyPress k)
                | None -> ())

    let fbSizeCb =
        GlfwCallbacks.FramebufferSizeCallback(fun _ w h -> events.Enqueue(Interaction.FramebufferResized(w, h)))

    let refreshCb =
        GlfwCallbacks.WindowRefreshCallback(fun _ -> events.Enqueue(Interaction.WindowRefresh))

    // Referenced in Dispose() so F# emits an instance field rather than eliding constructor locals.
    let rootedCallbacks: Delegate[] =
        [| mouseButtonCb
           cursorPosCb
           scrollCb
           keyCb
           fbSizeCb
           refreshCb |]

    do
        glfw.SetMouseButtonCallback(window, mouseButtonCb) |> ignore
        glfw.SetCursorPosCallback(window, cursorPosCb) |> ignore
        glfw.SetScrollCallback(window, scrollCb) |> ignore
        glfw.SetKeyCallback(window, keyCb) |> ignore
        glfw.SetFramebufferSizeCallback(window, fbSizeCb) |> ignore
        glfw.SetWindowRefreshCallback(window, refreshCb) |> ignore

    interface IInteractiveRenderer with
        member _.UploadScene scene grid = core.UploadScene scene grid
        member _.RenderShaded w h cam showGrid wireframe = core.RenderShaded w h cam showGrid wireframe
        member _.RenderSilhouette w h cam = core.RenderSilhouette w h cam
        member _.ShouldClose = disposed || ctx.ShouldClose()
        member _.RequestClose() = ctx.SetShouldClose(true)
        member _.SetTitle title = ctx.SetTitle(title)
        member _.FramebufferSize() = ctx.GetFramebufferSize()
        member _.WaitEvents() = ctx.WaitEvents()
        member _.WaitEventsTimeout seconds = ctx.WaitEventsTimeout(seconds)
        member _.PostWakeup() = ctx.PostEmptyEvent()

        member _.DrainEvents() =
            let items = ResizeArray<Interaction.WindowEvent>()
            let mutable ev = Unchecked.defaultof<Interaction.WindowEvent>

            while events.TryDequeue(&ev) do
                items.Add(ev)

            List.ofSeq items

        member _.RenderToWindow cam showGrid wireframe =
            if not disposed then
                core.RenderToWindow cam showGrid wireframe

        member _.Dispose() =
            if not disposed then
                disposed <- true

                try
                    glfw.SetMouseButtonCallback(window, null) |> ignore
                    glfw.SetCursorPosCallback(window, null) |> ignore
                    glfw.SetScrollCallback(window, null) |> ignore
                    glfw.SetKeyCallback(window, null) |> ignore
                    glfw.SetFramebufferSizeCallback(window, null) |> ignore
                    glfw.SetWindowRefreshCallback(window, null) |> ignore
                with _ ->
                    ()

                GC.KeepAlive(rootedCallbacks)
                core.Dispose()

/// Creates a headless OpenGL 3.3 Core offscreen renderer (§2.1–§2.4).
let createHeadless () : Result<IOffscreenRenderer, string> =
    match GlContext.createHeadless () with
    | Error e -> Error e
    | Ok ctx ->
        match Shaders.create ctx.Gl with
        | Error e ->
            (ctx :> IDisposable).Dispose()
            Error e
        | Ok shaders -> Ok(new GlRendererCore(ctx, shaders, false) :> IOffscreenRenderer)

/// Creates an interactive OpenGL 3.3 Core window renderer (§2.4, §6.4).
let createWindow (opts: WindowOptions) : Result<IInteractiveRenderer, string> =
    match GlContext.createWindow opts.Width opts.Height opts.Title opts.Visible with
    | Error e -> Error e
    | Ok ctx ->
        match Shaders.create ctx.Gl with
        | Error e ->
            (ctx :> IDisposable).Dispose()
            Error e
        | Ok shaders -> Ok(new GlInteractiveRenderer(ctx, shaders) :> IInteractiveRenderer)
