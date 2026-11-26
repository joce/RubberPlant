/// Pure bicubic Bézier surface (`.s` file) parser, canonical alignment,
/// 3-tier degenerate normal solver, seam averaging, and SVG boundary extractor
/// (v0.3-organs-animation.md §4.2, §4.4).
module RubberPlant.Bezier

open System
open System.Collections.Generic
open System.Globalization
open RubberPlant.Geometry

// ------------------------------------------------------------ scale-safe math

let private isSafe (v: Vec3) =
    let m = max (abs v.X) (max (abs v.Y) (abs v.Z))
    m > 1e-150 && m < 1e150

let private scaled (v: Vec3) =
    let m = max (abs v.X) (max (abs v.Y) (abs v.Z))
    let e = Math.ILogB m
    Vec3.create (Math.ScaleB(v.X, -e)) (Math.ScaleB(v.Y, -e)) (Math.ScaleB(v.Z, -e))

let private scaleSafeLength (v: Vec3) : float =
    if not (Vec3.isFinite v) then
        infinity
    elif v = Vec3.zero then
        0.0
    elif isSafe v then
        Vec3.length v
    else
        let m = max (abs v.X) (max (abs v.Y) (abs v.Z))
        let e = Math.ILogB m
        let w = Vec3.create (Math.ScaleB(v.X, -e)) (Math.ScaleB(v.Y, -e)) (Math.ScaleB(v.Z, -e))
        Math.ScaleB(Vec3.length w, e)

let private tryNormalize (v: Vec3) : Vec3 option =
    if not (Vec3.isFinite v) || v = Vec3.zero then
        None
    elif isSafe v then
        let len = Vec3.length v
        if len > 0.0 && Double.IsFinite len then Some((1.0 / len) * v) else None
    else
        let w = scaled v
        let len = Vec3.length w
        if len > 0.0 && Double.IsFinite len then Some((1.0 / len) * w) else None

// ----------------------------------------------------------------- .s parser

let private parseFloat (s: string) : float option =
    match Double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture) with
    | true, v when Double.IsFinite v -> Some v
    | _ -> None

let private parseInt (s: string) : int option =
    match Int32.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture) with
    | true, v -> Some v
    | _ -> None

let private splitWS (s: string) : string[] =
    s.Split([| ' '; '\t' |], StringSplitOptions.RemoveEmptyEntries)

let private tryPrefix (prefix: string) (line: string) : string option =
    if line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) then
        Some(line.Substring(prefix.Length).Trim())
    else
        None

let private parseVec3Body (label: string) (body: string) : Result<Vec3, string> =
    // Accept both `x y z` and `cpfg`-style `X: x Y: y Z: z`.
    let spaced = body.Replace(":", ": ")
    let raw = splitWS spaced

    let cleaned =
        raw
        |> Array.filter (fun tok ->
            not (
                tok.Equals("X:", StringComparison.OrdinalIgnoreCase)
                || tok.Equals("Y:", StringComparison.OrdinalIgnoreCase)
                || tok.Equals("Z:", StringComparison.OrdinalIgnoreCase)
            ))

    match cleaned with
    | [| sx; sy; sz |] ->
        match parseFloat sx, parseFloat sy, parseFloat sz with
        | Some x, Some y, Some z -> Ok(Vec3.create x y z)
        | _ -> Error $"invalid vector in %s{label}: '%s{body}'"
    | _ -> Error $"expected 3 coordinates in %s{label}, got '%s{body}'"

let private parsePrecisionLine (body: string) : Result<int * int, string> =
    let tokens = splitWS (body.Replace(":", ": "))
    let mutable sVal: int option = None
    let mutable tVal: int option = None
    let mutable err: string option = None
    let mutable i = 0

    while i < tokens.Length && err.IsNone do
        let tok = tokens[i].ToUpperInvariant()

        if (tok = "S:" || tok = "T:") && i + 1 < tokens.Length then
            match parseInt tokens[i + 1] with
            | Some n when n >= 1 ->
                if tok = "S:" then
                    if sVal.IsSome then err <- Some "duplicate S: in PRECISION line"
                    else sVal <- Some n
                else
                    if tVal.IsSome then err <- Some "duplicate T: in PRECISION line"
                    else tVal <- Some n

                i <- i + 2
            | _ -> err <- Some $"PRECISION %s{tok} must be an integer >= 1, got '%s{tokens[i + 1]}'"
        else
            err <- Some $"unexpected token '%s{tokens[i]}' in PRECISION line"

    match err, sVal, tVal with
    | Some msg, _, _ -> Error msg
    | None, Some s, Some t -> Ok(s, t)
    | None, _, _ -> Error "PRECISION line must specify both S: and T:"

let private neighborTags = set [ "AL:"; "A:"; "AR:"; "L:"; "R:"; "BL:"; "B:"; "BR:" ]

let private emptyNeighbors: PatchNeighbors =
    { TopLeft = None
      Top = None
      TopRight = None
      Left = None
      Right = None
      BottomLeft = None
      Bottom = None
      BottomRight = None }

let private isNeighborLine (line: string) =
    let tokens = splitWS (line.Replace(":", ": "))
    tokens.Length > 0 && neighborTags.Contains(tokens[0].ToUpperInvariant())

let private parseNeighborLine (line: string) (n: PatchNeighbors) (seen: HashSet<string>) : Result<PatchNeighbors, string> =
    let tokens = splitWS (line.Replace(":", ": "))
    let mutable cur = n
    let mutable err: string option = None
    let mutable i = 0

    while i < tokens.Length && err.IsNone do
        let tag = tokens[i].ToUpperInvariant()

        if not (neighborTags.Contains tag) then
            err <- Some $"unexpected neighbor tag '%s{tokens[i]}'"
        elif i + 1 >= tokens.Length then
            err <- Some $"missing patch name after '%s{tag}'"
        else
            let rawTarget = tokens[i + 1]

            if neighborTags.Contains(rawTarget.ToUpperInvariant()) then
                err <- Some $"missing patch name after '%s{tag}'"
            elif not (seen.Add tag) then
                err <- Some $"duplicate neighbor tag '%s{tag}' in patch"
            else
                let target = if rawTarget = "~" then None else Some rawTarget

                cur <-
                    match tag with
                    | "AL:" -> { cur with TopLeft = target }
                    | "A:" -> { cur with Top = target }
                    | "AR:" -> { cur with TopRight = target }
                    | "L:" -> { cur with Left = target }
                    | "R:" -> { cur with Right = target }
                    | "BL:" -> { cur with BottomLeft = target }
                    | "B:" -> { cur with Bottom = target }
                    | "BR:" -> { cur with BottomRight = target }
                    | _ -> cur

                i <- i + 2

    match err with
    | Some msg -> Error msg
    | None -> Ok cur

type private RawPatch =
    { Name: string
      Neighbors: PatchNeighbors
      RawPoints: Vec3[] }

/// Parses a `.s` bicubic Bézier surface file and transforms its control points
/// and END POINT into canonical surface space (ABOP Fig 5.1, §4.2).
/// Emits `RP0307` warnings for unknown or non-reciprocal patch neighbors.
let parse (name: string) (pos: Pos option) (text: string) : Result<BezierSurface * Diagnostic list, string> =
    if isNull text then
        Error "surface text is null"
    else
        let lines =
            text.Split('\n')
            |> Array.map (fun l -> l.TrimEnd('\r').Trim())
            |> Array.filter (fun l -> l.Length > 0)

        if lines.Length = 0 then
            Error "empty .s surface file"
        else
            // Line 0: bounding box (6 finite floats).
            let bboxTokens = splitWS lines[0]

            if bboxTokens.Length <> 6 || not (Array.forall (parseFloat >> Option.isSome) bboxTokens) then
                Error $"expected 6 finite bounding-box numbers on first line, got '%s{lines[0]}'"
            else
                let mutable idx = 1
                let mutable precision: (int * int) option = None
                let mutable contactPoint: Vec3 option = None
                let mutable endPoint: Vec3 option = None
                let mutable heading: Vec3 option = None
                let mutable up: Vec3 option = None
                let mutable size: float option = None
                let mutable headerErr: string option = None
                let mutable readingHeader = true

                while idx < lines.Length && readingHeader && headerErr.IsNone do
                    let line = lines[idx]

                    match tryPrefix "PRECISION" line with
                    | Some rest when rest = "" || line.Length > "PRECISION".Length && Char.IsWhiteSpace line["PRECISION".Length] ->
                        if precision.IsSome then
                            headerErr <- Some "duplicate PRECISION line"
                        else
                            match parsePrecisionLine rest with
                            | Ok p ->
                                precision <- Some p
                                idx <- idx + 1
                            | Error msg -> headerErr <- Some msg
                    | _ ->
                        match tryPrefix "CONTACT POINT:" line with
                        | Some rest ->
                            if contactPoint.IsSome then
                                headerErr <- Some "duplicate CONTACT POINT line"
                            else
                                match parseVec3Body "CONTACT POINT" rest with
                                | Ok v ->
                                    contactPoint <- Some v
                                    idx <- idx + 1
                                | Error msg -> headerErr <- Some msg
                        | None ->
                            match tryPrefix "END POINT:" line with
                            | Some rest ->
                                if endPoint.IsSome then
                                    headerErr <- Some "duplicate END POINT line"
                                else
                                    match parseVec3Body "END POINT" rest with
                                    | Ok v ->
                                        endPoint <- Some v
                                        idx <- idx + 1
                                    | Error msg -> headerErr <- Some msg
                            | None ->
                                match tryPrefix "HEADING:" line with
                                | Some rest ->
                                    if heading.IsSome then
                                        headerErr <- Some "duplicate HEADING line"
                                    else
                                        match parseVec3Body "HEADING" rest with
                                        | Ok v ->
                                            heading <- Some v
                                            idx <- idx + 1
                                        | Error msg -> headerErr <- Some msg
                                | None ->
                                    match tryPrefix "UP:" line with
                                    | Some rest ->
                                        if up.IsSome then
                                            headerErr <- Some "duplicate UP line"
                                        else
                                            match parseVec3Body "UP" rest with
                                            | Ok v ->
                                                up <- Some v
                                                idx <- idx + 1
                                            | Error msg -> headerErr <- Some msg
                                    | None ->
                                        match tryPrefix "SIZE:" line with
                                        | Some rest ->
                                            if size.IsSome then
                                                headerErr <- Some "duplicate SIZE line"
                                            else
                                                match parseFloat rest with
                                                | Some s when s > 0.0 ->
                                                    size <- Some s
                                                    idx <- idx + 1
                                                | _ -> headerErr <- Some $"SIZE must be a finite number > 0, got '%s{rest}'"
                                        | None -> readingHeader <- false

                match headerErr, contactPoint, endPoint, heading, up, size with
                | Some msg, _, _, _, _, _ -> Error msg
                | None, None, _, _, _, _ -> Error "missing CONTACT POINT line"
                | None, _, None, _, _, _ -> Error "missing END POINT line"
                | None, _, _, None, _, _ -> Error "missing HEADING line"
                | None, _, _, _, None, _ -> Error "missing UP line"
                | None, _, _, _, _, None -> Error "missing SIZE line"
                | None, Some cp, Some ep, Some hVec, Some uVec, Some sz ->
                    // Construct right-handed surface frame (ABOP Fig 5.1, §4.2).
                    match tryNormalize hVec, tryNormalize uVec with
                    | None, _ -> Error "HEADING vector must be non-zero"
                    | _, None -> Error "UP vector must be non-zero"
                    | Some yS, Some uHat ->
                        let xRaw = Vec3.cross yS uHat

                        if scaleSafeLength xRaw <= 1e-12 then
                            Error "HEADING and UP vectors must not be parallel"
                        else
                            match tryNormalize xRaw with
                            | None -> Error "HEADING and UP vectors produce degenerate frame"
                            | Some xS ->
                                let zS = Vec3.cross xS yS

                                let toCanonical (p: Vec3) : Result<Vec3, string> =
                                    let diff = p - cp
                                    let d = Vec3.create (diff.X / sz) (diff.Y / sz) (diff.Z / sz)
                                    let q = Vec3.create (Vec3.dot d xS) (Vec3.dot d yS) (Vec3.dot d zS)

                                    if Vec3.isFinite q then
                                        Ok q
                                    else
                                        Error "transformed surface coordinate is not finite"

                                match toCanonical ep with
                                | Error msg -> Error msg
                                | Ok canonEndPoint ->
                                    let rawPatches = ResizeArray<RawPatch>()
                                    let seenPatchNames = HashSet<string>()
                                    let mutable patchErr: string option = None

                                    while idx < lines.Length && patchErr.IsNone do
                                        let patchName = lines[idx]
                                        let nameToks = splitWS patchName

                                        if patchName = "~" then
                                            patchErr <- Some "patch name cannot be `~`"
                                        elif
                                            nameToks.Length = 3
                                            && Array.forall (parseFloat >> Option.isSome) nameToks
                                        then
                                            patchErr <- Some $"expected patch name, got control point '%s{patchName}'"
                                        elif not (seenPatchNames.Add patchName) then
                                            patchErr <- Some $"duplicate patch name '%s{patchName}'"
                                        else
                                            idx <- idx + 1
                                            let mutable neighbors = emptyNeighbors
                                            let seenTags = HashSet<string>()
                                            let mutable readingMeta = true

                                            while idx < lines.Length && readingMeta && patchErr.IsNone do
                                                let metaLine = lines[idx]

                                                if
                                                    (tryPrefix "TOP COLOR:" metaLine).IsSome
                                                    || (tryPrefix "BOTTOM COLOR:" metaLine).IsSome
                                                then
                                                    idx <- idx + 1
                                                elif isNeighborLine metaLine then
                                                    match parseNeighborLine metaLine neighbors seenTags with
                                                    | Ok next ->
                                                        neighbors <- next
                                                        idx <- idx + 1
                                                    | Error msg -> patchErr <- Some msg
                                                else
                                                    readingMeta <- false

                                            if patchErr.IsNone then
                                                let pts = ResizeArray<Vec3>(16)

                                                while pts.Count < 16 && idx < lines.Length && patchErr.IsNone do
                                                    let toks = splitWS lines[idx]

                                                    if toks.Length = 0 || toks.Length % 3 <> 0 then
                                                        patchErr <-
                                                            Some
                                                                $"expected 3*k coordinates in patch '%s{patchName}', got '%s{lines[idx]}'"
                                                    else
                                                        let k = toks.Length / 3

                                                        if pts.Count + k > 16 then
                                                            patchErr <-
                                                                Some $"patch '%s{patchName}' has more than 16 control points"
                                                        else
                                                            let mutable pIdx = 0

                                                            while pIdx < k && patchErr.IsNone do
                                                                match
                                                                    parseFloat toks[3 * pIdx],
                                                                    parseFloat toks[3 * pIdx + 1],
                                                                    parseFloat toks[3 * pIdx + 2]
                                                                with
                                                                | Some x, Some y, Some z ->
                                                                    match toCanonical (Vec3.create x y z) with
                                                                    | Ok q -> pts.Add q
                                                                    | Error msg -> patchErr <- Some msg
                                                                | _ ->
                                                                    patchErr <-
                                                                        Some
                                                                            $"invalid control point in patch '%s{patchName}': '%s{lines[idx]}'"

                                                                pIdx <- pIdx + 1

                                                            idx <- idx + 1

                                                if patchErr.IsNone && pts.Count <> 16 then
                                                    patchErr <-
                                                        Some
                                                            $"patch '%s{patchName}' expected 16 control points, got %s{string pts.Count}"
                                                elif patchErr.IsNone then
                                                    rawPatches.Add
                                                        { Name = patchName
                                                          Neighbors = neighbors
                                                          RawPoints = pts.ToArray() }

                                    match patchErr with
                                    | Some msg -> Error msg
                                    | None when rawPatches.Count = 0 -> Error "surface contains no patches"
                                    | None ->
                                        let nameToIdx = Dictionary<string, int>()

                                        for i = 0 to rawPatches.Count - 1 do
                                            nameToIdx[rawPatches[i].Name] <- i

                                        let warnings = ResizeArray<Diagnostic>()

                                        let validateLink
                                            (patchName: string)
                                            (label: string)
                                            (targetOpt: string option)
                                            (getOpp: PatchNeighbors -> string option)
                                            =
                                            match targetOpt with
                                            | None -> None
                                            | Some targetName ->
                                                match nameToIdx.TryGetValue targetName with
                                                | false, _ ->
                                                    warnings.Add(
                                                        Diagnostic.warning
                                                            Codes.InvalidSurfaceNeighbor
                                                            pos
                                                            $"patch `%s{patchName}` neighbor %s{label} references unknown patch `%s{targetName}`; ignored"
                                                    )

                                                    None
                                                | true, targetIdx ->
                                                    if getOpp rawPatches[targetIdx].Neighbors = Some patchName then
                                                        Some targetName
                                                    else
                                                        warnings.Add(
                                                            Diagnostic.warning
                                                                Codes.InvalidSurfaceNeighbor
                                                                pos
                                                                $"patch `%s{patchName}` neighbor %s{label} -> `%s{targetName}` is not reciprocal; ignored"
                                                        )

                                                        None

                                        let patches =
                                            [| for rp in rawPatches ->
                                                   let nb = rp.Neighbors

                                                   let validatedNeighbors =
                                                       { TopLeft = validateLink rp.Name "AL" nb.TopLeft _.BottomRight
                                                         Top = validateLink rp.Name "A" nb.Top _.Bottom
                                                         TopRight = validateLink rp.Name "AR" nb.TopRight _.BottomLeft
                                                         Left = validateLink rp.Name "L" nb.Left _.Right
                                                         Right = validateLink rp.Name "R" nb.Right _.Left
                                                         BottomLeft = validateLink rp.Name "BL" nb.BottomLeft _.TopRight
                                                         Bottom = validateLink rp.Name "B" nb.Bottom _.Top
                                                         BottomRight = validateLink rp.Name "BR" nb.BottomRight _.TopLeft }

                                                   { Name = rp.Name
                                                     Neighbors = validatedNeighbors
                                                     ControlPoints = rp.RawPoints } |]

                                        Ok(
                                            { Name = name
                                              DefaultDivisions = defaultArg precision (6, 6)
                                              EndPoint = canonEndPoint
                                              Patches = patches },
                                            List.ofSeq warnings
                                        )

// ------------------------------------------------ Bernstein & surface helpers

/// Transforms a canonical surface coordinate into world space (§4.4).
let inline toWorld (inst: SurfaceInstance) (q: Vec3) : Vec3 =
    inst.Origin
    + (inst.Scale.X * q.X) * inst.AxisX
    + (inst.Scale.Y * q.Y) * inst.AxisY
    + (inst.Scale.Z * q.Z) * inst.AxisZ

/// All world-space transformed control points of a surface instance.
let instanceControlPoints (inst: SurfaceInstance) : Vec3 seq =
    seq {
        for patch in inst.Surface.Patches do
            for q in patch.ControlPoints do
                yield toWorld inst q
    }

let inline private bernstein (t: float) =
    let s = 1.0 - t
    let s2 = s * s
    let t2 = t * t
    s2 * s, 3.0 * s2 * t, 3.0 * s * t2, t2 * t

let inline private bernsteinDeriv (t: float) =
    let s = 1.0 - t
    let d0 = -3.0 * s * s
    let d1 = 3.0 * s * s - 6.0 * s * t
    let d2 = 6.0 * s * t - 3.0 * t * t
    let d3 = 3.0 * t * t
    d0, d1, d2, d3

let private evalPatch (cp: Vec3[]) (u: float) (v: float) : Vec3 =
    let bu0, bu1, bu2, bu3 = bernstein u
    let bv0, bv1, bv2, bv3 = bernstein v
    let inline evalRow i =
        bv0 * cp[i * 4]
        + bv1 * cp[i * 4 + 1]
        + bv2 * cp[i * 4 + 2]
        + bv3 * cp[i * 4 + 3]

    bu0 * evalRow 0 + bu1 * evalRow 1 + bu2 * evalRow 2 + bu3 * evalRow 3

let private evalPartials (cp: Vec3[]) (u: float) (v: float) : Vec3 * Vec3 =
    let bu0, bu1, bu2, bu3 = bernstein u
    let bv0, bv1, bv2, bv3 = bernstein v
    let du0, du1, du2, du3 = bernsteinDeriv u
    let dv0, dv1, dv2, dv3 = bernsteinDeriv v

    let inline rowVal i =
        bv0 * cp[i * 4]
        + bv1 * cp[i * 4 + 1]
        + bv2 * cp[i * 4 + 2]
        + bv3 * cp[i * 4 + 3]

    let inline rowDerivV i =
        dv0 * cp[i * 4]
        + dv1 * cp[i * 4 + 1]
        + dv2 * cp[i * 4 + 2]
        + dv3 * cp[i * 4 + 3]

    let pu = du0 * rowVal 0 + du1 * rowVal 1 + du2 * rowVal 2 + du3 * rowVal 3
    let pv = bu0 * rowDerivV 0 + bu1 * rowDerivV 1 + bu2 * rowDerivV 2 + bu3 * rowDerivV 3
    pu, pv

/// 16-vertex boundary polygon (4 edges × 4 steps) per patch for SVG (§4.4).
let surfaceBoundaryPolygons (inst: SurfaceInstance) : Vec3[][] =
    [| for patch in inst.Surface.Patches do
           let cp = Array.map (toWorld inst) patch.ControlPoints
           let pts = Array.zeroCreate<Vec3> 16

           for k = 0 to 3 do
               let t = float k * 0.25
               let inv = 1.0 - t
               pts[k] <- evalPatch cp t 0.0
               pts[4 + k] <- evalPatch cp 1.0 t
               pts[8 + k] <- evalPatch cp inv 1.0
               pts[12 + k] <- evalPatch cp 0.0 inv

           yield pts |]

// --------------------------------------------------------------- tessellation

type TessellatedSurface =
    { Positions: Vec3[]
      Normals: Vec3[]
      Triangles: int[] }

let private analyticalNormal (cp: Vec3[]) (u: float) (v: float) : Vec3 option =
    let pu, pv = evalPartials cp u v

    match tryNormalize pu, tryNormalize pv with
    | Some nu, Some nv ->
        let c = Vec3.cross nu nv
        let len = Vec3.length c

        if len > 1e-12 && Double.IsFinite len then
            Some((1.0 / len) * c)
        else
            None
    | _ -> None

let inline private nudgeTowardHalf (delta: float) (w: float) =
    if w < 0.5 then min 0.5 (w + delta)
    elif w > 0.5 then max 0.5 (w - delta)
    else 0.5

/// Tessellates a `SurfaceInstance` using pre-scaled world control points,
/// quad triangulation, the 3-tier degenerate normal solver, and order-independent
/// reciprocal seam normal averaging (§4.4).
let tessellateSurface (divOverride: int option) (inst: SurfaceInstance) : TessellatedSurface =
    let sDiv, tDiv =
        match divOverride with
        | Some d -> max 1 d, max 1 d
        | None ->
            let s, t = inst.Surface.DefaultDivisions
            max 1 s, max 1 t

    let patches = inst.Surface.Patches
    let patchCount = patches.Length
    let stride = tDiv + 1
    let vertsPerPatch = (sDiv + 1) * stride
    let inline vIdx a b = a * stride + b

    let worldCPs =
        patches |> Array.map (fun p -> Array.map (toWorld inst) p.ControlPoints)

    let patchPositions = Array.zeroCreate<Vec3[]> patchCount
    let patchTris = Array.zeroCreate<int[]> patchCount
    let patchRawNormals = Array.zeroCreate<Vec3[]> patchCount
    let patchHasTris = Array.zeroCreate<bool> patchCount

    // Pass 1: evaluate grid positions, filter zero-area triangles, solve 3-tier normals.
    for pIdx = 0 to patchCount - 1 do
        let cp = worldCPs[pIdx]
        let pos = Array.zeroCreate<Vec3> vertsPerPatch

        for a = 0 to sDiv do
            let u = float a / float sDiv

            for b = 0 to tDiv do
                let v = float b / float tDiv
                pos[vIdx a b] <- evalPatch cp u v

        let tris = ResizeArray<int>(sDiv * tDiv * 6)
        let triNormalSums = Array.create vertsPerPatch Vec3.zero
        let firstIncNormal = Array.create<Vec3 option> vertsPerPatch None
        let mutable anyPatchNormal: Vec3 option = None

        let tryAddTri i0 i1 i2 =
            let e1 = pos[i1] - pos[i0]
            let e2 = pos[i2] - pos[i0]

            match tryNormalize e1, tryNormalize e2 with
            | Some u1, Some u2 ->
                let c = Vec3.cross u1 u2
                let cl = Vec3.length c

                if cl > 1e-12 && Double.IsFinite cl then
                    let fn = (1.0 / cl) * c
                    tris.Add i0
                    tris.Add i1
                    tris.Add i2

                    if anyPatchNormal.IsNone then
                        anyPatchNormal <- Some fn

                    for vi in [ i0; i1; i2 ] do
                        triNormalSums[vi] <- triNormalSums[vi] + fn

                        if firstIncNormal[vi].IsNone then
                            firstIncNormal[vi] <- Some fn
            | _ -> ()

        for a = 0 to sDiv - 1 do
            for b = 0 to tDiv - 1 do
                let i00 = vIdx a b
                let i10 = vIdx (a + 1) b
                let i11 = vIdx (a + 1) (b + 1)
                let i01 = vIdx a (b + 1)
                tryAddTri i00 i10 i11
                tryAddTri i00 i11 i01

        let rawNormals = Array.zeroCreate<Vec3> vertsPerPatch
        let defaultNormal = defaultArg anyPatchNormal (Vec3.create 0.0 1.0 0.0)

        for a = 0 to sDiv do
            let u = float a / float sDiv

            for b = 0 to tDiv do
                let v = float b / float tDiv
                let idx = vIdx a b

                let n =
                    match analyticalNormal cp u v with
                    | Some n -> n
                    | None ->
                        match analyticalNormal cp (nudgeTowardHalf 1e-4 u) (nudgeTowardHalf 1e-4 v) with
                        | Some n -> n
                        | None ->
                            match analyticalNormal cp (nudgeTowardHalf 1e-2 u) (nudgeTowardHalf 1e-2 v) with
                            | Some n -> n
                            | None ->
                                match tryNormalize triNormalSums[idx] with
                                | Some n -> n
                                | None -> defaultArg firstIncNormal[idx] defaultNormal

                rawNormals[idx] <- n

        patchPositions[pIdx] <- pos
        patchTris[pIdx] <- tris.ToArray()
        patchRawNormals[pIdx] <- rawNormals
        patchHasTris[pIdx] <- tris.Count > 0

    // Pass 2: order-independent reciprocal seam normal averaging.
    let nameToIdx = Dictionary<string, int>()

    for i = 0 to patchCount - 1 do
        nameToIdx[patches[i].Name] <- i

    let outPositions = ResizeArray<Vec3>()
    let outNormals = ResizeArray<Vec3>()
    let outTriangles = ResizeArray<int>()

    for pIdx = 0 to patchCount - 1 do
        if patchHasTris[pIdx] then
            let baseVertex = outPositions.Count
            let pos = patchPositions[pIdx]
            let raw = patchRawNormals[pIdx]
            let nb = patches[pIdx].Neighbors

            outPositions.AddRange pos

            for a = 0 to sDiv do
                for b = 0 to tDiv do
                    let idx = vIdx a b
                    let own = raw[idx]
                    let mutable sum = own
                    let visited = HashSet<struct (int * int)>()

                    let addNeighbor (targetOpt: string option) qa qb =
                        match targetOpt with
                        | None -> ()
                        | Some qName ->
                            match nameToIdx.TryGetValue qName with
                            | true, qIdx when patchHasTris[qIdx] ->
                                let qv = vIdx qa qb

                                if visited.Add(struct (qIdx, qv)) then
                                    sum <- sum + patchRawNormals[qIdx][qv]
                            | _ -> ()

                    if a = 0 then
                        addNeighbor nb.Top sDiv b

                    if a = sDiv then
                        addNeighbor nb.Bottom 0 b

                    if b = 0 then
                        addNeighbor nb.Left a tDiv

                    if b = tDiv then
                        addNeighbor nb.Right a 0

                    if a = 0 && b = 0 then
                        addNeighbor nb.TopLeft sDiv tDiv

                    if a = 0 && b = tDiv then
                        addNeighbor nb.TopRight sDiv 0

                    if a = sDiv && b = 0 then
                        addNeighbor nb.BottomLeft 0 tDiv

                    if a = sDiv && b = tDiv then
                        addNeighbor nb.BottomRight 0 0

                    let averaged =
                        if scaleSafeLength sum > 1e-12 then
                            tryNormalize sum |> Option.defaultValue own
                        else
                            own

                    outNormals.Add averaged

            for t in patchTris[pIdx] do
                outTriangles.Add(baseVertex + t)

    { Positions = outPositions.ToArray()
      Normals = outNormals.ToArray()
      Triangles = outTriangles.ToArray() }
