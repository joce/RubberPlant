/// Pure morphology telemetry (core-hardening.md §3): derives deterministic
/// structural and 3D geometry statistics and serializes them to UTF-8 JSON
/// without reflection (Native AOT compatible).
module RubberPlant.Stats

open System
open System.IO
open System.Text
open System.Text.Encodings.Web
open System.Text.Json
open RubberPlant.LString
open RubberPlant.Geometry
open RubberPlant.Derivation
open RubberPlant.Turtle

type BBox =
    { Min: Vec3
      Max: Vec3
      Extent: Vec3 }

type BranchOrderStats =
    { Order: int
      SegmentCount: int
      TotalLength: float
      PolygonCount: int }

type DerivationStats =
    { Steps: int
      Time: float option
      DerivedModules: int
      DrawingModules: int
      MaxBracketDepth: int
      FixedPointReached: bool
      TotalRuleFirings: int
      RuleFirings: RuleFiring list }

type GeometryStats =
    { BBox: BBox option
      Height: float option
      CrownWidth: float option
      AspectRatio: float option
      SegmentCount: int
      PolygonCount: int
      GcCount: int
      SurfaceCount: int
      TotalSegmentLength: float
      WidthMin: float option
      WidthMax: float option
      CenterOfMass: Vec3 option
      Lean: (float * float) option
      BranchOrder: BranchOrderStats list }

type Stats =
    { Derivation: DerivationStats
      Geometry: GeometryStats }

let private normZero (x: float) = if x = 0.0 then 0.0 else x

let private normVec (v: Vec3) =
    Vec3.create (normZero v.X) (normZero v.Y) (normZero v.Z)

/// Euclidean length of `v` using power-of-two scaling (as in Mesh.fs),
/// so intermediate squares neither overflow nor underflow.
let private scaleSafeLength (v: Vec3) : float =
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

/// Midpoint of `(p0, p1)`, avoiding `(p0 + p1) / 2` overflow when finite.
let private midpoint (p0: Vec3) (p1: Vec3) : Vec3 =
    let diff = p1 - p0

    if Vec3.isFinite diff then
        p0 + 0.5 * diff
    else
        0.5 * p0 + 0.5 * p1

let private maxBracketDepthOf (derived: LString) : int =
    let mutable depth = 0
    let mutable maxDepth = 0

    for sym in derived do
        match sym with
        | Push ->
            depth <- depth + 1

            if depth > maxDepth then
                maxDepth <- depth
        | Pop ->
            if depth > 0 then
                depth <- depth - 1
        | Cut
        | Mod _ -> ()

    maxDepth

/// Transforms a canonical surface control point into world space (v0.3-organs-animation.md §4.4).
let private transformSurfacePoint (inst: SurfaceInstance) (q: Vec3) : Vec3 =
    inst.Origin
    + (inst.Scale.X * q.X) * inst.AxisX
    + (inst.Scale.Y * q.Y) * inst.AxisY
    + (inst.Scale.Z * q.Z) * inst.AxisZ

let inline private iterGcSpans (sections: CrossSection list) (f: Vec3 -> Vec3 -> unit) =
    let rec go (prev: CrossSection) (rest: CrossSection list) =
        match rest with
        | [] -> ()
        | cur :: tail ->
            f prev.P cur.P
            go cur tail

    match sections with
    | first :: rest -> go first rest
    | [] -> ()

let private computeCenterOfMass (geometry: Geometry) (totalSegLen: float) (pointCount: int) : Vec3 option =
    if pointCount = 0 then
        None
    elif totalSegLen > 0.0 then
        // Scale lengths by 2^-e where 2^e <= totalSegLen < 2^(e+1) so the scaled
        // total lies in [1, 2). Power-of-two scaling preserves every mantissa bit
        // while avoiding intermediate overflow (1e200) and underflow (1e-200).
        let e = Math.ILogB totalSegLen
        let totalScaled = Math.ScaleB(totalSegLen, -e)
        let mutable sx = 0.0
        let mutable sy = 0.0
        let mutable sz = 0.0

        let addSpan (p0: Vec3) (p1: Vec3) =
            let len = scaleSafeLength (p1 - p0)

            if len > 0.0 then
                let w = Math.ScaleB(len, -e)
                let m = midpoint p0 p1
                sx <- sx + w * m.X
                sy <- sy + w * m.Y
                sz <- sz + w * m.Z

        for prim in geometry do
            match prim with
            | Segment(p0, p1, _, _) -> addSpan p0 p1
            | GeneralizedCylinder gc -> iterGcSpans gc.Sections addSpan
            | Polygon _
            | Surface _ -> ()

        let direct = Vec3.create (sx / totalScaled) (sy / totalScaled) (sz / totalScaled)

        if Vec3.isFinite direct then
            Some(normVec direct)
        else
            // Fallback when even the power-of-two scaled sum exceeds double range
            // (e.g., many segments near 1e308): normalize each weight individually.
            let mutable fx = 0.0
            let mutable fy = 0.0
            let mutable fz = 0.0

            let addSpanFallback (p0: Vec3) (p1: Vec3) =
                let len = scaleSafeLength (p1 - p0)

                if len > 0.0 then
                    let w = len / totalSegLen
                    let m = midpoint p0 p1
                    fx <- fx + w * m.X
                    fy <- fy + w * m.Y
                    fz <- fz + w * m.Z

            for prim in geometry do
                match prim with
                | Segment(p0, p1, _, _) -> addSpanFallback p0 p1
                | GeneralizedCylinder gc -> iterGcSpans gc.Sections addSpanFallback
                | Polygon _
                | Surface _ -> ()

            Some(normVec (Vec3.create fx fy fz))
    else
        let mutable sx = 0.0
        let mutable sy = 0.0
        let mutable sz = 0.0

        let addPt (p: Vec3) =
            sx <- sx + p.X
            sy <- sy + p.Y
            sz <- sz + p.Z

        for prim in geometry do
            match prim with
            | Segment(p0, p1, _, _) ->
                addPt p0
                addPt p1
            | Polygon(vs, _) -> List.iter addPt vs
            | GeneralizedCylinder gc ->
                for s in gc.Sections do
                    addPt s.P
            | Surface inst ->
                for patch in inst.Surface.Patches do
                    for q in patch.ControlPoints do
                        addPt (transformSurfacePoint inst q)

        let fn = float pointCount
        let direct = Vec3.create (sx / fn) (sy / fn) (sz / fn)

        if Vec3.isFinite direct then
            Some(normVec direct)
        else
            let mutable fx = 0.0
            let mutable fy = 0.0
            let mutable fz = 0.0

            let addScaled (p: Vec3) =
                fx <- fx + p.X / fn
                fy <- fy + p.Y / fn
                fz <- fz + p.Z / fn

            for prim in geometry do
                match prim with
                | Segment(p0, p1, _, _) ->
                    addScaled p0
                    addScaled p1
                | Polygon(vs, _) -> List.iter addScaled vs
                | GeneralizedCylinder gc ->
                    for s in gc.Sections do
                        addScaled s.P
                | Surface inst ->
                    for patch in inst.Surface.Patches do
                        for q in patch.ControlPoints do
                            addScaled (transformSurfacePoint inst q)

            Some(normVec (Vec3.create fx fy fz))

/// Pure morphology analyzer (core-hardening.md §3.2).
/// Returns `Error [ RP0205 ]` if any computed floating-point statistic is non-finite.
let analyze
    (derived: LString)
    (drawing: LString)
    (derivationTrace: DerivationTrace)
    (geometry: Geometry)
    (turtleTrace: TurtleTrace)
    : Result<Stats, Diagnostic list> =
    let depths = turtleTrace.PrimitiveDepths

    if depths.Length <> geometry.Length then
        invalidArg (nameof turtleTrace) "TurtleTrace.PrimitiveDepths length must equal Geometry length"

    let mutable maxOrder = -1

    for d in depths do
        if d < 0 then
            invalidArg (nameof turtleTrace) "TurtleTrace.PrimitiveDepths entries must be non-negative"

        if d > maxOrder then
            maxOrder <- d

    let orderCount = maxOrder + 1
    let orderSegs = Array.zeroCreate<int> orderCount
    let orderLen = Array.zeroCreate<float> orderCount
    let orderPolys = Array.zeroCreate<int> orderCount

    let mutable nonFinite = false
    let mutable pointCount = 0
    let mutable minX = infinity
    let mutable minY = infinity
    let mutable minZ = infinity
    let mutable maxX = -infinity
    let mutable maxY = -infinity
    let mutable maxZ = -infinity
    let mutable segCount = 0
    let mutable polyCount = 0
    let mutable gcCount = 0
    let mutable surfaceCount = 0
    let mutable totalSegLen = 0.0
    let mutable hasWidth = false
    let mutable widthMin = infinity
    let mutable widthMax = -infinity

    let time =
        match derivationTrace.Time with
        | Some t ->
            if not (Double.IsFinite t) then
                nonFinite <- true
                None
            else
                Some(normZero t)
        | None -> None

    let visitPoint (p: Vec3) =
        if not (Vec3.isFinite p) then
            nonFinite <- true
        else
            pointCount <- pointCount + 1
            if p.X < minX then minX <- p.X
            if p.Y < minY then minY <- p.Y
            if p.Z < minZ then minZ <- p.Z
            if p.X > maxX then maxX <- p.X
            if p.Y > maxY then maxY <- p.Y
            if p.Z > maxZ then maxZ <- p.Z

    let visitWidth (w: float) =
        hasWidth <- true

        if not (Double.IsFinite w) then
            nonFinite <- true
        else
            if w < widthMin then widthMin <- w
            if w > widthMax then widthMax <- w

    let visitSpan (order: int) (p0: Vec3) (p1: Vec3) =
        let len = scaleSafeLength (p1 - p0)

        if not (Double.IsFinite len) then
            nonFinite <- true
        else
            totalSegLen <- totalSegLen + len
            orderSegs[order] <- orderSegs[order] + 1
            orderLen[order] <- orderLen[order] + len

    let mutable idx = 0

    for prim in geometry do
        let order = depths[idx]
        idx <- idx + 1

        match prim with
        | Segment(p0, p1, w, _) ->
            visitPoint p0
            visitPoint p1
            segCount <- segCount + 1
            visitWidth w
            visitSpan order p0 p1
        | Polygon(vs, _) ->
            List.iter visitPoint vs
            polyCount <- polyCount + 1
            orderPolys[order] <- orderPolys[order] + 1
        | GeneralizedCylinder gc ->
            gcCount <- gcCount + 1

            for s in gc.Sections do
                visitPoint s.P

                if not (Double.IsFinite s.Width && Double.IsFinite s.ScaleL && Double.IsFinite s.ScaleU) then
                    hasWidth <- true
                    nonFinite <- true
                else
                    let w =
                        if s.ScaleL = 1.0 && s.ScaleU = 1.0 then
                            s.Width
                        else
                            s.Width * max s.ScaleL s.ScaleU

                    visitWidth w

            iterGcSpans gc.Sections (visitSpan order)
        | Surface inst ->
            surfaceCount <- surfaceCount + 1

            if
                not (
                    Vec3.isFinite inst.Origin
                    && Vec3.isFinite inst.AxisX
                    && Vec3.isFinite inst.AxisY
                    && Vec3.isFinite inst.AxisZ
                    && Vec3.isFinite inst.Scale
                )
            then
                nonFinite <- true
            else
                for patch in inst.Surface.Patches do
                    for q in patch.ControlPoints do
                        visitPoint (transformSurfacePoint inst q)

    if not (Double.IsFinite totalSegLen) then
        nonFinite <- true

    for o in 0 .. orderCount - 1 do
        if not (Double.IsFinite orderLen[o]) then
            nonFinite <- true

    let bbox =
        if pointCount = 0 then
            None
        else
            let bMin = Vec3.create minX minY minZ
            let bMax = Vec3.create maxX maxY maxZ
            let extent = bMax - bMin

            if not (Vec3.isFinite extent) then
                nonFinite <- true

            Some
                { Min = normVec bMin
                  Max = normVec bMax
                  Extent = normVec extent }

    let height = bbox |> Option.map (fun b -> b.Extent.Y)
    let crownWidth = bbox |> Option.map (fun b -> max b.Extent.X b.Extent.Z)

    let aspectRatio =
        match height, crownWidth with
        | Some h, Some cw when cw > 0.0 ->
            let r = normZero (h / cw)

            if not (Double.IsFinite r) then
                nonFinite <- true

            Some r
        | _ -> None

    let centerOfMass =
        if nonFinite then
            None
        else
            let com = computeCenterOfMass geometry totalSegLen pointCount

            match com with
            | Some c when not (Vec3.isFinite c) ->
                nonFinite <- true
                None
            | other -> other

    let lean =
        match centerOfMass, height with
        | Some c, Some h when h > 0.0 ->
            let lx = normZero (c.X / h)
            let lz = normZero (c.Z / h)

            if not (Double.IsFinite lx && Double.IsFinite lz) then
                nonFinite <- true

            Some(lx, lz)
        | _ -> None

    if nonFinite then
        Error
            [ Diagnostic.error
                  Codes.InvalidGeometry
                  None
                  "morphology statistics contain non-finite values (overflow)" ]
    else
        let branchOrder =
            [ for o in 0 .. orderCount - 1 ->
                  { Order = o
                    SegmentCount = orderSegs[o]
                    TotalLength = normZero orderLen[o]
                    PolygonCount = orderPolys[o] } ]

        let derivationStats =
            { Steps = derivationTrace.Steps
              Time = time
              DerivedModules = derived.Length
              DrawingModules = drawing.Length
              MaxBracketDepth = maxBracketDepthOf derived
              FixedPointReached = derivationTrace.FixedPointReached
              TotalRuleFirings = derivationTrace.TotalRuleFirings
              RuleFirings = derivationTrace.RuleFirings }

        let geometryStats =
            { BBox = bbox
              Height = height
              CrownWidth = crownWidth
              AspectRatio = aspectRatio
              SegmentCount = segCount
              PolygonCount = polyCount
              GcCount = gcCount
              SurfaceCount = surfaceCount
              TotalSegmentLength = normZero totalSegLen
              WidthMin = if hasWidth then Some(normZero widthMin) else None
              WidthMax = if hasWidth then Some(normZero widthMax) else None
              CenterOfMass = centerOfMass
              Lean = lean
              BranchOrder = branchOrder }

        Ok
            { Derivation = derivationStats
              Geometry = geometryStats }

let private compactJsonOptions =
    JsonWriterOptions(Indented = false, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping)

let private prettyJsonOptions =
    JsonWriterOptions(
        Indented = true,
        IndentSize = 2,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    )

/// Serializes `stats` to UTF-8 JSON ending with a single LF (`\n`).
/// Uses `Utf8JsonWriter` directly (reflection-free, Native AOT compatible).
let toJson (pretty: bool) (stats: Stats) : string =
    use stream = new MemoryStream()

    (use w = new Utf8JsonWriter(stream, if pretty then prettyJsonOptions else compactJsonOptions)

     let writeVec3 (name: string) (v: Vec3) =
         w.WriteStartArray name
         w.WriteNumberValue(normZero v.X)
         w.WriteNumberValue(normZero v.Y)
         w.WriteNumberValue(normZero v.Z)
         w.WriteEndArray()

     let writeOptFloat (name: string) (v: float option) =
         match v with
         | Some x -> w.WriteNumber(name, normZero x)
         | None -> w.WriteNull name

     w.WriteStartObject()

     // derivation
     w.WriteStartObject "derivation"
     w.WriteNumber("steps", stats.Derivation.Steps)
     writeOptFloat "time" stats.Derivation.Time
     w.WriteNumber("derived_modules", stats.Derivation.DerivedModules)
     w.WriteNumber("drawing_modules", stats.Derivation.DrawingModules)
     w.WriteNumber("max_bracket_depth", stats.Derivation.MaxBracketDepth)
     w.WriteBoolean("fixed_point_reached", stats.Derivation.FixedPointReached)
     w.WriteNumber("total_rule_firings", stats.Derivation.TotalRuleFirings)
     w.WriteStartArray "rule_firings"

     for rf in stats.Derivation.RuleFirings do
         w.WriteStartObject()
         w.WriteNumber("line", rf.Line)
         w.WriteString("predecessor", rf.Predecessor)
         w.WriteNumber("arity", rf.Arity)
         w.WriteNumber("count", rf.Count)
         w.WriteEndObject()

     w.WriteEndArray()
     w.WriteEndObject()

     // geometry
     let g = stats.Geometry
     w.WriteStartObject "geometry"

     match g.BBox with
     | None -> w.WriteNull "bbox"
     | Some b ->
         w.WriteStartObject "bbox"
         writeVec3 "min" b.Min
         writeVec3 "max" b.Max
         writeVec3 "extent" b.Extent
         w.WriteEndObject()

     writeOptFloat "height" g.Height
     writeOptFloat "crown_width" g.CrownWidth
     writeOptFloat "aspect_ratio" g.AspectRatio
     w.WriteNumber("segment_count", g.SegmentCount)
     w.WriteNumber("polygon_count", g.PolygonCount)
     w.WriteNumber("gc_count", g.GcCount)
     w.WriteNumber("surface_count", g.SurfaceCount)
     w.WriteNumber("total_segment_length", normZero g.TotalSegmentLength)
     writeOptFloat "width_min" g.WidthMin
     writeOptFloat "width_max" g.WidthMax

     match g.CenterOfMass with
     | None -> w.WriteNull "center_of_mass"
     | Some c -> writeVec3 "center_of_mass" c

     match g.Lean with
     | None -> w.WriteNull "lean"
     | Some(lx, lz) ->
         w.WriteStartArray "lean"
         w.WriteNumberValue(normZero lx)
         w.WriteNumberValue(normZero lz)
         w.WriteEndArray()

     w.WriteStartArray "branch_order"

     for bo in g.BranchOrder do
         w.WriteStartObject()
         w.WriteNumber("order", bo.Order)
         w.WriteNumber("segment_count", bo.SegmentCount)
         w.WriteNumber("total_length", normZero bo.TotalLength)
         w.WriteNumber("polygon_count", bo.PolygonCount)
         w.WriteEndObject()

     w.WriteEndArray()
     w.WriteEndObject()

     w.WriteEndObject())

    stream.WriteByte(byte '\n')
    Encoding.UTF8.GetString(stream.ToArray())
