/// GLFW window/context bootstrap and OpenGL capability query (docs/viewer.md §2).
/// Confined to the OpenGL backend boundary (§2.4).
module internal RubberPlant.Viewer.GlContext

#nowarn "9"

open System
open System.Runtime.InteropServices
open Microsoft.FSharp.NativeInterop
open Silk.NET.GLFW
open Silk.NET.OpenGL

type GlContext =
    private
        { Glfw: Glfw
          Window: nativeptr<WindowHandle>
          GlApi: GL
          MaxRbSize: int
          MaxSampleCount: int
          mutable Disposed: bool }

    member this.Gl = this.GlApi
    member this.MaxRenderbufferSize = this.MaxRbSize
    member this.MaxSamples = this.MaxSampleCount

    interface IDisposable with
        member this.Dispose() =
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
                    ()

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

/// Initializes GLFW and creates an offscreen hidden OpenGL 3.3 Core context (§2.1–§2.3).
let createHeadless () : Result<GlContext, string> =
    let glfwRes =
        try
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
        glfw.DefaultWindowHints()
        glfw.WindowHint(WindowHintInt.ContextVersionMajor, 3)
        glfw.WindowHint(WindowHintInt.ContextVersionMinor, 3)
        glfw.WindowHint(WindowHintOpenGlProfile.OpenGlProfile, OpenGlProfile.Core)
        glfw.WindowHint(WindowHintBool.OpenGLForwardCompat, true)
        glfw.WindowHint(WindowHintBool.Visible, false)

        let window =
            glfw.CreateWindow(
                1,
                1,
                "rubberplant-viewer",
                NativePtr.nullPtr<Monitor>,
                NativePtr.nullPtr<WindowHandle>
            )

        if NativePtr.toNativeInt window = 0n then
            let msg = formatGlfwError glfw

            try
                glfw.Terminate()
            with _ ->
                ()

            try
                glfw.Dispose()
            with _ ->
                ()

            Error $"failed to create OpenGL 3.3 Core context: %s{msg}"
        else
            try
                glfw.MakeContextCurrent(window)
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
                    try
                        gl.Dispose()
                    with _ ->
                        ()

                    try
                        glfw.MakeContextCurrent(NativePtr.nullPtr<WindowHandle>)
                    with _ ->
                        ()

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

                    Error "OpenGL context did not report a valid GL_MAX_RENDERBUFFER_SIZE"
                else
                    Ok
                        { Glfw = glfw
                          Window = window
                          GlApi = gl
                          MaxRbSize = maxRb
                          MaxSampleCount = max 1 maxS
                          Disposed = false }
            with ex ->
                try
                    glfw.MakeContextCurrent(NativePtr.nullPtr<WindowHandle>)
                with _ ->
                    ()

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

                Error $"failed to load OpenGL 3.3 Core functions: %s{ex.Message}"
