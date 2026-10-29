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

let private num (v: float) =
    let r = Math.Round(v, 2)
    let r = if r = 0.0 then 0.0 else r // no "-0"
    r.ToString("0.##", CultureInfo.InvariantCulture)

let private points (g: Geometry) =
    g
    |> Seq.collect (function
        | Segment(p0, p1, _, _) -> [ p0; p1 ]
        | Polygon(vs, _) -> vs)

let private maxStrokeWidth (geometry: Geometry) =
    geometry
    |> List.fold
        (fun acc p ->
            match p with
            | Segment(_, _, w, _) -> max acc w
            | Polygon _ -> acc)
        0.0

/// Draws with `pad` pixels around the drawing (margin plus half the widest stroke).
let private draw (options: Options) (pad: float) (geometry: Geometry) : string =
    let pts = points geometry |> Array.ofSeq

    let minX, maxX, minY, maxY =
        if pts.Length = 0 then
            0.0, 0.0, 0.0, 0.0
        else
            (pts |> Array.minBy _.X).X,
            (pts |> Array.maxBy _.X).X,
            (pts |> Array.minBy _.Y).Y,
            (pts |> Array.maxBy _.Y).Y

    let w, h = maxX - minX, maxY - minY
    let inner = options.Size - 2.0 * pad
    let extent = max w h
    let scale = if extent > 0.0 then inner / extent else 1.0
    let width = w * scale + 2.0 * pad
    let height = h * scale + 2.0 * pad

    let px (p: Vec3) =
        pad + (p.X - minX) * scale, pad + (maxY - p.Y) * scale

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
                $"""<path d="%s{path.ToString()}" fill="none" stroke="%s{Palette.hex color}" stroke-width="%s{num width}" stroke-linecap="butt" stroke-linejoin="round"/>"""

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

            line $"""<polygon points="%s{pts}" fill="%s{Palette.hex color}"/>"""

    flush ()
    line "</svg>"
    sb.ToString()

/// Renders geometry as SVG. The geometry must be finite (the turtle
/// guarantees it). Strokes are centered on the segments and widths are in
/// pixels, so half the widest stroke is reserved on each side: line ends are
/// straight, but a stroke still extends sideways, and joins are round. Fails
/// if the margins and the widest stroke leave no room to draw: the drawing is
/// never silently clipped.
let render (options: Options) (geometry: Geometry) : Result<string, string> =
    let maxStroke = maxStrokeWidth geometry
    let pad = options.Margin + maxStroke / 2.0

    if options.Margin < 0.0 || not (options.Size > 2.0 * options.Margin) then
        Error $"the image size (%g{options.Size} px) must be greater than twice the margin (%g{options.Margin} px)"
    elif not (options.Size - 2.0 * pad > 0.0) then
        Error
            $"the widest line (%g{maxStroke} px) doesn't fit in a %g{options.Size} px image with %g{options.Margin} px margins; use a larger size or thinner lines"
    else
        Ok(draw options pad geometry)
