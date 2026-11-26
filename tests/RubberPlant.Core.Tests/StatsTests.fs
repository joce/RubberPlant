module RubberPlant.Tests.StatsTests

open System
open System.Text.Json
open Expecto
open RubberPlant
open RubberPlant.Model
open RubberPlant.Geometry
open RubberPlant.Derivation
open RubberPlant.Turtle
open RubberPlant.Stats
open RubberPlant.Tests.TestUtil

let private compileOk src =
    match Pipeline.compile src with
    | Ok(m, _) -> m
    | Error ds -> failtestf "compile failed:\n%s" (formatDiags ds)

let private analyzeOk (src: string) : Stats.Stats =
    let m = compileOk src

    match Pipeline.run m with
    | Error ds -> failtestf "pipeline failed:\n%s" (formatDiags ds)
    | Ok o ->
        match Stats.analyze o.Derived o.Drawing o.DerivationTrace o.Geometry o.TurtleTrace with
        | Ok s -> s
        | Error ds -> failtestf "analyze failed:\n%s" (formatDiags ds)

let private analyzeErr (src: string) : Diagnostic =
    let m = compileOk src

    match Pipeline.run m with
    | Error ds -> failtestf "pipeline failed:\n%s" (formatDiags ds)
    | Ok o ->
        match Stats.analyze o.Derived o.Drawing o.DerivationTrace o.Geometry o.TurtleTrace with
        | Ok s -> failtestf "expected error, got %A" s
        | Error [ d ] -> d
        | Error ds -> failtestf "expected one error, got:\n%s" (formatDiags ds)

let private closeVec (a: Vec3) (b: Vec3) =
    Expect.floatClose Accuracy.high a.X b.X "x"
    Expect.floatClose Accuracy.high a.Y b.Y "y"
    Expect.floatClose Accuracy.high a.Z b.Z "z"

let derivationTests =
    testList
        "derivation telemetry"
        [ test "derived_modules vs drawing_modules with as rules" {
              let src =
                  """
settings { derivations = 2; }
axiom: A;
A -> A B;
A as F F;
B as + F;
"""

              let s = analyzeOk src
              // Step 0: A (1)
              // Step 1: A B (2)
              // Step 2: A B B (3)
              // Drawing: F F + F + F (6)
              Expect.equal s.Derivation.Steps 2 "steps"
              Expect.equal s.Derivation.DerivedModules 3 "derived_modules"
              Expect.equal s.Derivation.DrawingModules 6 "drawing_modules"
              Expect.equal s.Derivation.TotalRuleFirings 2 "only production rules count, not as rules"
              Expect.equal s.Derivation.RuleFirings.Length 1 "one production rule"
              Expect.equal s.Derivation.RuleFirings[0].Predecessor "A" "pred"
              Expect.equal s.Derivation.RuleFirings[0].Arity 0 "arity"
              Expect.equal s.Derivation.RuleFirings[0].Count 2 "count"
          }

          test "max_bracket_depth measures nesting in Derived" {
              Expect.equal (analyzeOk "axiom: F F;").Derivation.MaxBracketDepth 0 "unbranched"
              Expect.equal (analyzeOk "axiom: F[+F][-F]F;").Derivation.MaxBracketDepth 1 "depth 1"
              Expect.equal (analyzeOk "axiom: F[[F]][F[[[F]]]];").Derivation.MaxBracketDepth 4 "depth 4"
              // Brackets introduced only by `as` rules don't change Derived's max_bracket_depth.
              let s = analyzeOk "axiom: A;\nA as [[F]];"
              Expect.equal s.Derivation.MaxBracketDepth 0 "Derived has no brackets"
              Expect.equal s.Geometry.BranchOrder.Length 3 "Geometry has depth 2"
          }

          test "fixed_point_reached cases" {
              // derivations = 0 -> always false
              let s0 = analyzeOk "settings { derivations = 0; }\naxiom: A;\nA -> B;"
              Expect.isFalse s0.Derivation.FixedPointReached "derivations = 0 is false"
              Expect.equal s0.Derivation.TotalRuleFirings 0 "no firings at step 0"
              Expect.equal s0.Derivation.RuleFirings[0].Count 0 "dead rule still reported"

              // No matching rules on final step -> true
              let sNoMatch = analyzeOk "settings { derivations = 2; }\naxiom: A;\nA -> B;"
              Expect.isTrue sNoMatch.Derivation.FixedPointReached "step 2 matched nothing"
              Expect.equal sNoMatch.Derivation.TotalRuleFirings 1 "fired once on step 1"

              // Identity rule A -> A -> true
              let sIdent = analyzeOk "settings { derivations = 1; }\naxiom: A;\nA -> A;"
              Expect.isTrue sIdent.Derivation.FixedPointReached "identity rule reaches fixed point"
              Expect.equal sIdent.Derivation.TotalRuleFirings 1 "rule still counted as fired"

              // Parametric identity rule A(x) -> A(x) -> true
              let sParamIdent = analyzeOk "settings { derivations = 1; }\naxiom: A(3);\nA(x) -> A(x);"
              Expect.isTrue sParamIdent.Derivation.FixedPointReached "parametric identity reaches fixed point"

              // Not reached: growing grammar
              let sGrow = analyzeOk "settings { derivations = 3; }\naxiom: A;\nA -> A B;"
              Expect.isFalse sGrow.Derivation.FixedPointReached "still growing"

              // Not reached: oscillating grammar (step 2 changes B back to A)
              let sOsc = analyzeOk "settings { derivations = 2; }\naxiom: A;\nA -> B;\nB -> A;"
              Expect.isFalse sOsc.Derivation.FixedPointReached "step 2 changed B to A"

              // Not reached: cut (%) drops itself on the final step
              let sCut1 = analyzeOk "settings { derivations = 1; }\naxiom: [% F];"
              Expect.isFalse sCut1.Derivation.FixedPointReached "cut changed string on step 1"

              let sCut2 = analyzeOk "settings { derivations = 2; }\naxiom: [% F];"
              Expect.isTrue sCut2.Derivation.FixedPointReached "step 2 had nothing to cut"

              // Guarded rule whose guard is false: does not fire and reaches fixed point
              let sFalseGuard = analyzeOk "settings { derivations = 1; }\naxiom: A(0);\nA(x) : when x > 0 -> B;"
              Expect.isTrue sFalseGuard.Derivation.FixedPointReached "false guard leaves string unchanged"
              Expect.equal sFalseGuard.Derivation.TotalRuleFirings 0 "false guard did not fire"
              Expect.equal sFalseGuard.Derivation.RuleFirings[0].Count 0 "rule count is 0"
          }

          test "rule_firings preserves source order including dead and context rules" {
              let src =
                  """settings { derivations = 2; }
axiom: A B(1) C;
Z -> F;
B(x) -> B(x+1);
A < B(x) -> B(x+10);
A -> A;
"""

              let s = analyzeOk src
              // Context rule `A < B(x)` (line 5) takes precedence over `B(x)` (line 4),
              // so line 4 has count 0 while line 5 has count 2; output stays in source order!
              let expected =
                  [ { Line = 3; Predecessor = "Z"; Arity = 0; Count = 0 }
                    { Line = 4; Predecessor = "B"; Arity = 1; Count = 0 }
                    { Line = 5; Predecessor = "B"; Arity = 1; Count = 2 }
                    { Line = 6; Predecessor = "A"; Arity = 0; Count = 2 } ]

              Expect.equal s.Derivation.RuleFirings expected "source order with dead rules"
              Expect.equal s.Derivation.TotalRuleFirings 4 "sum of counts"
          } ]

let geometryTests =
    testList
        "geometry telemetry"
        [ test "2D bounding box, crown_width, aspect_ratio, widths, center_of_mass, lean" {
              // F(4) goes (0,0,0)->(0,4,0), width 4; then -(90) turns right (+X), !(2) F(2) goes (0,4,0)->(2,4,0), width 2.
              let src = "settings { width = 4; }\naxiom: F(4) -(90) !(2) F(2);"
              let s = analyzeOk src
              let g = s.Geometry

              let bbox = Expect.wantSome g.BBox "bbox"
              closeVec bbox.Min (Vec3.create 0.0 0.0 0.0)
              closeVec bbox.Max (Vec3.create 2.0 4.0 0.0)
              closeVec bbox.Extent (Vec3.create 2.0 4.0 0.0)

              Expect.floatClose Accuracy.high (Expect.wantSome g.Height "height") 4.0 "height"
              Expect.floatClose Accuracy.high (Expect.wantSome g.CrownWidth "crown_width") 2.0 "crown_width"
              Expect.floatClose Accuracy.high (Expect.wantSome g.AspectRatio "aspect_ratio") 2.0 "aspect_ratio"
              Expect.equal g.SegmentCount 2 "segments"
              Expect.equal g.PolygonCount 0 "polygons"
              Expect.floatClose Accuracy.high g.TotalSegmentLength 6.0 "total length"
              Expect.equal g.WidthMin (Some 2.0) "width_min"
              Expect.equal g.WidthMax (Some 4.0) "width_max"

              // Length-weighted center of mass:
              // seg1: len=4, mid=(0, 2, 0)
              // seg2: len=2, mid=(1, 4, 0)
              // com = (4*(0,2,0) + 2*(1,4,0)) / 6 = (2/6, 16/6, 0) = (1/3, 8/3, 0)
              let com = Expect.wantSome g.CenterOfMass "com"
              closeVec com (Vec3.create (1.0 / 3.0) (8.0 / 3.0) 0.0)

              // lean = [com.x / height, com.z / height] = [(1/3)/4, 0] = [1/12, 0]
              let lx, lz = Expect.wantSome g.Lean "lean"
              Expect.floatClose Accuracy.high lx (1.0 / 12.0) "lean.x"
              Expect.floatClose Accuracy.high lz 0.0 "lean.z"
          }

          test "3D bounding box where extent.z exceeds extent.x, and 3D lean" {
              // F(2): (0,0,0)->(0,2,0); then ^(90) pitches toward +Z, F(6): (0,2,0)->(0,2,6)
              // Plus [ -(90) F(2) ] from (0,2,0) -> (2,2,0)
              let src = "axiom: F(2) [ -(90) F(2) ] ^(90) F(6);"
              let s = analyzeOk src
              let g = s.Geometry

              let bbox = Expect.wantSome g.BBox "bbox"
              closeVec bbox.Min (Vec3.create 0.0 0.0 0.0)
              closeVec bbox.Max (Vec3.create 2.0 2.0 6.0)
              closeVec bbox.Extent (Vec3.create 2.0 2.0 6.0)

              Expect.floatClose Accuracy.high (Expect.wantSome g.Height "height") 2.0 "height"
              Expect.floatClose Accuracy.high (Expect.wantSome g.CrownWidth "crown_width") 6.0 "max(2, 6) = 6"
              Expect.floatClose Accuracy.high (Expect.wantSome g.AspectRatio "aspect_ratio") (2.0 / 6.0) "aspect"

              // seg1: len=2, mid=(0,1,0)
              // seg2: len=2, mid=(1,2,0)
              // seg3: len=6, mid=(0,2,3)
              // com = (2*(0,1,0) + 2*(1,2,0) + 6*(0,2,3)) / 10 = (0.2, 1.8, 1.8)
              let com = Expect.wantSome g.CenterOfMass "com"
              closeVec com (Vec3.create 0.2 1.8 1.8)

              let lx, lz = Expect.wantSome g.Lean "lean"
              Expect.floatClose Accuracy.high lx 0.1 "lean.x = 0.2 / 2"
              Expect.floatClose Accuracy.high lz 0.9 "lean.z = 1.8 / 2"
          }

          test "vertical segment has crown_width = 0 and aspect_ratio = null" {
              let g = (analyzeOk "axiom: F(5);").Geometry
              Expect.floatClose Accuracy.high (Expect.wantSome g.Height "height") 5.0 "height"
              Expect.floatClose Accuracy.high (Expect.wantSome g.CrownWidth "crown_width") 0.0 "crown_width"
              Expect.isNone g.AspectRatio "aspect_ratio is null when crown_width = 0"
              Expect.equal g.Lean (Some(0.0, 0.0)) "lean is [0, 0]"
          }

          test "horizontal geometry has height = 0, aspect_ratio = 0, and lean = null" {
              let g = (analyzeOk "axiom: +(90) F(4);").Geometry
              Expect.floatClose Accuracy.high (Expect.wantSome g.Height "height") 0.0 "height"
              Expect.floatClose Accuracy.high (Expect.wantSome g.CrownWidth "crown_width") 4.0 "crown_width"
              Expect.floatClose Accuracy.high (Expect.wantSome g.AspectRatio "aspect_ratio") 0.0 "0 / 4 = 0"
              Expect.isNone g.Lean "lean is null when height = 0"
          }

          test "polygon-only geometry falls back to unweighted mean of vertices" {
              // Triangle with vertices (0,0,0), (0,3,0), (-3,3,0) -> mean (-1, 2, 0)
              let g = (analyzeOk "axiom: { . f(3) +(90) f(3) };").Geometry
              Expect.equal g.SegmentCount 0 "no segments"
              Expect.equal g.PolygonCount 1 "one polygon"
              Expect.equal g.TotalSegmentLength 0.0 "zero segment length"
              Expect.isNone g.WidthMin "width_min null"
              Expect.isNone g.WidthMax "width_max null"

              let com = Expect.wantSome g.CenterOfMass "com"
              closeVec com (Vec3.create -1.0 2.0 0.0)

              let lx, lz = Expect.wantSome g.Lean "lean"
              Expect.floatClose Accuracy.high lx (-1.0 / 3.0) "lean.x"
              Expect.floatClose Accuracy.high lz 0.0 "lean.z"
          }

          test "zero-length segments fall back to unweighted mean of geometry points" {
              // f(2) moves to (0,2,0), then F(0) draws zero-length segment at (0,2,0), then +(90) f(4) to (-4,2,0), F(0)
              let g = (analyzeOk "axiom: f(2) F(0) +(90) f(4) F(0);").Geometry
              Expect.equal g.SegmentCount 2 "two segments"
              Expect.equal g.TotalSegmentLength 0.0 "zero total length"
              Expect.equal g.WidthMin (Some 1.0) "width_min still populated"
              Expect.equal g.WidthMax (Some 1.0) "width_max still populated"

              // Points: (0,2,0), (0,2,0), (-4,2,0), (-4,2,0) -> mean (-2, 2, 0)
              let com = Expect.wantSome g.CenterOfMass "com"
              closeVec com (Vec3.create -2.0 2.0 0.0)
              Expect.isNone g.Lean "height is 0 so lean is null"
          }

          test "mixed segments and polygons: bbox includes polygon vertices, center_of_mass weights segments only" {
              // Segment from (0,0,0) to (0,2,0) (mid=(0,1,0), len=2); polygon extends to x=-10 at y=2..4.
              let g = (analyzeOk "axiom: F(2) { . +(90) f(10) -(90) f(2) };").Geometry
              Expect.equal g.SegmentCount 1 "one segment"
              Expect.equal g.PolygonCount 1 "one polygon"

              let bbox = Expect.wantSome g.BBox "bbox"
              closeVec bbox.Min (Vec3.create -10.0 0.0 0.0)
              closeVec bbox.Max (Vec3.create 0.0 4.0 0.0)
              Expect.floatClose Accuracy.high (Expect.wantSome g.Height "height") 4.0 "height includes polygon"
              Expect.floatClose Accuracy.high (Expect.wantSome g.CrownWidth "crown_width") 10.0 "crown_width includes polygon"

              // Since total_segment_length > 0, center_of_mass uses the segment midpoint (0, 1, 0) only.
              let com = Expect.wantSome g.CenterOfMass "com"
              closeVec com (Vec3.create 0.0 1.0 0.0)
          }

          test "empty geometry has all nullable fields null and empty branch_order" {
              let g = (analyzeOk "axiom: + f(3) [ - f(2) ];").Geometry
              Expect.isNone g.BBox "bbox"
              Expect.isNone g.Height "height"
              Expect.isNone g.CrownWidth "crown_width"
              Expect.isNone g.AspectRatio "aspect_ratio"
              Expect.equal g.SegmentCount 0 "segment_count"
              Expect.equal g.PolygonCount 0 "polygon_count"
              Expect.equal g.TotalSegmentLength 0.0 "total_segment_length"
              Expect.isNone g.WidthMin "width_min"
              Expect.isNone g.WidthMax "width_max"
              Expect.isNone g.CenterOfMass "center_of_mass"
              Expect.isNone g.Lean "lean"
              Expect.equal g.BranchOrder [] "branch_order"
          }

          test "empty polygon primitive has branch_order entry but null point metrics" {
              let g = (analyzeOk "axiom: {};").Geometry
              Expect.isNone g.BBox "no points -> bbox null"
              Expect.isNone g.CenterOfMass "no points -> com null"
              Expect.equal g.PolygonCount 1 "1 polygon"
              Expect.equal g.BranchOrder [ { Order = 0; SegmentCount = 0; TotalLength = 0.0; PolygonCount = 1 } ] "order 0"
          }

          test "dense branch_order across nested brackets and polygons" {
              // No primitives at order 0 or 1, one segment at order 2 -> dense 0..2
              let gGap = (analyzeOk "axiom: [ [ F(3) ] ];").Geometry

              Expect.equal
                  gGap.BranchOrder
                  [ { Order = 0; SegmentCount = 0; TotalLength = 0.0; PolygonCount = 0 }
                    { Order = 1; SegmentCount = 0; TotalLength = 0.0; PolygonCount = 0 }
                    { Order = 2; SegmentCount = 1; TotalLength = 3.0; PolygonCount = 0 } ]
                  "dense array fills empty lower orders"

              // Polygon opened at order 1 containing a branch inside it and a nested polygon at order 2
              let src = "axiom: F(2) [ { . f [ + F(1) { . f + f } ] f } ];"
              let g = (analyzeOk src).Geometry

              Expect.equal
                  g.BranchOrder
                  [ { Order = 0; SegmentCount = 1; TotalLength = 2.0; PolygonCount = 0 }
                    { Order = 1; SegmentCount = 0; TotalLength = 0.0; PolygonCount = 1 }
                    { Order = 2; SegmentCount = 1; TotalLength = 1.0; PolygonCount = 1 } ]
                  "polygon order is depth when `{` opened"
          }

          test "scale-safe length and center_of_mass on very large and very small scales" {
              let gBig = (analyzeOk "axiom: F(1e200) -(90) F(1e200);").Geometry
              Expect.floatClose Accuracy.high gBig.TotalSegmentLength 2e200 "no overflow in length"
              let comBig = Expect.wantSome gBig.CenterOfMass "comBig"
              Expect.floatClose Accuracy.high comBig.X 2.5e199 "comBig.X"
              Expect.floatClose Accuracy.high comBig.Y 7.5e199 "comBig.Y"

              let gSmall = (analyzeOk "axiom: F(1e-200) -(90) F(1e-200);").Geometry
              Expect.floatClose Accuracy.high gSmall.TotalSegmentLength 2e-200 "no underflow in length"
              let comSmall = Expect.wantSome gSmall.CenterOfMass "comSmall"
              Expect.floatClose Accuracy.high comSmall.X 2.5e-201 "comSmall.X"
              Expect.floatClose Accuracy.high comSmall.Y 7.5e-201 "comSmall.Y"

              // Exercises the weighted center-of-mass fallback: turn perpendicular so F(1) F(0.5)
              // move along +X with exact total length 1.5 (totalScaled = 1.5), while y = 1.5e308
              // overflows 1.5 * 1.5e308 in the primary scaled sum and normalized weights stay finite.
              let gWeightedFallback = (analyzeOk "axiom: f(1.5e308) -(90) F(1) F(0.5);").Geometry
              Expect.floatClose Accuracy.high gWeightedFallback.TotalSegmentLength 1.5 "weighted total length"
              let comWf = Expect.wantSome gWeightedFallback.CenterOfMass "weighted fallback com"
              // seg1: len=1, mid.x=0.5; seg2: len=0.5, mid.x=1.25 -> (1*0.5 + 0.5*1.25) / 1.5 = 0.75 (unweighted would be 0.875)
              Expect.floatClose Accuracy.high comWf.X 0.75 "weighted fallback com.X"
              Expect.floatClose Accuracy.high comWf.Y 1.5e308 "weighted fallback com.Y"

              // Exercises the unweighted center-of-mass fallback: sum of 4 points at 1.5e308 overflows
              // in the direct sum while per-point division stays finite.
              let gUnweightedFallback = (analyzeOk "axiom: f(1.5e308) F(0) F(0);").Geometry
              Expect.equal gUnweightedFallback.TotalSegmentLength 0.0 "zero total length"
              let comUf = Expect.wantSome gUnweightedFallback.CenterOfMass "unweighted fallback com"
              Expect.floatClose Accuracy.high comUf.Y 1.5e308 "unweighted fallback com.Y"
          }

          test "overflow in derived metrics returns RP0205" {
              // Two opposite segments of 1e308: turtle stays within [-1e308, 1e308],
              // but total_segment_length = 2e308 overflows double.
              let d1 = analyzeErr "axiom: F(1e308) | F(1e308);"
              Expect.equal d1.Code Codes.InvalidGeometry "total length overflow -> RP0205"

              // Extent-only overflow: zero-length segments at +1e308 and -1e308
              // (total_segment_length = 0, but extent.y = 2e308 overflows double).
              let d2 = analyzeErr "axiom: [ f(1e308) F(0) ] | f(1e308) F(0);"
              Expect.equal d2.Code Codes.InvalidGeometry "extent overflow -> RP0205"

              // aspect_ratio overflow: height = 1e308, crown_width = 1e-308 -> height/crown_width = Infinity.
              let d3 = analyzeErr "axiom: [ F(1e308) ] -(90) F(1e-308);"
              Expect.equal d3.Code Codes.InvalidGeometry "aspect_ratio overflow -> RP0205"

              // lean overflow: com.x ~ 5e307, height = 1e-308 -> com.x/height = Infinity.
              let d4 = analyzeErr "axiom: [ -(90) F(1e308) ] F(1e-308);"
              Expect.equal d4.Code Codes.InvalidGeometry "lean overflow -> RP0205"
          }

          test "-0.0 is normalized to 0 in Stats and JSON" {
              let derived: LString.LString = [||]
              let dTrace =
                  { Steps = 0
                    Time = Some -0.0
                    FixedPointReached = false
                    TotalRuleFirings = 0
                    RuleFirings = [] }

              let geom = [ Segment(Vec3.create -0.0 -0.0 -0.0, Vec3.create -0.0 1.0 -0.0, -0.0, 0) ]
              let tTrace = { PrimitiveDepths = [| 0 |] }

              let stats =
                  match Stats.analyze derived derived dTrace geom tTrace with
                  | Ok s -> s
                  | Error ds -> failtestf "unexpected error:\n%s" (formatDiags ds)

              let compact = Stats.toJson false stats
              let pretty = Stats.toJson true stats
              Expect.isFalse (compact.Contains "-0") $"compact JSON should not contain -0: %s{compact}"
              Expect.isFalse (pretty.Contains "-0") $"pretty JSON should not contain -0: %s{pretty}"
              Expect.isFalse (Double.IsNegative (Expect.wantSome stats.Geometry.WidthMin "w")) "width_min +0"
              let lx, lz = Expect.wantSome stats.Geometry.Lean "lean"
              Expect.isFalse (Double.IsNegative lx) "lean.x +0"
              Expect.isFalse (Double.IsNegative lz) "lean.z +0"
          }

          test "toJson formatting: compact single-line LF, pretty 2-space LF, Unicode predecessors" {
              let src = "settings { derivations = 1; }\naxiom: α +;\nα -> F;\n+ -> -;"
              let s = analyzeOk src
              let compact = Stats.toJson false s
              let pretty = Stats.toJson true s

              Expect.isTrue (compact.EndsWith "\n") "compact ends with LF"
              Expect.isFalse (compact.Contains "\r") "compact has no CR"
              Expect.isFalse ((compact.TrimEnd '\n').Contains "\n") "compact is single line"

              Expect.isTrue (pretty.EndsWith "\n") "pretty ends with LF"
              Expect.isFalse (pretty.Contains "\r") "pretty has no CR"
              Expect.stringContains pretty "\n  \"derivation\": {\n" "2-space indented"

              // Parse both with System.Text.Json and verify schema keys and values
              use docCompact = JsonDocument.Parse compact
              use docPretty = JsonDocument.Parse pretty
              let d = docCompact.RootElement.GetProperty "derivation"
              let rules = d.GetProperty "rule_firings"
              Expect.equal (rules.GetArrayLength()) 2 "2 rules"
              Expect.equal (rules[0].GetProperty("predecessor").GetString()) "α" "Unicode preserved"
              Expect.equal (rules[1].GetProperty("predecessor").GetString()) "+" "+ preserved"

              let g = docPretty.RootElement.GetProperty "geometry"
              Expect.equal (g.GetProperty("segment_count").GetInt32()) 1 "segment_count"
              Expect.equal (g.GetProperty("aspect_ratio").ValueKind) JsonValueKind.Null "null aspect_ratio"
          }

          test "toJson emits the exact schema keys for populated and empty geometry" {
              let propNames (e: JsonElement) =
                  [ for p in e.EnumerateObject() -> p.Name ]

              let expectedRoot = [ "derivation"; "geometry" ]

              let expectedDeriv =
                  [ "steps"
                    "time"
                    "derived_modules"
                    "drawing_modules"
                    "max_bracket_depth"
                    "fixed_point_reached"
                    "total_rule_firings"
                    "rule_firings" ]

              let expectedGeom =
                  [ "bbox"
                    "height"
                    "crown_width"
                    "aspect_ratio"
                    "segment_count"
                    "polygon_count"
                    "gc_count"
                    "surface_count"
                    "total_segment_length"
                    "width_min"
                    "width_max"
                    "center_of_mass"
                    "lean"
                    "branch_order" ]

              // Populated model
              let populatedJson = Stats.toJson false (analyzeOk "settings { derivations = 1; }\naxiom: F [ -(90) F ];\nF -> F;")
              use popDoc = JsonDocument.Parse populatedJson
              Expect.equal (propNames popDoc.RootElement) expectedRoot "root keys"
              let popD = popDoc.RootElement.GetProperty "derivation"
              Expect.equal (propNames popD) expectedDeriv "derivation keys"
              Expect.equal (propNames (popD.GetProperty("rule_firings")[0])) [ "line"; "predecessor"; "arity"; "count" ] "rule_firing keys"
              let popG = popDoc.RootElement.GetProperty "geometry"
              Expect.equal (propNames popG) expectedGeom "geometry keys"
              Expect.equal (propNames (popG.GetProperty "bbox")) [ "min"; "max"; "extent" ] "bbox keys"
              Expect.equal (propNames ((popG.GetProperty "branch_order")[0])) [ "order"; "segment_count"; "total_length"; "polygon_count" ] "branch_order keys"

              // Empty geometry model
              let emptyJson = Stats.toJson false (analyzeOk "axiom: + f;")
              use emptyDoc = JsonDocument.Parse emptyJson
              Expect.equal (propNames emptyDoc.RootElement) expectedRoot "empty root keys"
              let emptyD = emptyDoc.RootElement.GetProperty "derivation"
              Expect.equal (propNames emptyD) expectedDeriv "empty derivation keys"
              Expect.equal (emptyD.GetProperty("rule_firings").GetArrayLength()) 0 "empty rule_firings"
              let emptyG = emptyDoc.RootElement.GetProperty "geometry"
              Expect.equal (propNames emptyG) expectedGeom "empty geometry keys"

              for nullable in [ "bbox"; "height"; "crown_width"; "aspect_ratio"; "width_min"; "width_max"; "center_of_mass"; "lean" ] do
                  Expect.equal (emptyG.GetProperty(nullable).ValueKind) JsonValueKind.Null $"%s{nullable} is null"

              Expect.equal (emptyG.GetProperty("branch_order").GetArrayLength()) 0 "empty branch_order"
          }

          test "analyze validates TurtleTrace invariants" {
              let emptyLStr: LString.LString = [||]
              let dTrace = { Steps = 0; Time = None; FixedPointReached = false; TotalRuleFirings = 0; RuleFirings = [] }
              let seg = Segment(Vec3.zero, Vec3.create 0.0 1.0 0.0, 1.0, 0)

              Expect.throwsT<ArgumentException>
                  (fun () -> Stats.analyze emptyLStr emptyLStr dTrace [ seg ] { PrimitiveDepths = [||] } |> ignore)
                  "length mismatch throws ArgumentException"

              Expect.throwsT<ArgumentException>
                  (fun () -> Stats.analyze emptyLStr emptyLStr dTrace [ seg ] { PrimitiveDepths = [| -1 |] } |> ignore)
                  "negative depth throws ArgumentException"
          }

          test "timed trace populates derivation.time and rejects non-finite time" {
              let emptyLStr: LString.LString = [||]

              let okTrace =
                  { Steps = 0
                    Time = Some 2.5
                    FixedPointReached = false
                    TotalRuleFirings = 3
                    RuleFirings = [ { Line = 4; Predecessor = "a"; Arity = 1; Count = 3 } ] }

              let stats =
                  Expect.wantOk (Stats.analyze emptyLStr emptyLStr okTrace [] { PrimitiveDepths = [||] }) "timed stats"

              Expect.equal stats.Derivation.Time (Some 2.5) "time preserved"
              let json = Stats.toJson false stats
              use doc = JsonDocument.Parse json
              Expect.floatClose Accuracy.veryHigh (doc.RootElement.GetProperty("derivation").GetProperty("time").GetDouble()) 2.5 "json time"

              let badTrace = { okTrace with Time = Some Double.PositiveInfinity }

              match Stats.analyze emptyLStr emptyLStr badTrace [] { PrimitiveDepths = [||] } with
              | Error [ d ] -> Expect.equal d.Code Codes.InvalidGeometry "non-finite time -> RP0205"
              | other -> failtestf "expected RP0205, got %A" other
          }

          test "GeneralizedCylinder telemetry: gc_count, spine spans, effective widths, branch_order, and center_of_mass" {
              let mkSection p w sL sU =
                  { P = p
                    H = Vec3.create 0.0 1.0 0.0
                    L = Vec3.create -1.0 0.0 0.0
                    U = Vec3.create 0.0 0.0 1.0
                    Width = w
                    ScaleL = sL
                    ScaleU = sU
                    Color = 0
                    Contour = Circle }

              // Spine: (0,0,0) -> (0,4,0) -> (2,4,0); spans of length 4 and 2.
              // Effective widths: 4*max(1,1)=4, 2*max(1.5,0.5)=3, 1*max(0.5,0.25)=0.5.
              let gc =
                  GeneralizedCylinder
                      { Sides = 8
                        Sections =
                          [ mkSection (Vec3.create 0.0 0.0 0.0) 4.0 1.0 1.0
                            mkSection (Vec3.create 0.0 4.0 0.0) 2.0 1.5 0.5
                            mkSection (Vec3.create 2.0 4.0 0.0) 1.0 0.5 0.25 ] }

              let emptyLStr: LString.LString = [||]
              let dTrace = { Steps = 0; Time = None; FixedPointReached = false; TotalRuleFirings = 0; RuleFirings = [] }

              let stats =
                  Expect.wantOk (Stats.analyze emptyLStr emptyLStr dTrace [ gc ] { PrimitiveDepths = [| 1 |] }) "gc stats"

              let g = stats.Geometry
              Expect.equal g.SegmentCount 0 "segment_count counts Segment primitives"
              Expect.equal g.GcCount 1 "gc_count"
              Expect.equal g.SurfaceCount 0 "surface_count"
              Expect.floatClose Accuracy.high g.TotalSegmentLength 6.0 "spine spans sum to 6"
              Expect.equal g.WidthMin (Some 0.5) "effective width_min"
              Expect.equal g.WidthMax (Some 4.0) "effective width_max"

              let bbox = Expect.wantSome g.BBox "bbox"
              closeVec bbox.Min (Vec3.create 0.0 0.0 0.0)
              closeVec bbox.Max (Vec3.create 2.0 4.0 0.0)

              // Same length-weighted center of mass as the 2-segment test: (1/3, 8/3, 0).
              let com = Expect.wantSome g.CenterOfMass "com"
              closeVec com (Vec3.create (1.0 / 3.0) (8.0 / 3.0) 0.0)

              Expect.equal
                  g.BranchOrder
                  [ { Order = 0; SegmentCount = 0; TotalLength = 0.0; PolygonCount = 0 }
                    { Order = 1; SegmentCount = 2; TotalLength = 6.0; PolygonCount = 0 } ]
                  "GC spine spans attributed to opening depth"

              let json = Stats.toJson false stats
              use doc = JsonDocument.Parse json
              let jg = doc.RootElement.GetProperty "geometry"
              Expect.equal (jg.GetProperty("gc_count").GetInt32()) 1 "json gc_count"
              Expect.equal (jg.GetProperty("surface_count").GetInt32()) 0 "json surface_count"
          }

          test "Surface telemetry: surface_count, world-space control points bbox and fallback center_of_mass" {
              let noNeighbors =
                  { TopLeft = None
                    Top = None
                    TopRight = None
                    Left = None
                    Right = None
                    BottomLeft = None
                    Bottom = None
                    BottomRight = None }

              let surf =
                  { Name = "leaf"
                    DefaultDivisions = (4, 4)
                    EndPoint = Vec3.create 0.0 1.0 0.0
                    Patches =
                      [| { Name = "p0"
                           Neighbors = noNeighbors
                           ControlPoints =
                             [| Vec3.create 0.0 0.0 0.0
                                Vec3.create 1.0 0.0 0.0
                                Vec3.create 0.0 1.0 0.0
                                Vec3.create 1.0 1.0 1.0 |] } |] }

              // Origin=(1,2,3), AxisX=(1,0,0), AxisY=(0,1,0), AxisZ=(0,0,1), Scale=(2,4,6)
              // World points: (1,2,3), (3,2,3), (1,6,3), (3,6,9)
              // BBox: min=(1,2,3), max=(3,6,9), extent=(2,4,6), height=4, crown_width=6
              // Mean: (2, 4, 4.5)
              let inst =
                  Surface
                      { Surface = surf
                        Origin = Vec3.create 1.0 2.0 3.0
                        AxisX = Vec3.create 1.0 0.0 0.0
                        AxisY = Vec3.create 0.0 1.0 0.0
                        AxisZ = Vec3.create 0.0 0.0 1.0
                        Scale = Vec3.create 2.0 4.0 6.0
                        Color = 0 }

              let emptyLStr: LString.LString = [||]
              let dTrace = { Steps = 0; Time = None; FixedPointReached = false; TotalRuleFirings = 0; RuleFirings = [] }

              let stats =
                  Expect.wantOk (Stats.analyze emptyLStr emptyLStr dTrace [ inst ] { PrimitiveDepths = [| 0 |] }) "surface stats"

              let g = stats.Geometry
              Expect.equal g.SegmentCount 0 "no segments"
              Expect.equal g.GcCount 0 "no GCs"
              Expect.equal g.SurfaceCount 1 "one surface"
              Expect.isNone g.WidthMin "no widths"
              Expect.isNone g.WidthMax "no widths"

              let bbox = Expect.wantSome g.BBox "bbox"
              closeVec bbox.Min (Vec3.create 1.0 2.0 3.0)
              closeVec bbox.Max (Vec3.create 3.0 6.0 9.0)
              Expect.floatClose Accuracy.high (Expect.wantSome g.Height "height") 4.0 "height"
              Expect.floatClose Accuracy.high (Expect.wantSome g.CrownWidth "crown_width") 6.0 "crown_width"
              Expect.floatClose Accuracy.high (Expect.wantSome g.AspectRatio "aspect_ratio") (4.0 / 6.0) "aspect_ratio"
              closeVec (Expect.wantSome g.CenterOfMass "com") (Vec3.create 2.0 4.0 4.5)

              // Non-finite surface transform -> RP0205
              let badInst =
                  Surface
                      { Surface = surf
                        Origin = Vec3.create nan 0.0 0.0
                        AxisX = Vec3.create 1.0 0.0 0.0
                        AxisY = Vec3.create 0.0 1.0 0.0
                        AxisZ = Vec3.create 0.0 0.0 1.0
                        Scale = Vec3.create 1.0 1.0 1.0
                        Color = 0 }

              match Stats.analyze emptyLStr emptyLStr dTrace [ badInst ] { PrimitiveDepths = [| 0 |] } with
              | Error [ d ] -> Expect.equal d.Code Codes.InvalidGeometry "non-finite surface -> RP0205"
              | other -> failtestf "expected RP0205, got %A" other
          }

          test "mixed Segment, Polygon, GeneralizedCylinder, and Surface stats and GC edge cases" {
              let mkSection p w sL sU =
                  { P = p
                    H = Vec3.create 0.0 1.0 0.0
                    L = Vec3.create -1.0 0.0 0.0
                    U = Vec3.create 0.0 0.0 1.0
                    Width = w
                    ScaleL = sL
                    ScaleU = sU
                    Color = 0
                    Contour = UCup 0.5 }

              let noNeighbors =
                  { TopLeft = None
                    Top = None
                    TopRight = None
                    Left = None
                    Right = None
                    BottomLeft = None
                    Bottom = None
                    BottomRight = None }

              let surf =
                  { Name = "petal"
                    DefaultDivisions = (4, 4)
                    EndPoint = Vec3.create 0.0 1.0 0.0
                    Patches =
                      [| { Name = "p0"
                           Neighbors = noNeighbors
                           ControlPoints = [| Vec3.create 10.0 0.0 0.0 |] } |] }

              let prims =
                  [ Segment(Vec3.create 0.0 0.0 0.0, Vec3.create 0.0 2.0 0.0, 2.0, 0)
                    Polygon([ Vec3.create -5.0 0.0 0.0; Vec3.create -5.0 1.0 0.0; Vec3.create -4.0 0.0 0.0 ], 1)
                    GeneralizedCylinder
                        { Sides = 6
                          Sections =
                            [ mkSection (Vec3.create 0.0 2.0 0.0) 1.0 1.0 0.5
                              mkSection (Vec3.create 0.0 4.0 0.0) 0.5 1.0 0.5 ] }
                    Surface
                        { Surface = surf
                          Origin = Vec3.zero
                          AxisX = Vec3.create 1.0 0.0 0.0
                          AxisY = Vec3.create 0.0 1.0 0.0
                          AxisZ = Vec3.create 0.0 0.0 1.0
                          Scale = Vec3.create 1.0 1.0 1.0
                          Color = 2 } ]

              let emptyLStr: LString.LString = [||]
              let dTrace = { Steps = 0; Time = None; FixedPointReached = false; TotalRuleFirings = 0; RuleFirings = [] }

              let stats =
                  Expect.wantOk (Stats.analyze emptyLStr emptyLStr dTrace prims { PrimitiveDepths = [| 0; 1; 0; 1 |] }) "mixed"

              let g = stats.Geometry
              Expect.equal g.SegmentCount 1 "1 segment"
              Expect.equal g.PolygonCount 1 "1 polygon"
              Expect.equal g.GcCount 1 "1 gc"
              Expect.equal g.SurfaceCount 1 "1 surface"
              Expect.floatClose Accuracy.high g.TotalSegmentLength 4.0 "segment(2) + gc span(2)"
              Expect.equal g.WidthMin (Some 0.5) "min width from GC"
              Expect.equal g.WidthMax (Some 2.0) "max width from Segment"

              let bbox = Expect.wantSome g.BBox "bbox"
              closeVec bbox.Min (Vec3.create -5.0 0.0 0.0)
              closeVec bbox.Max (Vec3.create 10.0 4.0 0.0)

              // Length-weighted CoM uses segment mid=(0,1,0) len=2 and GC span mid=(0,3,0) len=2 -> (0,2,0).
              closeVec (Expect.wantSome g.CenterOfMass "weighted com") (Vec3.create 0.0 2.0 0.0)

              Expect.equal
                  g.BranchOrder
                  [ { Order = 0; SegmentCount = 2; TotalLength = 4.0; PolygonCount = 0 }
                    { Order = 1; SegmentCount = 0; TotalLength = 0.0; PolygonCount = 1 } ]
                  "branch orders"

              // Zero-length GC falls back to unweighted mean of section points.
              let zeroGc =
                  GeneralizedCylinder
                      { Sides = 6
                        Sections =
                          [ mkSection (Vec3.create 2.0 4.0 6.0) 1.0 1.0 1.0
                            mkSection (Vec3.create 2.0 4.0 6.0) 1.0 1.0 1.0 ] }

              let zStats =
                  Expect.wantOk (Stats.analyze emptyLStr emptyLStr dTrace [ zeroGc ] { PrimitiveDepths = [| 0 |] }) "zero gc"

              closeVec (Expect.wantSome zStats.Geometry.CenterOfMass "zero gc com") (Vec3.create 2.0 4.0 6.0)

              // Non-finite GC width/scale returns RP0205.
              let badGc =
                  GeneralizedCylinder
                      { Sides = 6
                        Sections = [ mkSection Vec3.zero 1.0 Double.PositiveInfinity 1.0 ] }

              match Stats.analyze emptyLStr emptyLStr dTrace [ badGc ] { PrimitiveDepths = [| 0 |] } with
              | Error [ d ] -> Expect.equal d.Code Codes.InvalidGeometry "non-finite GC scale -> RP0205"
              | other -> failtestf "expected RP0205, got %A" other

              // Effective width product overflow (1e200 * 1e200 = +inf) returns RP0205.
              let overflowGc =
                  GeneralizedCylinder
                      { Sides = 6
                        Sections = [ mkSection Vec3.zero 1e200 1e200 1.0 ] }

              match Stats.analyze emptyLStr emptyLStr dTrace [ overflowGc ] { PrimitiveDepths = [| 0 |] } with
              | Error [ d ] -> Expect.equal d.Code Codes.InvalidGeometry "GC effective width overflow -> RP0205"
              | other -> failtestf "expected RP0205, got %A" other

              // Empty GC and empty Surface keep counts while leaving point/width metrics null.
              let emptyGc = GeneralizedCylinder { Sides = 6; Sections = [] }

              let emptySurf =
                  Surface
                      { Surface =
                          { Name = "empty"
                            DefaultDivisions = (4, 4)
                            EndPoint = Vec3.zero
                            Patches = [||] }
                        Origin = Vec3.zero
                        AxisX = Vec3.create 1.0 0.0 0.0
                        AxisY = Vec3.create 0.0 1.0 0.0
                        AxisZ = Vec3.create 0.0 0.0 1.0
                        Scale = Vec3.create 1.0 1.0 1.0
                        Color = 0 }

              let emptyStats =
                  Expect.wantOk
                      (Stats.analyze emptyLStr emptyLStr dTrace [ emptyGc; emptySurf ] { PrimitiveDepths = [| 0; 0 |] })
                      "empty gc and surface"

              Expect.equal emptyStats.Geometry.GcCount 1 "empty gc counted"
              Expect.equal emptyStats.Geometry.SurfaceCount 1 "empty surface counted"
              Expect.isNone emptyStats.Geometry.BBox "no points -> bbox null"
              Expect.isNone emptyStats.Geometry.WidthMin "no sections -> width_min null"

              // Rotated/mirrored/flattened Surface basis and non-finite control point check.
              let rotSurf =
                  Surface
                      { Surface =
                          { Name = "rot"
                            DefaultDivisions = (4, 4)
                            EndPoint = Vec3.zero
                            Patches =
                              [| { Name = "p0"
                                   Neighbors = noNeighbors
                                   ControlPoints = [| Vec3.create 1.0 2.0 3.0; Vec3.create 0.0 0.0 0.0 |] } |] }
                        Origin = Vec3.create 1.0 0.0 0.0
                        AxisX = Vec3.create 0.0 1.0 0.0
                        AxisY = Vec3.create -1.0 0.0 0.0
                        AxisZ = Vec3.create 0.0 0.0 1.0
                        Scale = Vec3.create -2.0 3.0 0.0
                        Color = 0 }

              // q0=(1,2,3) -> (1,0,0) + (-2)*(0,1,0) + (6)*(-1,0,0) + 0*(0,0,1) = (-5, -2, 0)
              // q1=(0,0,0) -> (1, 0, 0)
              let rotStats =
                  Expect.wantOk (Stats.analyze emptyLStr emptyLStr dTrace [ rotSurf ] { PrimitiveDepths = [| 0 |] }) "rot surface"

              let rotBox = Expect.wantSome rotStats.Geometry.BBox "rot bbox"
              closeVec rotBox.Min (Vec3.create -5.0 -2.0 0.0)
              closeVec rotBox.Max (Vec3.create 1.0 0.0 0.0)
              closeVec (Expect.wantSome rotStats.Geometry.CenterOfMass "rot com") (Vec3.create -2.0 -1.0 0.0)

              let badPtSurf =
                  Surface
                      { Surface =
                          { Name = "badpt"
                            DefaultDivisions = (4, 4)
                            EndPoint = Vec3.zero
                            Patches =
                              [| { Name = "p0"
                                   Neighbors = noNeighbors
                                   ControlPoints = [| Vec3.create 0.0 Double.NaN 0.0 |] } |] }
                        Origin = Vec3.zero
                        AxisX = Vec3.create 1.0 0.0 0.0
                        AxisY = Vec3.create 0.0 1.0 0.0
                        AxisZ = Vec3.create 0.0 0.0 1.0
                        Scale = Vec3.create 1.0 1.0 1.0
                        Color = 0 }

              match Stats.analyze emptyLStr emptyLStr dTrace [ badPtSurf ] { PrimitiveDepths = [| 0 |] } with
              | Error [ d ] -> Expect.equal d.Code Codes.InvalidGeometry "non-finite control point -> RP0205"
              | other -> failtestf "expected RP0205, got %A" other
          } ]

[<Tests>]
let tests = testList "Stats" [ derivationTests; geometryTests ]
