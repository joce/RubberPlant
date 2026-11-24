/// Pure pole-free orbit camera, aspect-aware perspective framing, canonical views,
/// and scale-invariant clip planes (docs/viewer.md §3).
module RubberPlant.Viewer.Camera

open System
open RubberPlant
open RubberPlant.Geometry

/// Vertical field of view in degrees (§3.2).
let fovYDeg = 35.0

/// Framing padding multiplier applied to projected AABB extents (§3.2).
let fitPadding = 1.15

/// Minimum clearance fraction of half-diagonal R in front of the camera (§3.2).
let clearanceFraction = 0.02

/// Default interactive camera angles: 3/4 ISO (§3.3).
let defaultAzimuthDeg = 35.0
let defaultElevationDeg = 25.0

/// Orbit camera parameterized in origin-centered local GPU coordinates (§3.1).
type Camera =
    { Target: Vec3
      AzimuthDeg: float
      ElevationDeg: float
      Distance: float }

/// Orthonormal right-handed camera basis and eye position in local GPU coordinates.
type Basis =
    { /// Unit vector pointing from target toward the camera (camera +Z).
      Dir: Vec3
      /// Unit vector pointing to the right of the viewport (camera +X).
      Right: Vec3
      /// Unit vector pointing toward the top of the viewport (camera +Y).
      Up: Vec3
      /// Eye position: Target + Distance * Dir.
      Pos: Vec3 }

/// Centered local-space AABB `[-HalfExtents, +HalfExtents]` of the loaded mesh.
type Bounds =
    { HalfExtents: Vec3
      /// Half-diagonal |HalfExtents| (0.0 when empty or zero-extent).
      Radius: float
      /// True when the mesh has no drawable groups (`mesh.Groups.IsEmpty`).
      IsEmpty: bool }

/// Canonical contact-sheet / shortcut view (§3.3).
type CanonicalView =
    { Label: string
      AzimuthDeg: float
      ElevationDeg: float }

let frontView = { Label = "FRONT"; AzimuthDeg = 0.0; ElevationDeg = 0.0 }
let sideView = { Label = "SIDE +90"; AzimuthDeg = 90.0; ElevationDeg = 0.0 }
let isoView = { Label = "3/4 ISO"; AzimuthDeg = 35.0; ElevationDeg = 25.0 }
let topView = { Label = "TOP"; AzimuthDeg = 0.0; ElevationDeg = 90.0 }

/// Contact-sheet quadrants in layout order: Top-Left, Top-Right, Bottom-Left, Bottom-Right.
let canonicalViews = [| frontView; sideView; isoView; topView |]

/// Euclidean length using power-of-two scaling so intermediate squares neither
/// overflow nor underflow on extreme coordinates.
let scaleSafeLength (v: Vec3) : float =
    if not (Vec3.isFinite v) then
        infinity
    else
        let m = max (abs v.X) (max (abs v.Y) (abs v.Z))

        if m = 0.0 then
            0.0
        elif m > 1e-150 && m < 1e150 then
            Vec3.length v
        else
            let e = Math.ILogB m
            let sx = Math.ScaleB(v.X, -e)
            let sy = Math.ScaleB(v.Y, -e)
            let sz = Math.ScaleB(v.Z, -e)
            Math.ScaleB(sqrt (sx * sx + sy * sy + sz * sz), e)

let emptyBounds =
    { HalfExtents = Vec3.zero
      Radius = 0.0
      IsEmpty = true }

let boundsOfHalfExtents (halfExtents: Vec3) : Bounds =
    { HalfExtents = halfExtents
      Radius = scaleSafeLength halfExtents
      IsEmpty = false }

/// Half-extent of a centered cube whose half-diagonal is exactly R = 1.
let private unitBoxHalfExtent = 1.0 / sqrt 3.0

/// Analytically constructs the pole-free orthonormal right-handed basis (dir, right, up)
/// for (azDeg, elDeg) using deterministic degree trig (§3.1).
let basisOfAngles (azimuthDeg: float) (elevationDeg: float) : struct (Vec3 * Vec3 * Vec3) =
    let struct (sinAz, cosAz) = Builtins.sinCosDeg azimuthDeg
    let struct (sinEl, cosEl) = Builtins.sinCosDeg elevationDeg
    let dir = Vec3.create (sinAz * cosEl) sinEl (cosAz * cosEl)
    let right = Vec3.create cosAz 0.0 (-sinAz)
    let up = Vec3.cross dir right
    struct (dir, right, up)

/// Full camera basis and eye position for `cam`.
let basis (cam: Camera) : Basis =
    let struct (dir, right, up) = basisOfAngles cam.AzimuthDeg cam.ElevationDeg

    { Dir = dir
      Right = right
      Up = up
      Pos = cam.Target + cam.Distance * dir }

/// Tightest perspective distance keeping all 8 centered AABB corners inside the
/// padded frustum at viewport aspect ratio `aspect = w / h` (§3.2).
let fitDistance (bounds: Bounds) (azimuthDeg: float) (elevationDeg: float) (aspect: float) : float =
    let hx, hy, hz, r =
        if bounds.IsEmpty || bounds.Radius = 0.0 then
            unitBoxHalfExtent, unitBoxHalfExtent, unitBoxHalfExtent, 1.0
        else
            bounds.HalfExtents.X, bounds.HalfExtents.Y, bounds.HalfExtents.Z, bounds.Radius

    let struct (dir, right, up) = basisOfAngles azimuthDeg elevationDeg
    let ty = Builtins.tanDeg (fovYDeg * 0.5)
    let tx = aspect * ty
    let clearance = clearanceFraction * r

    let mutable maxDist = 0.0

    for sx in [| -1.0; 1.0 |] do
        for sy in [| -1.0; 1.0 |] do
            for sz in [| -1.0; 1.0 |] do
                let c = Vec3.create (sx * hx) (sy * hy) (sz * hz)
                let xi = Vec3.dot c right
                let yi = Vec3.dot c up
                let zi = Vec3.dot c dir
                let lateral = max (fitPadding * abs yi / ty) (fitPadding * abs xi / tx)
                let d = zi + max lateral clearance

                if d > maxDist then
                    maxDist <- d

    maxDist

/// Resets target to (0, 0, 0) and fits the camera at (azimuthDeg, elevationDeg) for `aspect` (§3.2).
let fit (bounds: Bounds) (azimuthDeg: float) (elevationDeg: float) (aspect: float) : Camera =
    { Target = Vec3.zero
      AzimuthDeg = azimuthDeg
      ElevationDeg = elevationDeg
      Distance = fitDistance bounds azimuthDeg elevationDeg aspect }

/// Shared physical distance across all four square canonical contact-sheet views (§3.3).
let contactSheetDistance (bounds: Bounds) : float =
    canonicalViews
    |> Array.fold (fun acc v -> max acc (fitDistance bounds v.AzimuthDeg v.ElevationDeg 1.0)) 0.0

/// Signed depth of the closest mesh AABB corner in front of the camera (`distance` when empty).
let frontDepth (bounds: Bounds) (cam: Camera) : float =
    if bounds.IsEmpty then
        cam.Distance
    else
        let struct (dir, _, _) = basisOfAngles cam.AzimuthDeg cam.ElevationDeg
        let hx = bounds.HalfExtents.X
        let hy = bounds.HalfExtents.Y
        let hz = bounds.HalfExtents.Z
        let mutable maxProj = -infinity

        for sx in [| -1.0; 1.0 |] do
            for sy in [| -1.0; 1.0 |] do
                for sz in [| -1.0; 1.0 |] do
                    let c = Vec3.create (sx * hx) (sy * hy) (sz * hz)
                    let proj = Vec3.dot (c - cam.Target) dir

                    if proj > maxProj then
                        maxProj <- proj

        cam.Distance - maxProj

/// Scale-invariant near and far clip planes (§3.4).
/// Pass `Some gridExtent` when the ground grid is visible and non-empty, or `None` otherwise.
let clipPlanes (bounds: Bounds) (cam: Camera) (gridExtent: float option) : struct (float * float) =
    let modelRadius =
        if bounds.IsEmpty || bounds.Radius = 0.0 then
            1.0
        else
            bounds.Radius

    let targetLen = scaleSafeLength cam.Target
    let effectiveRadius = modelRadius + targetLen

    let maxExtent =
        match gridExtent with
        | Some g -> max effectiveRadius (g + targetLen)
        | None -> effectiveRadius

    let far = max (cam.Distance + 2.0 * maxExtent) (cam.Distance * 2.0)
    let fd = frontDepth bounds cam

    let near =
        if fd > 0.0 then
            let meshNear = 0.5 * fd

            let baseNear =
                match gridExtent with
                | Some _ -> min meshNear (cam.Distance * 0.05)
                | None -> meshNear

            let autoSafeFloor = min (cam.Distance * 0.005) (max (cam.Distance * 0.001) (far / 10000.0))
            max baseNear autoSafeFloor
        else
            min (cam.Distance * 0.1) (max (cam.Distance * 0.005) (far / 10000.0))

    struct (near, far)

/// Column-major 4x4 OpenGL view matrix transforming local GPU coordinates to eye space.
let viewMatrix (cam: Camera) : float32[] =
    let b = basis cam
    let tx = -(Vec3.dot b.Right b.Pos)
    let ty = -(Vec3.dot b.Up b.Pos)
    let tz = -(Vec3.dot b.Dir b.Pos)

    [| float32 b.Right.X; float32 b.Up.X; float32 b.Dir.X; 0.0f
       float32 b.Right.Y; float32 b.Up.Y; float32 b.Dir.Y; 0.0f
       float32 b.Right.Z; float32 b.Up.Z; float32 b.Dir.Z; 0.0f
       float32 tx;        float32 ty;     float32 tz;      1.0f |]

/// Column-major 4x4 OpenGL perspective projection matrix (right-handed eye space, NDC z in [-1, 1]).
let projectionMatrix (aspect: float) (near: float) (far: float) : float32[] =
    let f = 1.0 / Builtins.tanDeg (fovYDeg * 0.5)
    let invDepth = 1.0 / (far - near)

    [| float32 (f / aspect); 0.0f;       0.0f;                                    0.0f
       0.0f;                 float32 f;  0.0f;                                    0.0f
       0.0f;                 0.0f;       float32 (-(far + near) * invDepth);      -1.0f
       0.0f;                 0.0f;       float32 ((-2.0 * near) * (far * invDepth)); 0.0f |]

/// Multiplies two column-major 4x4 matrices (`a * b`).
let multiplyMat4 (a: float32[]) (b: float32[]) : float32[] =
    let out = Array.zeroCreate<float32> 16

    for col = 0 to 3 do
        for row = 0 to 3 do
            let mutable sum = 0.0

            for k = 0 to 3 do
                sum <- sum + float a[k * 4 + row] * float b[col * 4 + k]

            out[col * 4 + row] <- float32 sum

    out

/// Combined column-major 4x4 view-projection matrix (`proj * view`).
let viewProjectionMatrix (bounds: Bounds) (cam: Camera) (aspect: float) (gridExtent: float option) : float32[] =
    let struct (near, far) = clipPlanes bounds cam gridExtent
    let proj = projectionMatrix aspect near far
    let view = viewMatrix cam
    multiplyMat4 proj view

/// Transforms a local-space point by a column-major 4x4 matrix, returning clip-space `(x, y, z, w)`.
let transformPoint (m: float32[]) (p: Vec3) : struct (float * float * float * float) =
    let x = float m[0] * p.X + float m[4] * p.Y + float m[8] * p.Z + float m[12]
    let y = float m[1] * p.X + float m[5] * p.Y + float m[9] * p.Z + float m[13]
    let z = float m[2] * p.X + float m[6] * p.Y + float m[10] * p.Z + float m[14]
    let w = float m[3] * p.X + float m[7] * p.Y + float m[11] * p.Z + float m[15]
    struct (x, y, z, w)

/// Orbits the camera by `(deltaAzDeg, deltaElDeg)`, clamping elevation to `[-90°, 90°]` (§6.4).
let orbit (deltaAzDeg: float) (deltaElDeg: float) (cam: Camera) : Camera =
    let el = max -90.0 (min 90.0 (cam.ElevationDeg + deltaElDeg))

    { cam with
        AzimuthDeg = cam.AzimuthDeg + deltaAzDeg
        ElevationDeg = el }

/// Pans the camera target along camera `right` and `up` axes scaled by `cam.Distance` (§6.4).
let pan (deltaRight: float) (deltaUp: float) (cam: Camera) : Camera =
    let b = basis cam
    let shift = (deltaRight * cam.Distance) * b.Right + (deltaUp * cam.Distance) * b.Up
    { cam with Target = cam.Target + shift }

/// Exponentially zooms the camera (`distance *= 0.9^scrollDelta`) clamped to `[minDist, maxDist]` (§6.4).
let zoom (scrollDelta: float) (minDist: float) (maxDist: float) (cam: Camera) : Camera =
    if not (Double.IsFinite scrollDelta) then
        cam
    else
        let factor = Math.Pow(0.9, scrollDelta)
        let raw = cam.Distance * factor

        let clamped =
            if not (Double.IsFinite raw) then
                if scrollDelta < 0.0 then maxDist else minDist
            else
                max minDist (min maxDist raw)

        { cam with Distance = clamped }
