module RubberPlant.Backends.Tests.SvgTests

open System
open System.IO
open Expecto
open RubberPlant
open RubberPlant.Geometry
open RubberPlant.Backends

let private examples = Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "examples")
let private goldenDir = Path.Combine(__SOURCE_DIRECTORY__, "golden")

/// Set RUBBERPLANT_UPDATE_GOLDEN=1 to rewrite the golden files.
let private update = Environment.GetEnvironmentVariable "RUBBERPLANT_UPDATE_GOLDEN" = "1"

let private normalize (s: string) = s.Replace("\r\n", "\n")

let private renderExample (name: string) =
    let source = File.ReadAllText(Path.Combine(examples, name + ".rp"))

    match Pipeline.compile source |> Result.bind (fst >> Pipeline.run) with
    | Ok output -> Svg.render Svg.defaultOptions output.Geometry
    | Error ds -> failtestf "%s:\n%s" name (ds |> List.map (Diagnostic.format name) |> String.concat "\n")

let private golden (name: string) =
    test name {
        let actual = renderExample name
        let path = Path.Combine(goldenDir, name + ".svg")

        if update || not (File.Exists path) then
            File.WriteAllText(path, actual)
        else
            let expected = normalize (File.ReadAllText path)

            if actual <> expected then
                failtestf "%s differs from golden/%s.svg (set RUBBERPLANT_UPDATE_GOLDEN=1 to accept)" name name
    }

let private seg (x0, y0) (x1, y1) =
    Segment(Vec3.create x0 y0 0.0, Vec3.create x1 y1 0.0, 1.0, 0)

let private options =
    { Svg.defaultOptions with
        Size = 120.0
        Margin = 10.0
        Background = None }

[<Tests>]
let tests =
    testList
        "Svg"
        [ testList
              "golden"
              [ for name in [ "koch-island"; "dragon"; "fig1-24a"; "fig1-26-plant"; "fig1-31a"; "fig1-37b"; "stochastic" ] ->
                    golden name ]

          test "fits the drawing to the canvas, +Y up" {
              // Width-1 strokes: padding is the margin plus half a pixel.
              let svg = Svg.render options [ seg (0.0, 0.0) (0.0, 2.0); seg (0.0, 2.0) (1.0, 2.0) ]
              Expect.stringContains svg """width="70.5" height="120" """ "aspect ratio kept"
              Expect.stringContains svg """d="M10.5 109.5 L10.5 10.5 L60 10.5" """ "connected segments share a subpath"
          }

          test "disconnected segments start a new subpath" {
              let svg = Svg.render options [ seg (0.0, 0.0) (0.0, 1.0); seg (1.0, 0.0) (1.0, 1.0) ]
              Expect.stringContains svg """d="M10.5 109.5 L10.5 10.5 M109.5 109.5 L109.5 10.5" """ ""
          }

          test "wide strokes fit in the viewport" {
              let wide = Segment(Vec3.create 0.0 0.0 0.0, Vec3.create 0.0 1.0 0.0, 100.0, 0)
              let svg = Svg.render Svg.defaultOptions [ wide ]
              // Half the stroke (50) plus the margin (10) on each side.
              Expect.stringContains svg """width="120" height="800" """ ""
              Expect.stringContains svg """d="M60 740 L60 60" """ ""
          }

          test "size must exceed twice the margin" {
              for size in [ 1.0; 20.0 ] do
                  Expect.throwsT<System.ArgumentException>
                      (fun () -> Svg.render { options with Size = size } [ seg (0.0, 0.0) (0.0, 1.0) ] |> ignore)
                      $"size %g{size}"

              Svg.render { options with Size = 21.0 } [ seg (0.0, 0.0) (0.0, 1.0) ] |> ignore
          }

          test "style changes start a new path" {
              let svg =
                  Svg.render
                      options
                      [ seg (0.0, 0.0) (0.0, 1.0)
                        Segment(Vec3.create 0.0 1.0 0.0, Vec3.create 0.0 2.0 0.0, 3.0, 1) ]

              Expect.stringContains svg """stroke="#1a1a1a" stroke-width="1" """ ""
              Expect.stringContains svg """stroke="#2e7d32" stroke-width="3" """ ""
          }

          test "polygons are filled" {
              let tri = [ Vec3.create 0.0 0.0 0.0; Vec3.create 1.0 0.0 0.0; Vec3.create 0.0 1.0 0.0 ]
              let svg = Svg.render options [ Polygon(tri, 2) ]
              Expect.stringContains svg """<polygon points="10,110 110,110 10,10" fill="#6d4c41"/>""" "no strokes: no extra padding"
          }

          test "colors wrap around the palette" {
              Expect.equal (Svg.colorOf Svg.palette.Length) (Svg.colorOf 0) ""
              Expect.equal (Svg.colorOf -1) (Svg.colorOf (Svg.palette.Length - 1)) ""
          }

          test "empty geometry" {
              let svg = Svg.render options []
              Expect.stringContains svg "<svg" ""
              Expect.stringContains svg "</svg>" ""
          } ]
