/// GLSL 330 Core shader compilation and program handles (docs/viewer.md §5.2).
/// Confined to the OpenGL backend boundary (§2.4).
module internal RubberPlant.Viewer.Shaders

open System
open Silk.NET.OpenGL

type LitProgram =
    { Program: uint32
      ViewProjLoc: int
      AlbedoLoc: int
      EyePosLoc: int }

type FlatProgram =
    { Program: uint32
      ViewProjLoc: int
      ColorLoc: int }

type LineProgram =
    { Program: uint32
      ViewProjLoc: int }

type ShaderSet =
    { Gl: GL
      Lit: LitProgram
      Flat: FlatProgram
      Line: LineProgram
      mutable Disposed: bool }

    interface IDisposable with
        member this.Dispose() =
            if not this.Disposed then
                this.Disposed <- true
                this.Gl.DeleteProgram(this.Lit.Program)
                this.Gl.DeleteProgram(this.Flat.Program)
                this.Gl.DeleteProgram(this.Line.Program)

let private litVertSrc =
    """#version 330 core
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aNormal;

uniform mat4 uViewProj;

out vec3 vPos;
out vec3 vNormal;

void main()
{
    vPos = aPos;
    vNormal = aNormal;
    gl_Position = uViewProj * vec4(aPos, 1.0);
}
"""

let private litFragSrc =
    """#version 330 core
uniform vec3 uAlbedo;
uniform vec3 uEyePos;

in vec3 vPos;
in vec3 vNormal;

out vec4 FragColor;

void main()
{
    float nLen = length(vNormal);
    vec3 n = nLen > 0.0 ? (vNormal / nLen) : vec3(0.0, 1.0, 0.0);
    if (!gl_FrontFacing)
        n = -n;

    // Hemisphere ambient (~0.38): warmer sky (+Y), cooler ground (-Y).
    float hemi = 0.5 * n.y + 0.5;
    vec3 skyTint = vec3(1.02, 1.00, 0.97);
    vec3 groundTint = vec3(0.76, 0.80, 0.86);
    vec3 ambient = 0.38 * mix(groundTint, skyTint, hemi) * uAlbedo;

    // Warm key light from upper front-right (~0.85).
    vec3 keyDir = normalize(vec3(0.55, 0.80, 0.45));
    vec3 keyColor = vec3(1.04, 1.00, 0.94);
    float keyDiff = max(dot(n, keyDir), 0.0);
    vec3 key = 0.85 * keyDiff * keyColor * uAlbedo;

    // Cool fill light from opposite back-left (~0.35).
    vec3 fillDir = normalize(vec3(-0.60, 0.35, -0.55));
    vec3 fillColor = vec3(0.86, 0.93, 1.05);
    float fillDiff = max(dot(n, fillDir), 0.0);
    vec3 fill = 0.35 * fillDiff * fillColor * uAlbedo;

    // Subtle Blinn-Phong view specular for cylindrical highlight separation.
    vec3 toEye = uEyePos - vPos;
    float vLen = length(toEye);
    vec3 V = vLen > 0.0 ? (toEye / vLen) : vec3(0.0, 0.0, 1.0);

    vec3 keySum = V + keyDir;
    float khLen = length(keySum);
    vec3 keyHalf = khLen > 0.0 ? (keySum / khLen) : vec3(0.0);
    float kh = max(dot(n, keyHalf), 0.0);
    float keySpec = kh > 0.0 ? pow(kh, 32.0) : 0.0;

    vec3 fillSum = V + fillDir;
    float fhLen = length(fillSum);
    vec3 fillHalf = fhLen > 0.0 ? (fillSum / fhLen) : vec3(0.0);
    float fh = max(dot(n, fillHalf), 0.0);
    float fillSpec = fh > 0.0 ? pow(fh, 32.0) : 0.0;

    vec3 spec = (0.06 * keySpec * keyColor) + (0.025 * fillSpec * fillColor);

    vec3 linearCol = ambient + key + fill + spec;

    // Exact IEC linear -> sRGB transfer clamped to [0, 1], guarding pow against 0.0.
    vec3 c = clamp(linearCol, 0.0, 1.0);
    vec3 lo = 12.92 * c;
    vec3 hi = 1.055 * pow(max(c, vec3(0.0031308)), vec3(1.0 / 2.4)) - 0.055;
    vec3 srgb = mix(lo, hi, greaterThan(c, vec3(0.0031308)));

    FragColor = vec4(srgb, 1.0);
}
"""

let private flatVertSrc =
    """#version 330 core
layout(location = 0) in vec3 aPos;

uniform mat4 uViewProj;

void main()
{
    gl_Position = uViewProj * vec4(aPos, 1.0);
}
"""

let private flatFragSrc =
    """#version 330 core
uniform vec3 uColor;

out vec4 FragColor;

void main()
{
    FragColor = vec4(uColor, 1.0);
}
"""

let private lineVertSrc =
    """#version 330 core
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aColor;

uniform mat4 uViewProj;

out vec3 vColor;

void main()
{
    vColor = aColor;
    gl_Position = uViewProj * vec4(aPos, 1.0);
}
"""

let private lineFragSrc =
    """#version 330 core
in vec3 vColor;

out vec4 FragColor;

void main()
{
    FragColor = vec4(vColor, 1.0);
}
"""

let private compileShader (gl: GL) (label: string) (shaderType: ShaderType) (source: string) : Result<uint32, string> =
    let shader = gl.CreateShader(shaderType)

    if shader = 0u then
        Error $"glCreateShader failed for %s{label}"
    else
        gl.ShaderSource(shader, source)
        gl.CompileShader(shader)
        let mutable status = 0
        gl.GetShader(shader, ShaderParameterName.CompileStatus, &status)

        if status = 0 then
            let log = gl.GetShaderInfoLog(shader)
            gl.DeleteShader(shader)
            Error $"shader compilation failed (%s{label}): %s{log}"
        else
            Ok shader

let private linkProgram (gl: GL) (label: string) (vertSrc: string) (fragSrc: string) : Result<uint32, string> =
    match compileShader gl $"%s{label}.vert" ShaderType.VertexShader vertSrc with
    | Error e -> Error e
    | Ok vert ->
        match compileShader gl $"%s{label}.frag" ShaderType.FragmentShader fragSrc with
        | Error e ->
            gl.DeleteShader(vert)
            Error e
        | Ok frag ->
            let prog = gl.CreateProgram()

            if prog = 0u then
                gl.DeleteShader(vert)
                gl.DeleteShader(frag)
                Error $"glCreateProgram failed for %s{label}"
            else
                gl.AttachShader(prog, vert)
                gl.AttachShader(prog, frag)
                gl.LinkProgram(prog)
                gl.DetachShader(prog, vert)
                gl.DetachShader(prog, frag)
                gl.DeleteShader(vert)
                gl.DeleteShader(frag)

                let mutable status = 0
                gl.GetProgram(prog, ProgramPropertyARB.LinkStatus, &status)

                if status = 0 then
                    let log = gl.GetProgramInfoLog(prog)
                    gl.DeleteProgram(prog)
                    Error $"shader link failed (%s{label}): %s{log}"
                else
                    Ok prog

/// Compiles and links all three GLSL 330 Core shader programs (§5.2).
let create (gl: GL) : Result<ShaderSet, string> =
    match linkProgram gl "lit" litVertSrc litFragSrc with
    | Error e -> Error e
    | Ok litProg ->
        match linkProgram gl "flat" flatVertSrc flatFragSrc with
        | Error e ->
            gl.DeleteProgram(litProg)
            Error e
        | Ok flatProg ->
            match linkProgram gl "line" lineVertSrc lineFragSrc with
            | Error e ->
                gl.DeleteProgram(litProg)
                gl.DeleteProgram(flatProg)
                Error e
            | Ok lineProg ->
                Ok
                    { Gl = gl
                      Lit =
                        { Program = litProg
                          ViewProjLoc = gl.GetUniformLocation(litProg, "uViewProj")
                          AlbedoLoc = gl.GetUniformLocation(litProg, "uAlbedo")
                          EyePosLoc = gl.GetUniformLocation(litProg, "uEyePos") }
                      Flat =
                        { Program = flatProg
                          ViewProjLoc = gl.GetUniformLocation(flatProg, "uViewProj")
                          ColorLoc = gl.GetUniformLocation(flatProg, "uColor") }
                      Line =
                        { Program = lineProg
                          ViewProjLoc = gl.GetUniformLocation(lineProg, "uViewProj") }
                      Disposed = false }
