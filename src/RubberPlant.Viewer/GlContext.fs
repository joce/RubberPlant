/// GLFW window/context bootstrap and OpenGL capability query (docs/viewer.md §2).
/// Confined to the OpenGL backend boundary (§2.4).
module internal RubberPlant.Viewer.GlContext

#nowarn "9"

open System
open System.Collections.Generic
open System.IO
open System.Runtime.InteropServices
open Microsoft.FSharp.NativeInterop
open Silk.NET.Core.Loader
open Silk.NET.GLFW
open Silk.NET.OpenGL

type GlContext =
    private
        { Glfw: Glfw
          Window: nativeptr<WindowHandle>
          GlApi: GL
          MaxRbSize: int
          MaxSampleCount: int
          WindowSampleCount: int
          LifecycleLock: obj
          mutable Disposed: bool }

    member this.Gl = this.GlApi
    member this.GlfwApi = this.Glfw
    member this.WindowHandle = this.Window
    member this.MaxRenderbufferSize = this.MaxRbSize
    member this.MaxSamples = this.MaxSampleCount
    member this.WindowSamples = this.WindowSampleCount

    member this.SetTitle(title: string) =
        if not this.Disposed then
            this.Glfw.SetWindowTitle(this.Window, title)

    member this.GetFramebufferSize() : struct (int * int) =
        if this.Disposed then
            struct (0, 0)
        else
            let mutable w = 0
            let mutable h = 0
            this.Glfw.GetFramebufferSize(this.Window, &w, &h)
            struct (w, h)

    member this.GetWindowSize() : struct (int * int) =
        if this.Disposed then
            struct (0, 0)
        else
            let mutable w = 0
            let mutable h = 0
            this.Glfw.GetWindowSize(this.Window, &w, &h)
            struct (w, h)

    member this.ShouldClose() : bool =
        this.Disposed || this.Glfw.WindowShouldClose(this.Window)

    member this.SetShouldClose(value: bool) =
        if not this.Disposed then
            this.Glfw.SetWindowShouldClose(this.Window, value)

    member this.SwapBuffers() =
        if not this.Disposed then
            this.Glfw.SwapBuffers(this.Window)

    member this.WaitEvents() =
        if not this.Disposed then
            this.Glfw.WaitEvents()

    member this.WaitEventsTimeout(seconds: float) =
        if not this.Disposed && Double.IsFinite seconds && seconds >= 0.0 then
            this.Glfw.WaitEventsTimeout(seconds)

    member this.PostEmptyEvent() =
        lock this.LifecycleLock (fun () ->
            if not this.Disposed then
                try
                    this.Glfw.PostEmptyEvent()
                with _ ->
                    ())

    interface IDisposable with
        member this.Dispose() =
            lock this.LifecycleLock (fun () ->
                if not this.Disposed then
                    this.Disposed <- true

                    try
                        this.GlApi.Dispose()
                    with _ ->
                        ()

                    try
                        this.Glfw.MakeContextCurrent(NativePtr.nullPtr<WindowHandle>)
                    with _ ->
                        ()

                    try
                        this.Glfw.DestroyWindow(this.Window)
                    with _ ->
                        ()

                    try
                        this.Glfw.Terminate()
                    with _ ->
                        ()

                    try
                        this.Glfw.Dispose()
                    with _ ->
                        ())

let private formatGlfwError (glfw: Glfw) : string =
    let mutable ptr = NativePtr.nullPtr<byte>
    let code = glfw.GetError(&ptr)
    let rawPtr = NativePtr.toNativeInt ptr

    let desc =
        if rawPtr = 0n then
            ""
        else
            let s = Marshal.PtrToStringUTF8 rawPtr
            if isNull s then "" else s.Trim()

    if not (String.IsNullOrEmpty desc) then
        desc
    elif code <> Silk.NET.GLFW.ErrorCode.NoError then
        $"GLFW error code %s{string (int code)}"
    else
        "unknown GLFW error"

// `Silk.NET.GLFW` asks for `libglfw.so.3.3` on Linux whereas `Ultz.Native.GLFW`
// ships `runtimes/linux-*/native/libglfw.so.3` (and flat `libglfw.so.3` on publish),
// and `DefaultPathResolver` doesn't probe `runtimes/` for the stripped `.3` name.
let private linuxGlfwResolverRegistered =
    lazy (
        if OperatingSystem.IsLinux() then
            match PathResolver.Default with
            | :? DefaultPathResolver as resolver ->
                let rid =
                    match RuntimeInformation.OSArchitecture with
                    | Architecture.Arm64 -> Some "linux-arm64"
                    | Architecture.Arm -> Some "linux-arm"
                    | Architecture.X64 -> Some "linux-x64"
                    | _ -> None

                resolver.Resolvers.Add(
                    Func<string, IEnumerable<string>>(fun name ->
                        if name.StartsWith("libglfw", StringComparison.Ordinal) then
                            seq {
                                let flat = Path.Combine(AppContext.BaseDirectory, "libglfw.so.3")

                                if File.Exists flat then
                                    yield flat

                                match rid with
                                | Some r ->
                                    let nested =
                                        Path.Combine(AppContext.BaseDirectory, "runtimes", r, "native", "libglfw.so.3")

                                    if File.Exists nested then
                                        yield nested
                                | None -> ()
                            }
                        else
                            Seq.empty)
                )
            | _ -> ()
    )

let private applyWindowHints (glfw: Glfw) (visible: bool) (windowed: bool) (samples: int) =
    glfw.DefaultWindowHints()
    glfw.WindowHint(WindowHintInt.ContextVersionMajor, 3)
    glfw.WindowHint(WindowHintInt.ContextVersionMinor, 3)
    glfw.WindowHint(WindowHintOpenGlProfile.OpenGlProfile, OpenGlProfile.Core)
    glfw.WindowHint(WindowHintBool.OpenGLForwardCompat, true)
    glfw.WindowHint(WindowHintBool.Visible, visible)

    if windowed then
        glfw.WindowHint(WindowHintBool.Resizable, true)
        glfw.WindowHint(WindowHintBool.DoubleBuffer, true)

    if samples > 0 then
        glfw.WindowHint(WindowHintInt.Samples, samples)

let private cleanupFailedWindow (glfw: Glfw) (glOpt: GL option) (window: nativeptr<WindowHandle>) =
    match glOpt with
    | Some gl ->
        try
            gl.Dispose()
        with _ ->
            ()
    | None -> ()

    try
        glfw.MakeContextCurrent(NativePtr.nullPtr<WindowHandle>)
    with _ ->
        ()

    if NativePtr.toNativeInt window <> 0n then
        try
            glfw.DestroyWindow(window)
        with _ ->
            ()

    try
        glfw.Terminate()
    with _ ->
        ()

    try
        glfw.Dispose()
    with _ ->
        ()

let private createInternal
    (width: int)
    (height: int)
    (title: string)
    (visible: bool)
    (windowed: bool)
    : Result<GlContext, string> =
    let glfwRes =
        try
            linuxGlfwResolverRegistered.Force()
            Ok(Glfw.GetApi())
        with ex ->
            Error $"failed to initialize GLFW: %s{ex.Message}"

    match glfwRes with
    | Error e -> Error e
    | Ok glfw ->

    if not (glfw.Init()) then
        let msg = formatGlfwError glfw

        try
            glfw.Dispose()
        with _ ->
            ()

        Error $"failed to initialize GLFW: %s{msg}"
    else
        let createWin samples =
            applyWindowHints glfw visible windowed samples

            glfw.CreateWindow(
                width,
                height,
                title,
                NativePtr.nullPtr<Monitor>,
                NativePtr.nullPtr<WindowHandle>
            )

        let window, requestedSamples =
            if windowed then
                let msaaWin = createWin 4

                if NativePtr.toNativeInt msaaWin <> 0n then
                    msaaWin, 4
                else
                    // Clear any GLFW error from the 4x MSAA attempt before falling back to 0 samples.
                    let mutable dummyPtr = NativePtr.nullPtr<byte>
                    glfw.GetError(&dummyPtr) |> ignore
                    createWin 0, 0
            else
                createWin 0, 0

        if NativePtr.toNativeInt window = 0n then
            let msg = formatGlfwError glfw
            cleanupFailedWindow glfw None window
            Error $"failed to create OpenGL 3.3 Core context: %s{msg}"
        else
            try
                glfw.MakeContextCurrent(window)

                if windowed then
                    glfw.SwapInterval(1)

                let gl = GL.GetApi(Func<string, nativeint>(fun name -> glfw.GetProcAddress name))

                // Clear any benign pre-existing error state before querying limits.
                let mutable drained = 0

                while drained < 32 && gl.GetError() <> GLEnum.NoError do
                    drained <- drained + 1

                let mutable maxRb = 0
                gl.GetInteger(GLEnum.MaxRenderbufferSize, &maxRb)

                if gl.GetError() <> GLEnum.NoError || maxRb <= 0 then
                    maxRb <- 0

                let mutable maxS = 1
                gl.GetInteger(GLEnum.MaxSamples, &maxS)

                if gl.GetError() <> GLEnum.NoError || maxS < 1 then
                    maxS <- 1

                if maxRb <= 0 then
                    cleanupFailedWindow glfw (Some gl) window
                    Error "OpenGL context did not report a valid GL_MAX_RENDERBUFFER_SIZE"
                else
                    Ok
                        { Glfw = glfw
                          Window = window
                          GlApi = gl
                          MaxRbSize = maxRb
                          MaxSampleCount = max 1 maxS
                          WindowSampleCount = requestedSamples
                          LifecycleLock = obj ()
                          Disposed = false }
            with ex ->
                cleanupFailedWindow glfw None window
                Error $"failed to load OpenGL 3.3 Core functions: %s{ex.Message}"

/// Initializes GLFW and creates an offscreen hidden OpenGL 3.3 Core context (§2.1–§2.3).
let createHeadless () : Result<GlContext, string> =
    createInternal 1 1 "rubberplant-viewer" false false

/// Initializes GLFW and creates an OpenGL 3.3 Core window context (§2.1, §6.4).
let createWindow (width: int) (height: int) (title: string) (visible: bool) : Result<GlContext, string> =
    createInternal (max 1 width) (max 1 height) title visible true
