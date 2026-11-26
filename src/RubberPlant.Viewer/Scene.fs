/// Pure CPU scene preparation: double-precision origin-centered GPU vertex/index
/// buffers and auto-scaled ground grid geometry (docs/viewer.md §4).
module RubberPlant.Viewer.Scene

open System
open RubberPlant.Geometry
open RubberPlant.Backends.Mesh

/// Contiguous slice of the unified uint32 index buffer sharing one palette color.
type DrawRange =
    { Color: int
      FirstIndex: int
      IndexCount: int }

/// Origin-centered CPU buffers ready for single-shot OpenGL upload (§4.1).
type MeshScene =
    { /// Interleaved float32[]: 6 floats per vertex (px, py, pz, nx, ny, nz).
      Vertices: float32[]
      /// 32-bit triangle indices across all draw ranges.
      Indices: uint32[]
      /// Up to 8 ranges in ascending Color order.
      Ranges: DrawRange list
      /// World-space AABB center subtracted in double precision before float32 cast.
      Center: Vec3
      /// World-space AABB extent (max - min).
      Extent: Vec3
      /// Centered local-space bounds for camera framing.
      Bounds: Camera.Bounds
      /// True when `mesh.Groups` has no drawable triangles.
      IsEmpty: bool }

/// Auto-scaled ground grid on the world y = 0 plane shifted into local GPU space (§4.2).
type GridGeometry =
    { /// Interleaved float32[]: 6 floats per vertex (px, py, pz, r, g, b) in sRGB [0, 1].
      Vertices: float32[]
      /// Number of vertices (2 per GL_LINES segment).
      VertexCount: int
      /// Grid cell spacing in world units.
      Step: float
      /// Max Euclidean distance from local (0, 0, 0) to the four grid corners.
      Extent: float }

/// Shared world-space reference frame and fixed width scale across an animation
/// sequence (docs/v0.3-organs-animation.md §5.3).
type ReferenceFrame =
    { Center: Vec3
      Extent: Vec3
      Bounds: Camera.Bounds
      WidthScale: float }

let emptyScene =
    { Vertices = [||]
      Indices = [||]
      Ranges = []
      Center = Vec3.zero
      Extent = Vec3.zero
      Bounds = Camera.emptyBounds
      IsEmpty = true }

type private ValidatedGroups =
    { Groups: MeshGroup list
      TotalVerts: int64
      TotalIndices: int64
      Min: Vec3
      Max: Vec3 }

let private validateMeshGroups (mesh: Mesh) : Result<ValidatedGroups option, string> =
    let groups =
        mesh.Groups
        |> List.filter (fun g -> g.Positions.Length > 0 && g.Triangles.Length > 0)
        |> List.sortBy (fun g -> g.Color)

    if groups.IsEmpty then
        Ok None
    else
        let mutable totalVerts = 0L
        let mutable totalIndices = 0L
        let mutable minX = infinity
        let mutable minY = infinity
        let mutable minZ = infinity
        let mutable maxX = -infinity
        let mutable maxY = -infinity
        let mutable maxZ = -infinity
        let mutable errorMsg: string option = None

        for g in groups do
            if errorMsg.IsNone then
                if g.Positions.Length <> g.Normals.Length then
                    errorMsg <- Some "mesh group position and normal counts disagree"
                elif g.Triangles.Length % 3 <> 0 then
                    errorMsg <- Some "mesh group triangle index count is not a multiple of 3"
                else
                    totalVerts <- totalVerts + int64 g.Positions.Length
                    totalIndices <- totalIndices + int64 g.Triangles.Length

                    for i = 0 to g.Positions.Length - 1 do
                        let p = g.Positions[i]
                        let n = g.Normals[i]

                        if
                            not (Vec3.isFinite p)
                            || not (Vec3.isFinite n)
                            || not (Single.IsFinite(float32 n.X) && Single.IsFinite(float32 n.Y) && Single.IsFinite(float32 n.Z))
                        then
                            errorMsg <- Some "mesh contains non-finite vertex data"
                        else
                            if p.X < minX then minX <- p.X
                            if p.Y < minY then minY <- p.Y
                            if p.Z < minZ then minZ <- p.Z
                            if p.X > maxX then maxX <- p.X
                            if p.Y > maxY then maxY <- p.Y
                            if p.Z > maxZ then maxZ <- p.Z

                    if errorMsg.IsNone then
                        for i = 0 to g.Triangles.Length - 1 do
                            let idx = g.Triangles[i]

                            if idx < 0 || idx >= g.Positions.Length then
                                errorMsg <- Some "mesh group contains out-of-range triangle index"

        match errorMsg with
        | Some err -> Error err
        | None when totalVerts > int64 UInt32.MaxValue || totalVerts * 6L > int64 Array.MaxLength ->
            Error "mesh exceeds maximum GPU vertex buffer size"
        | None when totalIndices > int64 Array.MaxLength ->
            Error "mesh exceeds maximum GPU index buffer size"
        | None ->
            Ok(
                Some
                    { Groups = groups
                      TotalVerts = totalVerts
                      TotalIndices = totalIndices
                      Min = Vec3.create minX minY minZ
                      Max = Vec3.create maxX maxY maxZ }
            )

let private packMeshGroups
    (v: ValidatedGroups)
    (center: Vec3)
    (extent: Vec3)
    (bounds: Camera.Bounds)
    : Result<MeshScene, string> =
    let vertices = Array.zeroCreate<float32> (int (v.TotalVerts * 6L))
    let indices = Array.zeroCreate<uint32> (int v.TotalIndices)
    let ranges = ResizeArray<DrawRange>(v.Groups.Length)
    let mutable vOffset = 0
    let mutable iOffset = 0
    let mutable overflowed = false

    for g in v.Groups do
        let baseVertex = uint32 vOffset

        for i = 0 to g.Positions.Length - 1 do
            let p = g.Positions[i]
            let n = g.Normals[i]
            let lx = float32 (p.X - center.X)
            let ly = float32 (p.Y - center.Y)
            let lz = float32 (p.Z - center.Z)

            if not (Single.IsFinite lx && Single.IsFinite ly && Single.IsFinite lz) then
                overflowed <- true

            let o = (vOffset + i) * 6
            vertices[o] <- lx
            vertices[o + 1] <- ly
            vertices[o + 2] <- lz
            vertices[o + 3] <- float32 n.X
            vertices[o + 4] <- float32 n.Y
            vertices[o + 5] <- float32 n.Z

        let firstIndex = iOffset

        for i = 0 to g.Triangles.Length - 1 do
            indices[iOffset + i] <- baseVertex + uint32 g.Triangles[i]

        ranges.Add
            { Color = g.Color
              FirstIndex = firstIndex
              IndexCount = g.Triangles.Length }

        vOffset <- vOffset + g.Positions.Length
        iOffset <- iOffset + g.Triangles.Length

    if overflowed then
        Error "mesh extent exceeds single-precision GPU range"
    else
        Ok
            { Vertices = vertices
              Indices = indices
              Ranges = List.ofSeq ranges
              Center = center
              Extent = extent
              Bounds = bounds
              IsEmpty = false }

let private frameGeometryOfAabb (minP: Vec3) (maxP: Vec3) : Result<Vec3 * Vec3 * Camera.Bounds, string> =
    // Overflow-safe midpoint and half-extents in double precision (§4.1).
    let cx = 0.5 * minP.X + 0.5 * maxP.X
    let cy = 0.5 * minP.Y + 0.5 * maxP.Y
    let cz = 0.5 * minP.Z + 0.5 * maxP.Z
    let hx = 0.5 * maxP.X - 0.5 * minP.X
    let hy = 0.5 * maxP.Y - 0.5 * minP.Y
    let hz = 0.5 * maxP.Z - 0.5 * minP.Z
    let center = Vec3.create cx cy cz
    let half = Vec3.create hx hy hz

    if
        not (Vec3.isFinite center)
        || not (Vec3.isFinite half)
        || not (Single.IsFinite(float32 hx))
        || not (Single.IsFinite(float32 hy))
        || not (Single.IsFinite(float32 hz))
    then
        Error "mesh extent exceeds single-precision GPU range"
    else
        let extent = Vec3.create (2.0 * hx) (2.0 * hy) (2.0 * hz)
        Ok(center, extent, Camera.boundsOfHalfExtents half)

/// Validates a mesh and accumulates its world-space AABB into `acc` (for Pass 1 of `sequence`).
let validateAndAccumulateMeshBounds
    (mesh: Mesh)
    (acc: (Vec3 * Vec3) option)
    : Result<(Vec3 * Vec3) option, string> =
    validateMeshGroups mesh
    |> Result.map (function
        | None -> acc
        | Some v ->
            match acc with
            | None -> Some(v.Min, v.Max)
            | Some(aMin, aMax) ->
                let minP = Vec3.create (min aMin.X v.Min.X) (min aMin.Y v.Min.Y) (min aMin.Z v.Min.Z)
                let maxP = Vec3.create (max aMax.X v.Max.X) (max aMax.Y v.Max.Y) (max aMax.Z v.Max.Z)
                Some(minP, maxP))

/// Constructs a `ReferenceFrame` from an accumulated world-space AABB and fixed `WidthScale`.
let referenceFrameOfBounds (widthScale: float) (boundsOpt: (Vec3 * Vec3) option) : Result<ReferenceFrame, string> =
    match boundsOpt with
    | None ->
        Ok
            { Center = Vec3.zero
              Extent = Vec3.zero
              Bounds = Camera.emptyBounds
              WidthScale = widthScale }
    | Some(minP, maxP) ->
        frameGeometryOfAabb minP maxP
        |> Result.map (fun (center, extent, bounds) ->
            { Center = center
              Extent = extent
              Bounds = bounds
              WidthScale = widthScale })

/// Packs a backend `Mesh` into origin-centered GPU buffers (§4.1).
/// Returns `Error` if coordinates are non-finite or centered half-extents exceed float32.
let fromMesh (mesh: Mesh) : Result<MeshScene, string> =
    validateMeshGroups mesh
    |> Result.bind (function
        | None -> Ok emptyScene
        | Some v ->
            frameGeometryOfAabb v.Min v.Max
            |> Result.bind (fun (center, extent, bounds) -> packMeshGroups v center extent bounds))

/// Packs a backend `Mesh` centered against a shared `ReferenceFrame`, preserving
/// `frame.Center`, `frame.Extent`, and `frame.Bounds` even when `mesh.Groups` is empty
/// (docs/v0.3-organs-animation.md §5.3).
let fromMeshWithFrame (frame: ReferenceFrame) (mesh: Mesh) : Result<MeshScene, string> =
    if not (Vec3.isFinite frame.Center && Vec3.isFinite frame.Extent) then
        Error "reference frame contains non-finite coordinates"
    else
        validateMeshGroups mesh
        |> Result.bind (function
            | None ->
                Ok
                    { Vertices = [||]
                      Indices = [||]
                      Ranges = []
                      Center = frame.Center
                      Extent = frame.Extent
                      Bounds = frame.Bounds
                      IsEmpty = true }
            | Some v -> packMeshGroups v frame.Center frame.Extent frame.Bounds)

// Ground grid sRGB line colors (§4.2, tuned for #f2f2f0 background).
let private minorColor = struct (0.847f, 0.847f, 0.831f) // #d8d8d4
let private majorColor = struct (0.722f, 0.722f, 0.698f) // #b8b8b2
let private xAxisColor = struct (0.784f, 0.439f, 0.439f) // #c87070 (muted red along z = 0)
let private zAxisColor = struct (0.420f, 0.557f, 0.769f) // #6b8ec4 (muted blue along x = 0)

/// Smallest `step` in `{1, 2, 5} * 10^k` such that `2.5 * span / step <= 16.0` (§4.2).
/// Returns `nan` if `span` is not finite and positive.
let niceGridStep (span: float) : float =
    if not (Double.IsFinite span) || span <= 0.0 then
        nan
    else
        let fits (step: float) =
            if not (Double.IsFinite step) || step <= 0.0 then
                false
            elif span > 1e300 then
                (span / 16.0) * 2.5 <= step
            else
                2.5 * span / step <= 16.0

        // Compute log10(2.5 * span / 16.0) via addition so tiny subnormal or huge spans cannot underflow/overflow.
        let rawExp = Math.Floor(Math.Log10 span + Math.Log10(2.5 / 16.0))
        let mutable exp = max -323 (min 308 (int rawExp))

        while exp > -323 && fits (5.0 * Math.Pow(10.0, float (exp - 1))) do
            exp <- exp - 1

        while exp < 308 && not (fits (10.0 * Math.Pow(10.0, float exp))) do
            exp <- exp + 1

        let b = Math.Pow(10.0, float exp)

        if fits (1.0 * b) then 1.0 * b
        elif fits (2.0 * b) then 2.0 * b
        elif fits (5.0 * b) then 5.0 * b
        elif fits (10.0 * b) then 10.0 * b
        else nan

/// Builds the auto-scaled ground grid on the world `y = 0` plane shifted into local GPU
/// coordinates, or `None` if `|center.Y|` or any endpoint exceeds finite `float32` (§4.2).
let buildGrid (scene: MeshScene) : GridGeometry option =
    let span =
        if scene.Extent = Vec3.zero then
            1.0
        else
            let s = max scene.Extent.X (max scene.Extent.Z (scene.Extent.Y * 0.5))
            if s > 0.0 && Double.IsFinite s then s else 1.0

    let step = niceGridStep span

    if not (Double.IsFinite step && step > 0.0) then
        None
    else
        let cells = max 1 (int (Math.Ceiling(1.25 * span / step)))
        let half = float cells * step

        let qx = scene.Center.X / step
        let qz = scene.Center.Z / step
        let exactX = Double.IsFinite qx && abs qx < 1e15
        let exactZ = Double.IsFinite qz && abs qz < 1e15

        let baseIx = if exactX then int64 (Math.Round(qx, MidpointRounding.AwayFromZero)) else 0L
        let baseIz = if exactZ then int64 (Math.Round(qz, MidpointRounding.AwayFromZero)) else 0L

        // Compute local offsets directly so large |center.X| / |center.Z| never lose `half` to cancellation.
        let offsetX = if exactX then float baseIx * step - scene.Center.X else 0.0
        let offsetZ = if exactZ then float baseIz * step - scene.Center.Z else 0.0

        let lx0 = offsetX - half
        let lx1 = offsetX + half
        let ly = -scene.Center.Y
        let lz0 = offsetZ - half
        let lz1 = offsetZ + half

        let flx0 = float32 lx0
        let flx1 = float32 lx1
        let fly = float32 ly
        let flz0 = float32 lz0
        let flz1 = float32 lz1

        if
            not (
                Double.IsFinite lx0
                && Double.IsFinite lx1
                && Double.IsFinite ly
                && Double.IsFinite lz0
                && Double.IsFinite lz1
                && Single.IsFinite flx0
                && Single.IsFinite flx1
                && Single.IsFinite fly
                && Single.IsFinite flz0
                && Single.IsFinite flz1
            )
        then
            None
        else
            let lineCountPerAxis = 2 * cells + 1
            let vertexCount = lineCountPerAxis * 2 * 2
            let verts = Array.zeroCreate<float32> (vertexCount * 6)
            let mutable o = 0

            let inline pushLine (x0: float32) (z0: float32) (x1: float32) (z1: float32) (struct (r, g, b)) =
                verts[o] <- x0
                verts[o + 1] <- fly
                verts[o + 2] <- z0
                verts[o + 3] <- r
                verts[o + 4] <- g
                verts[o + 5] <- b
                verts[o + 6] <- x1
                verts[o + 7] <- fly
                verts[o + 8] <- z1
                verts[o + 9] <- r
                verts[o + 10] <- g
                verts[o + 11] <- b
                o <- o + 12

            // Lines parallel to Z (constant X).
            for i = -cells to cells do
                let lx, color =
                    if exactX then
                        let ix = baseIx + int64 i
                        let c =
                            if ix = 0L then zAxisColor
                            elif ix % 5L = 0L then majorColor
                            else minorColor

                        float32 (float ix * step - scene.Center.X), c
                    else
                        let c = if i % 5 = 0 then majorColor else minorColor
                        float32 (float i * step), c

                pushLine lx flz0 lx flz1 color

            // Lines parallel to X (constant Z).
            for j = -cells to cells do
                let lz, color =
                    if exactZ then
                        let iz = baseIz + int64 j
                        let c =
                            if iz = 0L then xAxisColor
                            elif iz % 5L = 0L then majorColor
                            else minorColor

                        float32 (float iz * step - scene.Center.Z), c
                    else
                        let c = if j % 5 = 0 then majorColor else minorColor
                        float32 (float j * step), c

                pushLine flx0 lz flx1 lz color

            let cornerExtent x z = Camera.scaleSafeLength (Vec3.create x ly z)

            let extent =
                max
                    (max (cornerExtent lx0 lz0) (cornerExtent lx1 lz0))
                    (max (cornerExtent lx0 lz1) (cornerExtent lx1 lz1))

            Some
                { Vertices = verts
                  VertexCount = vertexCount
                  Step = step
                  Extent = extent }
