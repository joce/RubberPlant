/// Unified swept-contour mesh builder for generalized cylinders (v0.3-organs-animation.md §3.2).
/// Supports closed circular (`Circle`) and open (`Flat`, `UCup`, `VFold`) contours with zero-trig
/// parallel transport, bit-identical cross-platform math, and clean palette-boundary handling.
module RubberPlant.Backends.SweptMesh

open System
open RubberPlant
open RubberPlant.Geometry

/// Vertices and triangles being collected for one palette entry.
type internal GroupBuilder() =
    member val Positions = ResizeArray<Vec3>()
    member val Normals = ResizeArray<Vec3>()
    member val Triangles = ResizeArray<int>()

/// v scaled by 2^-e, where 2^e <= max |component| < 2^(e+1); and e. v must not be 0.
let internal scaled (v: Vec3) =
    let m = max (abs v.X) (max (abs v.Y) (abs v.Z))
    let e = Math.ILogB m
    Vec3.create (Math.ScaleB(v.X, -e)) (Math.ScaleB(v.Y, -e)) (Math.ScaleB(v.Z, -e)), e

/// True when the direct computation is safe from overflow/underflow.
let internal isSafe (v: Vec3) =
    let m = max (abs v.X) (max (abs v.Y) (abs v.Z))
    m > 1e-150 && m < 1e150

/// v / |v|, without overflow or underflow. v must not be 0.
let internal normalize (v: Vec3) =
    if isSafe v then
        (1.0 / Vec3.length v) * v
    else
        let w, _ = scaled v
        (1.0 / Vec3.length w) * w

/// (cos, sin) of 360° j / s, for j = 0 … s − 1.
let internal ringDirections (sides: int) =
    Array.init sides (fun j ->
        let struct (s, c) = Builtins.sinCosDeg (360.0 * float j / float sides)
        struct (c, s))

let private xAxis = Vec3.create 1.0 0.0 0.0
let private yAxis = Vec3.create 0.0 1.0 0.0
let private zAxis = Vec3.create 0.0 0.0 1.0

let private safeDiff (a: Vec3) (b: Vec3) =
    let d = b - a
    if Vec3.isFinite d then d else 0.5 * b - 0.5 * a

/// Constructs a right-handed orthonormal pair (nL, nU) perpendicular to unit `t`
/// using `hintL` (then `hintU`, then the least-aligned world axis) as reference.
let private orthonormalPerp (t: Vec3) (hintL: Vec3) (hintU: Vec3) : struct (Vec3 * Vec3) =
    let tryProj (v: Vec3) =
        if v = Vec3.zero || not (Vec3.isFinite v) then
            None
        else
            let vn = normalize v
            let p = vn - Vec3.dot vn t * t

            if p <> Vec3.zero && Vec3.length p > 1e-12 then
                Some(normalize p)
            else
                None

    let nl =
        match tryProj hintL with
        | Some p -> p
        | None ->
            match tryProj hintU with
            | Some q ->
                let c = Vec3.cross q t
                if c <> Vec3.zero then normalize c else xAxis
            | None ->
                let ax, ay, az = abs t.X, abs t.Y, abs t.Z

                let a =
                    if ax <= ay && ax <= az then xAxis
                    elif ay <= az then yAxis
                    else zAxis

                normalize (Vec3.cross a t)

    let nu = normalize (Vec3.cross t nl)
    struct (nl, nu)

/// Zero-trig 5-FLOP minimal-rotation transport of (nL, nU) from unit `t0` onto unit `t1`.
let private transportFrame (t0: Vec3) (t1: Vec3) (nL: Vec3) (nU: Vec3) : struct (Vec3 * Vec3) =
    let denom = 1.0 + Vec3.dot t0 t1

    if denom > 1e-12 then
        let k = Vec3.dot nL t1 / denom
        let nl' = nL - k * (t0 + t1)

        if nl' <> Vec3.zero && Vec3.length nl' > 1e-12 then
            let nlUnit = normalize nl'
            struct (nlUnit, normalize (Vec3.cross t1 nlUnit))
        else
            orthonormalPerp t1 nL nU
    else
        orthonormalPerp t1 nL nU

let private uCupPointCount (s: int) =
    let r = max 5 (s / 2 + 1)
    if r % 2 = 0 then r + 1 else r

let private depthOf =
    function
    | UCup d
    | VFold d -> d
    | Circle
    | Flat -> 0.0

type private StripSpec =
    { Closed: bool
      Count: int
      PointAt: float -> int -> struct (float * float) }

let private stripsFor (s: int) (contour: ContourKind) : bool * StripSpec list =
    match contour with
    | Circle ->
        let ring = ringDirections s

        let spec =
            { Closed = true
              Count = s
              PointAt = fun _ j ->
                  let struct (c, sn) = ring[j]
                  struct (0.5 * c, 0.5 * sn) }

        true, [ spec ]
    | Flat ->
        let spec =
            { Closed = false
              Count = 2
              PointAt = fun _ j -> if j = 0 then struct (0.5, 0.0) else struct (-0.5, 0.0) }

        false, [ spec ]
    | UCup _ ->
        let m = uCupPointCount s
        let denom = float (m - 1)

        let spec =
            { Closed = false
              Count = m
              PointAt = fun depth j ->
                  let u = 1.0 - 2.0 * float j / denom
                  struct (0.5 * u, 0.5 * depth * u * u) }

        false, [ spec ]
    | VFold _ ->
        let s0 =
            { Closed = false
              Count = 2
              PointAt = fun depth j -> if j = 0 then struct (0.5, 0.5 * depth) else struct (0.0, 0.0) }

        let s1 =
            { Closed = false
              Count = 2
              PointAt = fun depth j -> if j = 0 then struct (0.0, 0.0) else struct (-0.5, 0.5 * depth) }

        false, [ s0; s1 ]

let private isNonDegenerateTri (a: Vec3) (b: Vec3) (c: Vec3) : struct (bool * Vec3) =
    let e1 = b - a
    let e2 = c - a

    if e1 = Vec3.zero || e2 = Vec3.zero then
        struct (false, Vec3.zero)
    else
        let nTri = Vec3.cross e1 e2

        if nTri = Vec3.zero then
            struct (false, Vec3.zero)
        else
            let se1 = if isSafe e1 then e1 else fst (scaled e1)
            let se2 = if isSafe e2 then e2 else fst (scaled e2)
            let sn = Vec3.cross se1 se2
            let l1Sq = Vec3.dot se1 se1
            let l2Sq = Vec3.dot se2 se2
            let nSq = Vec3.dot sn sn

            if nSq > 1e-24 * l1Sq * l2Sq then
                struct (true, nTri)
            else
                struct (false, Vec3.zero)

let private emitStrip
    (groupOf: int -> GroupBuilder)
    (sections: CrossSection[])
    (framesL: Vec3[])
    (framesU: Vec3[])
    (widthScale: float)
    (a: int)
    (b: int)
    (strip: StripSpec)
    =
    let m = b - a + 1
    let ringLen = strip.Count
    let totalVerts = m * ringLen
    let positions = Array.zeroCreate<Vec3> totalVerts

    for k = 0 to m - 1 do
        let sec = sections[a + k]
        let sL = widthScale * sec.Width * sec.ScaleL
        let sU = widthScale * sec.Width * sec.ScaleU
        let d = depthOf sec.Contour
        let nL = framesL[k]
        let nU = framesU[k]
        let row = k * ringLen

        for j = 0 to ringLen - 1 do
            let struct (x, y) = strip.PointAt d j
            positions[row + j] <- sec.P + (x * sL) * nL + (y * sU) * nU

    let allFinite = Array.forall Vec3.isFinite positions
    let quadCount = if strip.Closed then ringLen else ringLen - 1

    if not allFinite then
        // Emit triangles with N_U fallback so Mesh.build catches and reports non-finite coordinates cleanly.
        let normals = Array.init totalVerts (fun idx -> framesU[idx / ringLen])

        for k = 0 to m - 2 do
            let g = groupOf sections[a + k].Color
            let baseIdx = g.Positions.Count

            for v = 0 to 2 * ringLen - 1 do
                let src = k * ringLen + v
                g.Positions.Add positions[src]
                g.Normals.Add normals[src]

            for j = 0 to quadCount - 1 do
                let j' = if strip.Closed then (j + 1) % ringLen else j + 1
                g.Triangles.AddRange [ baseIdx + j; baseIdx + j'; baseIdx + ringLen + j' ]
                g.Triangles.AddRange [ baseIdx + j; baseIdx + ringLen + j'; baseIdx + ringLen + j ]
    else
        // Scale positions into a local frame so area-weighted cross products never overflow or underflow.
        let origin = positions[0]

        let relative =
            let direct = positions |> Array.map (fun p -> p - origin)

            if Array.forall Vec3.isFinite direct then
                direct
            else
                positions |> Array.map (fun p -> 0.5 * p - 0.5 * origin)

        let maxCoord =
            relative
            |> Array.fold (fun acc v -> max acc (max (abs v.X) (max (abs v.Y) (abs v.Z)))) 0.0

        let localPos =
            if maxCoord > 0.0 then
                let e = Math.ILogB maxCoord

                relative
                |> Array.map (fun v ->
                    Vec3.create (Math.ScaleB(v.X, -e)) (Math.ScaleB(v.Y, -e)) (Math.ScaleB(v.Z, -e)))
            else
                relative

        let accumNormals = Array.zeroCreate<Vec3> totalVerts
        let spanTriangles = Array.init (m - 1) (fun _ -> ResizeArray<struct (int * int * int)>())
        let usedVerts = Array.zeroCreate<bool> totalVerts
        let mutable anyTri = false

        for k = 0 to m - 2 do
            let r0 = k * ringLen
            let r1 = (k + 1) * ringLen
            let tris = spanTriangles[k]

            for j = 0 to quadCount - 1 do
                let j' = if strip.Closed then (j + 1) % ringLen else j + 1
                let v00 = r0 + j
                let v01 = r0 + j'
                let v10 = r1 + j
                let v11 = r1 + j'

                let struct (ok1, n1) = isNonDegenerateTri localPos[v00] localPos[v01] localPos[v11]

                if ok1 then
                    accumNormals[v00] <- accumNormals[v00] + n1
                    accumNormals[v01] <- accumNormals[v01] + n1
                    accumNormals[v11] <- accumNormals[v11] + n1
                    tris.Add(struct (v00, v01, v11))
                    usedVerts[v00] <- true
                    usedVerts[v01] <- true
                    usedVerts[v11] <- true
                    anyTri <- true

                let struct (ok2, n2) = isNonDegenerateTri localPos[v00] localPos[v11] localPos[v10]

                if ok2 then
                    accumNormals[v00] <- accumNormals[v00] + n2
                    accumNormals[v11] <- accumNormals[v11] + n2
                    accumNormals[v10] <- accumNormals[v10] + n2
                    tris.Add(struct (v00, v11, v10))
                    usedVerts[v00] <- true
                    usedVerts[v11] <- true
                    usedVerts[v10] <- true
                    anyTri <- true

        if anyTri then
            let hasNormal = Array.zeroCreate<bool> totalVerts
            let normals = Array.zeroCreate<Vec3> totalVerts
            let rowHasNormal = Array.zeroCreate<bool> m
            let mutable needFallback = false

            for k = 0 to m - 1 do
                let row = k * ringLen

                for j = 0 to ringLen - 1 do
                    let idx = row + j

                    if accumNormals[idx] <> Vec3.zero then
                        let n = normalize accumNormals[idx]

                        if Vec3.isFinite n then
                            hasNormal[idx] <- true
                            normals[idx] <- n
                            rowHasNormal[k] <- true

                    if usedVerts[idx] && not hasNormal[idx] then
                        needFallback <- true

            if needFallback then
                let prevSec = Array.create totalVerts -1
                let nextSec = Array.create totalVerts -1

                for k = 0 to m - 1 do
                    let row = k * ringLen

                    for j = 0 to ringLen - 1 do
                        let idx = row + j

                        prevSec[idx] <-
                            if hasNormal[idx] then k
                            elif k > 0 then prevSec[idx - ringLen]
                            else -1

                for k = m - 1 downto 0 do
                    let row = k * ringLen

                    for j = 0 to ringLen - 1 do
                        let idx = row + j

                        nextSec[idx] <-
                            if hasNormal[idx] then k
                            elif k < m - 1 then nextSec[idx + ringLen]
                            else -1

                let fallbackNormal (k: int) (j: int) =
                    let row = k * ringLen
                    let mutable ringSum = Vec3.zero
                    let mutable foundRing = false

                    if rowHasNormal[k] then
                        // 1. Immediate ring neighbors, then wider ring scan on section k.
                        let mutable dist = 1

                        while not foundRing && dist < ringLen do
                            let checkOffset (delta: int) =
                                let jn =
                                    if strip.Closed then
                                        ((j + delta) % ringLen + ringLen) % ringLen
                                    else
                                        j + delta

                                if jn >= 0 && jn < ringLen && hasNormal[row + jn] then
                                    ringSum <- ringSum + normals[row + jn]

                            checkOffset -dist
                            checkOffset dist

                            if ringSum <> Vec3.zero then
                                foundRing <- true

                            dist <- dist + 1

                    if foundRing then
                        normalize ringSum
                    else
                        // 2. Nearest adjacent spine sections at the same contour index j in O(1).
                        let idx = row + j
                        let pSec = prevSec[idx]
                        let nSec = nextSec[idx]

                        if pSec >= 0 && nSec >= 0 then
                            let dPrev = k - pSec
                            let dNext = nSec - k
                            let pN = normals[pSec * ringLen + j]
                            let nN = normals[nSec * ringLen + j]

                            if dPrev < dNext then
                                pN
                            elif dNext < dPrev then
                                nN
                            else
                                let spineSum = pN + nN
                                if spineSum <> Vec3.zero then normalize spineSum else framesU[k]
                        elif pSec >= 0 then
                            normals[pSec * ringLen + j]
                        elif nSec >= 0 then
                            normals[nSec * ringLen + j]
                        else
                            framesU[k]

                for k = 0 to m - 1 do
                    let row = k * ringLen

                    for j = 0 to ringLen - 1 do
                        let idx = row + j

                        if usedVerts[idx] && not hasNormal[idx] then
                            normals[idx] <- fallbackNormal k j

            // Emit each span's triangles into `groupOf section_i.Color`, duplicating vertices
            // at palette boundaries while sharing vertices within contiguous runs of one palette entry.
            let remap = Array.create totalVerts -1
            let lastRun = Array.create totalVerts 0
            let mutable runId = 0
            let mutable currentEntry = -1
            let mutable currentGroup: GroupBuilder option = None

            for k = 0 to m - 2 do
                let tris = spanTriangles[k]

                if tris.Count > 0 then
                    let col = sections[a + k].Color
                    let entry = Palette.entry col

                    if entry <> currentEntry || currentGroup.IsNone then
                        currentEntry <- entry
                        currentGroup <- Some(groupOf col)
                        runId <- runId + 1

                    let g = currentGroup.Value

                    let emitVert (idx: int) =
                        if lastRun[idx] <> runId then
                            lastRun[idx] <- runId
                            let outIdx = g.Positions.Count
                            g.Positions.Add positions[idx]
                            g.Normals.Add normals[idx]
                            remap[idx] <- outIdx

                        remap[idx]

                    for struct (ia, ib, ic) in tris do
                        g.Triangles.Add(emitVert ia)
                        g.Triangles.Add(emitVert ib)
                        g.Triangles.Add(emitVert ic)

/// Builds triangles for a `GeneralizedCylinder` into palette-grouped `GroupBuilder`s (§3.2).
let internal addCylinder
    (groupOf: int -> GroupBuilder)
    (defaultSides: int)
    (widthScale: float)
    (gc: GeneralizedCylinder)
    : unit =
    let filtered = ResizeArray<CrossSection>()

    for s in gc.Sections do
        if filtered.Count = 0 || s.P <> filtered[filtered.Count - 1].P then
            filtered.Add s

    if filtered.Count >= 2 then
        let sections = filtered.ToArray()
        let n = sections.Length
        let dirs = Array.init (n - 1) (fun i -> normalize (safeDiff sections[i].P sections[i + 1].P))
        let sides = if gc.Sides >= 3 then gc.Sides else defaultSides
        let isClosed, strips = stripsFor sides sections[0].Contour

        let emitSubSweep (a: int) (b: int) =
            let m = b - a + 1
            let tangents = Array.zeroCreate<Vec3> m
            tangents[0] <- dirs[a]

            for k = 1 to m - 2 do
                tangents[k] <- normalize (dirs[a + k - 1] + dirs[a + k])

            tangents[m - 1] <- dirs[b - 1]

            let framesL = Array.zeroCreate<Vec3> m
            let framesU = Array.zeroCreate<Vec3> m

            let sectionFrame (sec: CrossSection) (t: Vec3) =
                if sec.H <> Vec3.zero && Vec3.isFinite sec.H && sec.L <> Vec3.zero && Vec3.isFinite sec.L then
                    let h = normalize sec.H
                    let l = normalize sec.L
                    let u = if sec.U <> Vec3.zero && Vec3.isFinite sec.U then normalize sec.U else normalize (Vec3.cross h l)
                    transportFrame h t l u
                else
                    orthonormalPerp t sec.L sec.U

            if isClosed then
                let struct (nl0, nu0) = sectionFrame sections[a] tangents[0]
                framesL[0] <- nl0
                framesU[0] <- nu0

                for k = 1 to m - 1 do
                    let struct (nl, nu) = transportFrame tangents[k - 1] tangents[k] framesL[k - 1] framesU[k - 1]
                    framesL[k] <- nl
                    framesU[k] <- nu
            else
                for k = 0 to m - 1 do
                    let struct (nl, nu) = sectionFrame sections[a + k] tangents[k]
                    framesL[k] <- nl
                    framesU[k] <- nu

            for strip in strips do
                emitStrip groupOf sections framesL framesU widthScale a b strip

        let mutable startIdx = 0

        for i = 1 to n - 2 do
            if Vec3.length (dirs[i - 1] + dirs[i]) < 1e-6 then
                emitSubSweep startIdx i
                startIdx <- i

        emitSubSweep startIdx (n - 1)
