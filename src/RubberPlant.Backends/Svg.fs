/// SVG backend: a pure function from geometry to SVG text.
///
/// Projects onto the XY plane (looking down −Z, +Y up) and fits the drawing
/// to the canvas, which replaces ABOP's "decrease d between images" (§5.4).
/// Line widths are in pixels, so they don't change with the drawing's scale.
module RubberPlant.Backends.Svg

open System
open System.Globalization
open System.Text
open RubberPlant.Geometry

type Options =
    { /// Size in pixels of the longer side of the drawing, margins included.
      Size: float
      /// Margin in pixels around the drawing.
      Margin: float
      /// Background fill, or None for transparent.
      Background: string option }

let defaultOptions =
    { Size = 800.0
      Margin = 10.0
      Background = Some "white" }

/// Color index → CSS color. Indices wrap around.
let palette =
    [| "#1a1a1a" // 0: near black
       "#2e7d32" // 1: green
       "#6d4c41" // 2: brown
       "#f9a825" // 3: yellow
       "#c62828" // 4: red
       "#1565c0" // 5: blue
       "#6a1b9a" // 6: purple
       "#ef6c00" |] // 7: orange

let colorOf (index: int) =
    let n = palette.Length
    palette[((index % n) + n) % n]

let private num (v: float) =
    let r = Math.Round(v, 2)
    let r = if r = 0.0 then 0.0 else r // no "-0"
    r.ToString("0.##", CultureInfo.InvariantCulture)

let private points (g: Geometry) =
    g
    |> Seq.collect (function
        | Segment(p0, p1, _, _) -> [ p0; p1 ]
        | Polygon(vs, _) -> vs)

let render (options: Options) (geometry: Geometry) : string =
    let pts = points geometry |> Array.ofSeq

    let finite =
        pts
        |> Array.filter (fun p -> Double.IsFinite p.X && Double.IsFinite p.Y)

    let minX, maxX, minY, maxY =
        if finite.Length = 0 then
            0.0, 0.0, 0.0, 0.0
        else
            (finite |> Array.minBy _.X).X,
            (finite |> Array.maxBy _.X).X,
            (finite |> Array.minBy _.Y).Y,
            (finite |> Array.maxBy _.Y).Y

    let w, h = maxX - minX, maxY - minY
    let inner = options.Size - 2.0 * options.Margin
    let extent = max w h
    let scale = if extent > 0.0 then inner / extent else 1.0
    let width = w * scale + 2.0 * options.Margin
    let height = h * scale + 2.0 * options.Margin

    let px (p: Vec3) =
        options.Margin + (p.X - minX) * scale, options.Margin + (maxY - p.Y) * scale

    let sb = StringBuilder()
    let line (s: string) = sb.Append(s).Append('\n') |> ignore

    line
        $"""<svg xmlns="http://www.w3.org/2000/svg" width="%s{num width}" height="%s{num height}" viewBox="0 0 %s{num width} %s{num height}">"""

    options.Background
    |> Option.iter (fun bg -> line $"""<rect width="100%%" height="100%%" fill="%s{bg}"/>""")

    // Consecutive segments with the same style share one <path>; a segment
    // that starts where the previous one ended continues the subpath.
    let path = StringBuilder()
    let mutable style: (float * int) option = None
    let mutable lastEnd = ""

    let flush () =
        match style with
        | Some(width, color) ->
            line
                $"""<path d="%s{path.ToString()}" fill="none" stroke="%s{colorOf color}" stroke-width="%s{num width}" stroke-linecap="round" stroke-linejoin="round"/>"""

            path.Clear() |> ignore
            style <- None
            lastEnd <- ""
        | None -> ()

    for prim in geometry do
        match prim with
        | Segment(p0, p1, width, color) ->
            if style <> Some(width, color) then
                flush ()
                style <- Some(width, color)

            let x0, y0 = px p0
            let x1, y1 = px p1
            let start = $"%s{num x0} %s{num y0}"
            let finish = $"%s{num x1} %s{num y1}"

            if start <> lastEnd then
                (if path.Length > 0 then path.Append ' ' else path).Append($"M%s{start}") |> ignore

            path.Append($" L%s{finish}") |> ignore
            lastEnd <- finish
        | Polygon(vs, color) ->
            flush ()

            let pts =
                vs
                |> List.map (fun v ->
                    let x, y = px v
                    $"%s{num x},%s{num y}")
                |> String.concat " "

            line $"""<polygon points="%s{pts}" fill="%s{colorOf color}"/>"""

    flush ()
    line "</svg>"
    sb.ToString()
